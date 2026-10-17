using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JxTerminal.Models;

namespace JxTerminal.Services.Monitoring;

public class LinuxProcMonitorProvider : ISystemMonitorProvider
{
    public string PlatformName => "Linux";

    private long _prevIdleTime = 0;
    private long _prevTotalTime = 0;
    private long _prevRxBytes = 0;
    private long _prevTxBytes = 0;
    private long _prevDiskReadSectors = 0;
    private long _prevDiskWriteSectors = 0;
    private DateTime _prevCheckTime = DateTime.MinValue;
    private string _gpuVendor = string.Empty;
    public int CoreCount { get; set; } = 1;

    public async Task<SystemStaticInfo> ProbeStaticInfoAsync(SshSession session, CancellationToken ct)
    {
        var info = new SystemStaticInfo();
        try
        {
            var cmd = "echo '>>>CPU_MODEL'; grep -m1 'model name' /proc/cpuinfo 2>/dev/null | cut -d: -f2; " +
                      "echo '>>>HOSTNAME'; hostname 2>/dev/null; " +
                      "echo '>>>OS_INFO'; uname -s; cat /etc/os-release 2>/dev/null | grep '^PRETTY_NAME=' | cut -d= -f2 | tr -d '\"'; " +
                      "echo '>>>CORE_COUNT'; grep -c '^processor' /proc/cpuinfo 2>/dev/null; " +
                      "echo '>>>THREADS'; lscpu 2>/dev/null | grep '^Thread(s) per core' | awk '{print $4}'";

            var output = await session.ExecuteCommandAsync(cmd);
            if (!string.IsNullOrWhiteSpace(output))
            {
                var lines = output.Split('\n', StringSplitOptions.TrimEntries);
                string section = "";
                var osLines = new List<string>();

                foreach (var line in lines)
                {
                    if (line.StartsWith(">>>"))
                    {
                        section = line;
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    switch (section)
                    {
                        case ">>>CPU_MODEL":
                            if (string.IsNullOrEmpty(info.CpuModel)) info.CpuModel = line.Trim();
                            break;
                        case ">>>HOSTNAME":
                            if (string.IsNullOrEmpty(info.Hostname)) info.Hostname = line.Trim();
                            break;
                        case ">>>OS_INFO":
                            osLines.Add(line.Trim());
                            break;
                        case ">>>CORE_COUNT":
                            if (int.TryParse(line.Trim(), out int cores) && cores > 0)
                            {
                                info.CpuCoreCount = cores;
                                CoreCount = cores;
                            }
                            break;
                        case ">>>THREADS":
                            if (int.TryParse(line.Trim(), out int threadsPerCore) && threadsPerCore > 0)
                            {
                                info.CpuThreadCount = info.CpuCoreCount * threadsPerCore;
                            }
                            break;
                    }
                }

                if (osLines.Count >= 2) info.SystemInfo = $"{osLines[0]} / {osLines[1]}";
                else if (osLines.Count == 1) info.SystemInfo = osLines[0];

                if (info.CpuThreadCount <= 0) info.CpuThreadCount = info.CpuCoreCount;
            }
        }
        catch { /* ignore probe failure */ }

        return info;
    }

    public async Task<SystemSnapshotData?> SampleMetricsAsync(SshSession session, CancellationToken ct)
    {
        if (!session.IsConnected) return null;

        // df -P forces POSIX one-line-per-fs output; without it a long filesystem name
        // wraps onto two lines and the tail/field parsing silently yields zeros.
        var cmd = "echo '>>>STAT'; grep '^cpu ' /proc/stat; " +
                  "echo '>>>MEM'; cat /proc/meminfo | grep -E '^(MemTotal|MemFree|MemAvailable|Buffers|Cached|SwapTotal|SwapFree):'; " +
                  "echo '>>>DISK_B'; df -P -B1 / 2>/dev/null | tail -n 1; " +
                  "echo '>>>NET'; cat /proc/net/dev 2>/dev/null; cat /proc/net/snmp 2>/dev/null | grep 'Tcp:' | tail -n 1; " +
                  "echo '>>>UPTIME'; uptime; " +
                  "echo '>>>TEMP'; cat /sys/class/thermal/thermal_zone*/temp 2>/dev/null | head -n 1; " +
                  "echo '>>>DISKIO'; grep -E ' (sd[a-z]|vd[a-z]|hd[a-z]|nvme[0-9]+n[0-9]+|xvd[a-z]|mmcblk[0-9]+) ' /proc/diskstats 2>/dev/null || cat /proc/diskstats 2>/dev/null; " +
                  "echo '>>>GPU'; if command -v nvidia-smi >/dev/null 2>&1; then echo 'NVIDIA'; " +
                  "nvidia-smi --query-gpu=utilization.gpu,temperature.gpu,power.draw,name,memory.used,memory.total,fan.speed --format=csv,noheader,nounits 2>/dev/null; " +
                  "else echo 'N/A'; fi; " +
                  "echo '>>>PROC'; P=$(COLUMNS=512 top -b -n 2 -d 0.2 -w 512 -c 2>/dev/null | awk '/^ *PID/ {p++} p==2 && substr($12, 1, 1) != \"[\" {print}' | head -n 35); if [ -z \"$P\" ]; then P=$(COLUMNS=512 top -b -n 2 -d 0.2 -w 512 2>/dev/null | awk '/^ *PID/ {p++} p==2 && substr($12, 1, 1) != \"[\" {print}' | head -n 35); fi; if [ -z \"$P\" ]; then P=$(top -b -n 2 -d 0.2 2>/dev/null | awk '/^ *PID/ {p++} p==2 && substr($12, 1, 1) != \"[\" {print}' | head -n 35); fi; if [ -z \"$P\" ]; then P=$(top -b -n 1 2>/dev/null | awk '/^ *PID/ {p=1} p && substr($12, 1, 1) != \"[\" {print}' | head -n 35); fi; if [ -n \"$P\" ]; then echo \"$P\"; else ps -Ao pid,user:16,%cpu,%mem,comm --sort=-%cpu,-%mem 2>/dev/null | tail -n +2 | head -n 35; fi; " +
                  "echo '>>>PORTS'; ss -tlnp 2>/dev/null | tail -n +2 | head -n 20 || ss -tln 2>/dev/null | tail -n +2 | head -n 20 || netstat -tln 2>/dev/null | tail -n +2 | head -n 20";

        var output = await session.ExecuteCommandAsync(cmd);
        if (string.IsNullOrWhiteSpace(output)) return null;

        return ParseOutput(output, DateTime.Now);
    }

    public SystemSnapshotData ParseOutput(string output, DateTime now)
    {
        var data = new SystemSnapshotData();
        if (string.IsNullOrWhiteSpace(output)) return data;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToArray();
        int section = 0;

        long currentRx = 0;
        long currentTx = 0;
        long currentDiskRead = 0;
        long currentDiskWrite = 0;
        long currentPacketsRecv = 0;
        long currentPacketsSent = 0;

        foreach (var line in lines)
        {
            if (line == ">>>STAT") { section = 1; continue; }
            if (line == ">>>MEM") { section = 2; continue; }
            if (line == ">>>DISK_H") { section = 3; continue; }
            if (line == ">>>DISK_B") { section = 11; continue; }
            if (line == ">>>NET") { section = 4; continue; }
            if (line == ">>>UPTIME") { section = 5; continue; }
            if (line == ">>>TEMP") { section = 6; continue; }
            if (line == ">>>DISKIO") { section = 7; continue; }
            if (line == ">>>GPU") { section = 8; _gpuVendor = ""; continue; }
            if (line == ">>>PROC") { section = 9; continue; }
            if (line == ">>>PORTS") { section = 10; continue; }

            try
            {
                switch (section)
                {
                    case 1: // CPU
                        if (line.StartsWith("cpu "))
                        {
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 5)
                            {
                                long idle = long.Parse(parts[4]);
                                long total = 0;
                                for (int i = 1; i < parts.Length; i++) total += long.Parse(parts[i]);

                                if (_prevTotalTime > 0)
                                {
                                    long deltaTotal = total - _prevTotalTime;
                                    long deltaIdle = idle - _prevIdleTime;
                                    if (deltaTotal > 0)
                                    {
                                        data.CpuUsage = Math.Clamp((1.0 - (double)deltaIdle / deltaTotal) * 100.0, 0.0, 100.0);
                                    }
                                }
                                _prevIdleTime = idle;
                                _prevTotalTime = total;
                            }
                        }
                        break;

                    case 2: // MEM
                        if (line.Contains(':'))
                        {
                            var colonIdx = line.IndexOf(':');
                            var key = line.Substring(0, colonIdx).Trim();
                            var valueStr = line.Substring(colonIdx + 1).Trim().Replace("kB", "").Trim();

                            if (long.TryParse(valueStr, out long valueKB))
                            {
                                double valueGB = valueKB / (1024.0 * 1024.0);
                                switch (key)
                                {
                                    case "MemTotal":
                                        data.MemTotal = valueGB;
                                        break;
                                    case "MemFree":
                                        data.MemFree = valueGB;
                                        break;
                                    case "MemAvailable":
                                        if (data.MemTotal > 0)
                                            data.RamUsage = Math.Clamp(((data.MemTotal - valueGB) / data.MemTotal) * 100.0, 0.0, 100.0);
                                        data.MemUsed = Math.Max(0, data.MemTotal - valueGB);
                                        break;
                                    case "Buffers":
                                        data.MemBuffers = valueGB;
                                        break;
                                    case "Cached":
                                        data.MemCached = valueGB;
                                        break;
                                    case "SwapTotal":
                                        data.SwapTotal = valueGB;
                                        break;
                                    case "SwapFree":
                                        if (data.SwapTotal > 0)
                                            data.SwapUsed = Math.Max(0, data.SwapTotal - valueGB);
                                        break;
                                }
                            }
                        }
                        break;

                    case 3: // DISK_H
                        if (!line.StartsWith("Filesystem"))
                        {
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 6 && parts[4].Contains('%'))
                            {
                                var useStr = parts[4].Replace("%", "");
                                if (double.TryParse(useStr, out double usage))
                                    data.DiskUsage = usage;
                                data.DiskPath = parts[5];
                            }
                        }
                        break;

                    case 11: // DISK_B
                        if (!line.StartsWith("Filesystem"))
                        {
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 4)
                            {
                                if (long.TryParse(parts[1], out long totalBytes))
                                    data.DiskTotal = totalBytes / (1024.0 * 1024 * 1024);
                                if (long.TryParse(parts[2], out long usedBytes))
                                    data.DiskUsedBytes = usedBytes / (1024.0 * 1024 * 1024);
                            }
                            if (parts.Length >= 6 && parts[4].Contains('%'))
                            {
                                var useStr = parts[4].Replace("%", "");
                                if (double.TryParse(useStr, out double usage))
                                    data.DiskUsage = usage;
                                data.DiskPath = parts[5];
                            }
                        }
                        break;

                    case 4: // NET
                        if (line.Contains(':') && !line.Trim().StartsWith("Tcp"))
                        {
                            if (!line.Trim().StartsWith("lo") && !line.Contains("loopback"))
                            {
                                var raw = line.Substring(line.IndexOf(':') + 1).Trim();
                                var nums = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                if (nums.Length >= 9)
                                {
                                    currentRx += long.Parse(nums[0]);
                                    currentTx += long.Parse(nums[8]);
                                    currentPacketsRecv += long.Parse(nums[1]);
                                    currentPacketsSent += long.Parse(nums[9]);
                                }
                            }
                        }
                        else if (line.StartsWith("Tcp:") && line.Contains(' '))
                        {
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 10 && int.TryParse(parts[9], out int estab))
                                data.TcpConnections = estab;
                        }
                        else if (!line.Contains(':') && int.TryParse(line.Trim(), out int connCount))
                        {
                            data.TcpConnections = connCount;
                        }
                        break;

                    case 5: // UPTIME
                        if (line.Contains("load average:"))
                        {
                            var loadIdx = line.IndexOf("load average:");
                            var loadStr = line.Substring(loadIdx + 13).Trim();
                            var loads = loadStr.Split(',');
                            if (loads.Length >= 1 && double.TryParse(loads[0].Trim(), out double l1)) data.Load1 = l1;
                            if (loads.Length >= 2 && double.TryParse(loads[1].Trim(), out double l5)) data.Load5 = l5;
                            if (loads.Length >= 3 && double.TryParse(loads[2].Trim(), out double l15)) data.Load15 = l15;

                            var upIdx = line.IndexOf("up ");
                            if (upIdx != -1)
                            {
                                var endIdx = line.IndexOf(',', upIdx);
                                if (endIdx == -1) endIdx = line.IndexOf("user", upIdx);
                                if (endIdx > upIdx)
                                {
                                    data.Uptime = line.Substring(upIdx, endIdx - upIdx).Trim();
                                }
                            }
                        }
                        else
                        {
                            data.Uptime = line;
                        }
                        break;

                    case 6: // TEMP
                        if (double.TryParse(line, out double temp))
                        {
                            data.CpuTemp = temp / 1000.0;
                        }
                        break;

                    case 7: // DISKIO
                        var dParts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (dParts.Length >= 10)
                        {
                            var dev = dParts[2];
                            bool isDisk = false;
                            if ((dev.StartsWith("sd") || dev.StartsWith("vd") || dev.StartsWith("hd") || dev.StartsWith("xvd")) && dev.Length <= 5) isDisk = true;
                            if (dev.StartsWith("nvme") && dev.Contains('n') && !dev.Contains('p')) isDisk = true;
                            if (dev.StartsWith("mmcblk") && !dev.Contains('p')) isDisk = true;

                            if (isDisk && long.TryParse(dParts[5], out long r) && long.TryParse(dParts[9], out long w))
                            {
                                currentDiskRead += r;
                                currentDiskWrite += w;
                            }
                        }
                        break;

                    case 8: // GPU
                        if (line == "N/A")
                        {
                            data.GpuDetected = false;
                        }
                        else if (line is "NVIDIA" or "AMD" or "INTEL")
                        {
                            _gpuVendor = line;
                            data.GpuDetected = true;
                        }
                        else if (_gpuVendor == "NVIDIA")
                        {
                            var gParts = line.Split(',');
                            if (gParts.Length >= 4)
                            {
                                if (double.TryParse(gParts[0], out double gu)) data.GpuUsage = gu;
                                if (double.TryParse(gParts[1], out double gt)) data.GpuTemp = gt;
                                if (double.TryParse(gParts[2], out double gp)) data.GpuPower = gp;
                                data.GpuModel = gParts[3].Trim();

                                if (gParts.Length >= 6)
                                {
                                    if (double.TryParse(gParts[4], out double memUsed)) data.GpuMemoryUsed = memUsed / 1024.0;
                                    if (double.TryParse(gParts[5], out double memTotal)) data.GpuMemoryTotal = memTotal / 1024.0;
                                }
                                if (gParts.Length >= 7 && int.TryParse(gParts[6].Trim(), out int fan))
                                {
                                    data.GpuFanSpeed = fan;
                                }
                            }
                        }
                        break;

                    case 9: // PROC
                        var procParts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (procParts.Length >= 4)
                        {
                            int pid = 0;
                            double rawCpu = 0;
                            double rawMem = 0;
                            string user = "";
                            string name = "";

                            if (int.TryParse(procParts[0], out pid) && procParts.Length >= 10)
                            {
                                // top -b format: PID USER PR NI VIRT RES SHR S %CPU %MEM TIME+ COMMAND
                                user = procParts.Length > 1 ? procParts[1] : "";
                                double.TryParse(procParts[8].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out rawCpu);
                                double.TryParse(procParts[9].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out rawMem);
                                if (procParts.Length >= 12)
                                {
                                    var cmd = string.Join(" ", procParts.Skip(11));
                                    var firstToken = cmd.Split(' ', 2)[0];
                                    name = firstToken.StartsWith("[") ? firstToken : System.IO.Path.GetFileName(firstToken);
                                }
                                else if (procParts.Length >= 11)
                                {
                                    var firstToken = procParts[10].Split(' ', 2)[0];
                                    name = firstToken.StartsWith("[") ? firstToken : System.IO.Path.GetFileName(firstToken);
                                }
                                else
                                {
                                    name = $"pid-{pid}";
                                }
                            }
                            else if (procParts.Length >= 11 && int.TryParse(procParts[1], out pid))
                            {
                                // ps aux format: USER PID %CPU %MEM VSZ RSS TTY STAT START TIME COMMAND
                                user = procParts[0];
                                double.TryParse(procParts[2].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out rawCpu);
                                double.TryParse(procParts[3].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out rawMem);
                                var cmd = procParts[10];
                                var firstToken = cmd.Split(' ', 2)[0];
                                name = firstToken.StartsWith("[") ? firstToken : System.IO.Path.GetFileName(firstToken);
                            }
                            else if (int.TryParse(procParts[1], out pid))
                            {
                                user = procParts[0];
                                double.TryParse(procParts[2].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out rawCpu);
                                double.TryParse(procParts[3].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out rawMem);
                                var cmd = string.Join(" ", procParts.Skip(4));
                                var firstToken = cmd.Split(' ', 2)[0];
                                name = firstToken.StartsWith("[") ? firstToken : System.IO.Path.GetFileName(firstToken);
                            }
                            else if (int.TryParse(procParts[0], out pid))
                            {
                                user = procParts.Length > 1 ? procParts[1] : "";
                                if (procParts.Length > 2) double.TryParse(procParts[2].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out rawCpu);
                                if (procParts.Length > 3) double.TryParse(procParts[3].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out rawMem);
                                var firstToken = procParts.Length > 4 ? procParts[4] : "";
                                name = firstToken.StartsWith("[") ? firstToken : System.IO.Path.GetFileName(firstToken);
                            }

                            if (pid <= 0)
                                continue;

                            name = name.Trim('(', ')').TrimEnd('+');

                            // Filter out transient monitor commands if any slipped through
                            if (name is "top" or "ps" or "grep" or "head" or "tail" or "cat" or "cut" or "awk" or "sed" or "ss" or "sh" or "bash")
                                continue;

                            // Filter out Linux kernel threads (kthreadd, kworker, pool_workqueue, rcu_sched, etc.)
                            if (name.StartsWith("[") || name.EndsWith("]") || name.StartsWith("kworker") || name == "kthreadd" ||
                                name.StartsWith("pool_wo") || name.StartsWith("ksoftirqd") || name.StartsWith("migration") ||
                                name.StartsWith("rcu_") || name.StartsWith("cpuhp") || name.StartsWith("idle_inject") ||
                                name.StartsWith("kdevtmpfs") || name.StartsWith("netns") || name.StartsWith("khungtaskd") ||
                                name.StartsWith("oom_reaper") || name.StartsWith("writeback") || name.StartsWith("kcompactd") ||
                                name.StartsWith("kauditd") || name.StartsWith("khugepaged") || name.StartsWith("scsi_") ||
                                name.StartsWith("jbd2/") || name.StartsWith("ext4-") || name.StartsWith("xfs-"))
                                continue;

                            if (data.TopProcesses.Count >= 10)
                                continue;

                            data.TopProcesses.Add(new ProcessInfo
                            {
                                User = user,
                                Pid = pid,
                                CpuPercent = Math.Round(Math.Max(0.0, rawCpu), 1),
                                MemPercent = Math.Clamp(Math.Round(rawMem, 1), 0.0, 100.0),
                                Name = string.IsNullOrWhiteSpace(name) ? $"pid-{pid}" : name
                            });
                        }
                        break;

                    case 10: // PORTS
                        var portParts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (portParts.Length >= 4 && portParts[0] == "LISTEN")
                        {
                            var localAddr = portParts[3];
                            var colonIdx = localAddr.LastIndexOf(':');
                            if (colonIdx >= 0 && int.TryParse(localAddr.Substring(colonIdx + 1), out int port))
                            {
                                var rawProc = portParts.Length > 5 ? string.Join(" ", portParts.Skip(5)) : string.Empty;
                                data.ListeningPorts.Add(new PortInfo
                                {
                                    Port = port,
                                    Protocol = "TCP",
                                    State = "LISTEN",
                                    LocalAddress = localAddr.Substring(0, colonIdx),
                                    Process = ExtractListeningProcessName(rawProc)
                                });
                            }
                        }
                        else if (portParts.Length >= 6 && (portParts[0] == "tcp" || portParts[0] == "tcp6") && portParts[5] == "LISTEN")
                        {
                            // netstat fallback: tcp 0 0 0.0.0.0:22 0.0.0.0:* LISTEN 800/sshd
                            var localAddr = portParts[3];
                            var colonIdx = localAddr.LastIndexOf(':');
                            if (colonIdx >= 0 && int.TryParse(localAddr.Substring(colonIdx + 1), out int port))
                            {
                                var rawProc = portParts.Length > 6 ? string.Join(" ", portParts.Skip(6)) : string.Empty;
                                data.ListeningPorts.Add(new PortInfo
                                {
                                    Port = port,
                                    Protocol = "TCP",
                                    State = "LISTEN",
                                    LocalAddress = localAddr.Substring(0, colonIdx),
                                    Process = ExtractListeningProcessName(rawProc)
                                });
                            }
                        }
                        break;
                }
            }
            catch { /* protect per-line parsing */ }
        }

        if (_prevCheckTime != DateTime.MinValue)
        {
            var seconds = (now - _prevCheckTime).TotalSeconds;
            if (seconds > 0)
            {
                data.NetworkDown = FormatSpeed((currentRx - _prevRxBytes) / seconds);
                data.NetworkUp = FormatSpeed((currentTx - _prevTxBytes) / seconds);
                data.DiskReadSpeed = FormatSpeed((currentDiskRead - _prevDiskReadSectors) * 512.0 / seconds);
                data.DiskWriteSpeed = FormatSpeed((currentDiskWrite - _prevDiskWriteSectors) * 512.0 / seconds);
                data.DiskReadIops = (long)((currentDiskRead - _prevDiskReadSectors) / seconds);
                data.DiskWriteIops = (long)((currentDiskWrite - _prevDiskWriteSectors) / seconds);
            }
        }

        data.NetTotalRecv = FormatSize(currentRx);
        data.NetTotalSent = FormatSize(currentTx);
        data.TotalPacketsRecv = currentPacketsRecv;
        data.TotalPacketsSent = currentPacketsSent;

        _prevCheckTime = now;
        _prevRxBytes = currentRx;
        _prevTxBytes = currentTx;
        _prevDiskReadSectors = currentDiskRead;
        _prevDiskWriteSectors = currentDiskWrite;

        return data;
    }

    public static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec < 0) bytesPerSec = 0;
        if (bytesPerSec < 1024) return $"{bytesPerSec:0} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024.0:0.0} KB/s";
        if (bytesPerSec < 1024 * 1024 * 1024) return $"{bytesPerSec / (1024.0 * 1024.0):0.0} MB/s";
        return $"{bytesPerSec / (1024.0 * 1024.0 * 1024.0):0.0} GB/s";
    }

    public static string FormatSize(double bytes)
    {
        if (bytes < 0) bytes = 0;
        if (bytes < 1024) return $"{bytes:0} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):0.0} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):0.0} GB";
    }

    public static string ExtractListeningProcessName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "-") return "-";

        // Format 1: ss -tlnp -> users:(("sshd",pid=800,fd=3)) or users:(("nginx",pid=901,fd=6),("nginx",pid=900,fd=6))
        var firstQuote = raw.IndexOf('"');
        if (firstQuote >= 0)
        {
            var secondQuote = raw.IndexOf('"', firstQuote + 1);
            if (secondQuote > firstQuote)
            {
                var procName = raw.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                return System.IO.Path.GetFileName(procName);
            }
        }

        // Format 2: netstat -tlnp -> 800/sshd or 1234/python3
        var slashIdx = raw.IndexOf('/');
        if (slashIdx >= 0 && slashIdx < raw.Length - 1)
        {
            var afterSlash = raw.Substring(slashIdx + 1).Trim();
            return System.IO.Path.GetFileName(afterSlash.Split(' ', 2)[0]);
        }

        return raw.Trim();
    }
}
