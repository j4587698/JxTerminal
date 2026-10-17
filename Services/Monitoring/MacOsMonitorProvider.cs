using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JxTerminal.Models;

namespace JxTerminal.Services.Monitoring;

public class MacOsMonitorProvider : ISystemMonitorProvider
{
    public string PlatformName => "macOS";

    private long _prevRxBytes = 0;
    private long _prevTxBytes = 0;
    private DateTime _prevCheckTime = DateTime.MinValue;

    public async Task<SystemStaticInfo> ProbeStaticInfoAsync(SshSession session, CancellationToken ct)
    {
        var info = new SystemStaticInfo();
        try
        {
            var model = await session.ExecuteCommandAsync("sysctl -n machdep.cpu.brand_string");
            if (!string.IsNullOrWhiteSpace(model)) info.CpuModel = model.Trim();

            var hostname = await session.ExecuteCommandAsync("hostname");
            if (!string.IsNullOrWhiteSpace(hostname)) info.Hostname = hostname.Trim();

            var os = await session.ExecuteCommandAsync("sw_vers -productName; sw_vers -productVersion");
            var osParts = os.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (osParts.Length >= 2) info.SystemInfo = $"{osParts[0].Trim()} {osParts[1].Trim()}";

            var cores = await session.ExecuteCommandAsync("sysctl -n hw.physicalcpu");
            if (int.TryParse(cores.Trim(), out int c)) info.CpuCoreCount = c;

            var threads = await session.ExecuteCommandAsync("sysctl -n hw.logicalcpu");
            if (int.TryParse(threads.Trim(), out int t)) info.CpuThreadCount = t;
        }
        catch { /* ignore probe failure */ }

        return info;
    }

    public async Task<SystemSnapshotData?> SampleMetricsAsync(SshSession session, CancellationToken ct)
    {
        if (!session.IsConnected) return null;

        // hw.pagesize matters: vm_stat counts pages, which are 16K on Apple Silicon,
        // not the historical 4K — hardcoding 4096 underreports memory 4x there.
        var cmd = "echo '>>>CPU'; top -l 1 -n 0 2>/dev/null | grep 'CPU usage'; " +
                  "echo '>>>MEM'; sysctl hw.memsize hw.pagesize 2>/dev/null; vm_stat 2>/dev/null; " +
                  "echo '>>>DISK'; df -P -h / 2>/dev/null | tail -n 1; " +
                  "echo '>>>NET'; netstat -ib -n 2>/dev/null | grep Link; " +
                  "echo '>>>UPTIME'; uptime";

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
        long totalMem = 0;
        long freePages = 0;
        long activePages = 0;
        long wiredPages = 0;
        long speculativePages = 0;
        long compressorPages = 0;
        long pageSize = 4096; // overridden by hw.pagesize (16384 on Apple Silicon)

        long currentRx = 0;
        long currentTx = 0;

        foreach (var line in lines)
        {
            if (line == ">>>CPU") { section = 1; continue; }
            if (line == ">>>MEM") { section = 2; continue; }
            if (line == ">>>DISK") { section = 3; continue; }
            if (line == ">>>NET") { section = 4; continue; }
            if (line == ">>>UPTIME") { section = 5; continue; }

            try
            {
                switch (section)
                {
                    case 1: // CPU
                        if (line.StartsWith("CPU usage:"))
                        {
                            var parts = line.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int i = 0; i < parts.Length; i++)
                            {
                                if (parts[i] == "idle" && i > 0)
                                {
                                    var valStr = parts[i - 1].Replace("%", "");
                                    if (double.TryParse(valStr, out double idle))
                                    {
                                        data.CpuUsage = Math.Clamp(100.0 - idle, 0.0, 100.0);
                                    }
                                    break;
                                }
                            }
                        }
                        break;

                    case 2: // MEM
                        if (line.StartsWith("hw.memsize:"))
                        {
                            var parts = line.Split(':');
                            if (parts.Length > 1 && long.TryParse(parts[1].Trim(), out totalMem))
                            {
                                data.MemTotal = totalMem / (1024.0 * 1024 * 1024);
                            }
                        }
                        else if (line.StartsWith("hw.pagesize:"))
                        {
                            var parts = line.Split(':');
                            if (parts.Length > 1 && long.TryParse(parts[1].Trim(), out long ps) && ps > 0)
                                pageSize = ps;
                        }
                        else if (line.StartsWith("Pages free:"))
                            long.TryParse(line.Split(':')[1].Trim().TrimEnd('.'), out freePages);
                        else if (line.StartsWith("Pages active:"))
                            long.TryParse(line.Split(':')[1].Trim().TrimEnd('.'), out activePages);
                        else if (line.StartsWith("Pages wired down:"))
                            long.TryParse(line.Split(':')[1].Trim().TrimEnd('.'), out wiredPages);
                        else if (line.StartsWith("Pages speculative:"))
                            long.TryParse(line.Split(':')[1].Trim().TrimEnd('.'), out speculativePages);
                        else if (line.StartsWith("Pages occupied by compressor:"))
                            long.TryParse(line.Split(':')[1].Trim().TrimEnd('.'), out compressorPages);
                        break;

                    case 3: // DISK
                        if (!line.StartsWith("Filesystem"))
                        {
                            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var p in parts)
                            {
                                if (p.EndsWith('%') && double.TryParse(p.TrimEnd('%'), out double cap))
                                {
                                    data.DiskUsage = cap;
                                    break;
                                }
                            }
                            if (parts.Length > 0) data.DiskPath = parts[^1];
                        }
                        break;

                    case 4: // NET
                        if (line.Contains("Link"))
                        {
                            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 10)
                            {
                                if (long.TryParse(parts[6], out long rx)) currentRx += rx;
                                if (long.TryParse(parts[9], out long tx)) currentTx += tx;
                            }
                        }
                        break;

                    case 5: // UPTIME
                        var upIdx = line.IndexOf("up ");
                        if (upIdx != -1)
                        {
                            data.Uptime = line.Substring(upIdx);
                        }
                        break;
                }
            }
            catch { /* protect per-line parsing */ }
        }

        if (totalMem > 0)
        {
            // "Used" the way top/Activity Monitor report it: active + wired + compressed.
            // Fall back to total-(free+speculative) when vm_stat fields were missing.
            long usedBytes;
            long usedPages = activePages + wiredPages + compressorPages;
            if (usedPages > 0)
            {
                usedBytes = usedPages * pageSize;
            }
            else
            {
                long freeBytes = (freePages + speculativePages) * pageSize;
                usedBytes = Math.Max(0, totalMem - freeBytes);
            }
            data.RamUsage = Math.Clamp(((double)usedBytes / totalMem) * 100.0, 0.0, 100.0);
            data.MemUsed = usedBytes / (1024.0 * 1024 * 1024);
            data.MemFree = Math.Max(0, totalMem - usedBytes) / (1024.0 * 1024 * 1024);
        }

        if (_prevCheckTime != DateTime.MinValue)
        {
            var seconds = (now - _prevCheckTime).TotalSeconds;
            if (seconds > 0)
            {
                data.NetworkDown = LinuxProcMonitorProvider.FormatSpeed((currentRx - _prevRxBytes) / seconds);
                data.NetworkUp = LinuxProcMonitorProvider.FormatSpeed((currentTx - _prevTxBytes) / seconds);
            }
        }

        data.NetTotalRecv = LinuxProcMonitorProvider.FormatSize(currentRx);
        data.NetTotalSent = LinuxProcMonitorProvider.FormatSize(currentTx);

        _prevCheckTime = now;
        _prevRxBytes = currentRx;
        _prevTxBytes = currentTx;

        return data;
    }
}
