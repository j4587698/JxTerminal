using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JxTerminal.Controls;
using JxTerminal.Models;
using JxTerminal.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Linq;

namespace JxTerminal.ViewModels;

public partial class TerminalViewModel : ViewModelBase, IDisposable, ITerminalController
{
    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _showOverlay;

    // SFTP & Tools
    [ObservableProperty]
    private SftpViewModel? _sftp;

    [ObservableProperty]
    private bool _isSftpVisible;

    [ObservableProperty]
    private bool _isSystemInfoVisible = true; // Default to true for modern look

    // Find in terminal
    [ObservableProperty]
    private bool _isSearchVisible;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _searchMatchCase;

    partial void OnIsSearchVisibleChanged(bool value)
    {
        // Dropping the query clears the highlights in the terminal control.
        if (!value) SearchText = string.Empty;
    }

    [RelayCommand]
    private void ToggleSearch() => IsSearchVisible = !IsSearchVisible;

    [RelayCommand]
    private void CloseSearch() => IsSearchVisible = false;

    // System Info (Mockup for UI dev, real implementation requires SSH command parsing)
    [ObservableProperty]
    private double _cpuUsage;

    [ObservableProperty]
    private double _ramUsage;
    
    [ObservableProperty]
    private double _gpuUsage;

    [ObservableProperty]
    private double _diskUsage; // Percentage

    [ObservableProperty]
    private string _networkUp = "0 KB/s";

    [ObservableProperty]
    private string _networkDown = "0 KB/s";

    [ObservableProperty]
    private string _uptime = "0d 0h 0m";

    // Overview page data
    [ObservableProperty] private string _hostname = "Loading...";
    [ObservableProperty] private string _systemInfo = "Loading...";

    // Detailed Hardware Info
    [ObservableProperty] private string _cpuModel = "Loading...";
    [ObservableProperty] private string _cpuClock = "";
    [ObservableProperty] private double _cpuTemp = 0;
    [ObservableProperty] private int _cpuCoreCount = 0;
    [ObservableProperty] private int _cpuThreadCount = 0;
    [ObservableProperty] private double _load1 = 0;
    [ObservableProperty] private double _load5 = 0;
    [ObservableProperty] private double _load15 = 0;
    
    [ObservableProperty] private string _gpuModel = "N/A";
    [ObservableProperty] private double _gpuTemp = 0;
    [ObservableProperty] private int _gpuFanSpeed = 0; // RPM or %
    [ObservableProperty] private double _gpuPower = 0; // Watts
    [ObservableProperty] private bool _gpuDetected = false;
    [ObservableProperty] private double _gpuMemoryUsed = 0; // GB
    [ObservableProperty] private double _gpuMemoryTotal = 0; // GB
    
    [ObservableProperty] private string _diskReadSpeed = "0 B/s";
    [ObservableProperty] private string _diskWriteSpeed = "0 B/s";
    [ObservableProperty] private long _diskReadIops = 0;
    [ObservableProperty] private long _diskWriteIops = 0;
    [ObservableProperty] private string _diskPath = "/";
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskTotalFormatted))]
    private double _diskTotal = 0; // GB
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskUsedBytesFormatted))]
    private double _diskUsedBytes = 0; // GB
    
    [ObservableProperty] private string _netTotalSent = "0 GB";
    [ObservableProperty] private string _netTotalRecv = "0 GB";
    [ObservableProperty] private int _tcpConnections = 0;
    [ObservableProperty] private long _totalPacketsSent = 0;
    [ObservableProperty] private long _totalPacketsRecv = 0;
    
    // Memory detailed info
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemTotalFormatted))]
    private double _memTotal = 0; // GB
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemUsedFormatted))]
    private double _memUsed = 0; // GB
    
    [ObservableProperty] private double _memBuffers = 0; // GB
    [ObservableProperty] private double _memFree = 0; // GB
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemCachedFormatted))]
    private double _memCached = 0; // GB
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwapTotalFormatted))]
    private double _swapTotal = 0; // GB
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwapUsedFormatted))]
    private double _swapUsed = 0; // GB
    
    // Formatted memory strings for UI (to avoid locale issues)
    public string MemTotalFormatted => MemTotal.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB";
    public string MemUsedFormatted => MemUsed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB";
    public string MemCachedFormatted => MemCached.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB";
    public string SwapTotalFormatted => SwapTotal.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB";
    public string SwapUsedFormatted => SwapUsed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB";
    public string DiskTotalFormatted => DiskTotal.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB";
    public string DiskUsedBytesFormatted => DiskUsedBytes.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB";
    
    // Ping latency
    [ObservableProperty] private double _pingLatency = 0; // ms
    [ObservableProperty] private string _pingStatus = "-";
    
    // Process list (top CPU consumers)
    public ObservableCollection<ProcessInfo> TopProcesses { get; } = new();
    
    // Network listening ports
    public ObservableCollection<PortInfo> ListeningPorts { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTabOverview))]
    [NotifyPropertyChangedFor(nameof(IsTabCpu))]
    [NotifyPropertyChangedFor(nameof(IsTabGpu))]
    [NotifyPropertyChangedFor(nameof(IsTabMemory))]
    [NotifyPropertyChangedFor(nameof(IsTabDisk))]
    [NotifyPropertyChangedFor(nameof(IsTabNetwork))]
    [NotifyPropertyChangedFor(nameof(IsTabSftp))]
    private int _selectedSidebarIndex = 0;

    public bool IsTabOverview => SelectedSidebarIndex == 0;
    public bool IsTabCpu => SelectedSidebarIndex == 1;
    public bool IsTabGpu => SelectedSidebarIndex == 2;
    public bool IsTabMemory => SelectedSidebarIndex == 3;
    public bool IsTabDisk => SelectedSidebarIndex == 4;
    public bool IsTabNetwork => SelectedSidebarIndex == 5;
    public bool IsTabSftp => SelectedSidebarIndex == 6;

    [RelayCommand]
    private void SelectTab(string indexStr)
    {
        if (int.TryParse(indexStr, out int index))
        {
            SelectedSidebarIndex = index;
        }
    }

    public bool IsSshSession => Session is SshSession;

    public ITerminalSession Session { get; }
    public TerminalSettings Settings { get; }
    public SavedConnection? ConnectionInfo { get; }
    
    // The terminal state (buffer, cursor, etc.) persists here
    public XTerm.Terminal Terminal { get; }
    
    public event Action? ScreenUpdated;

    public event Action<TerminalViewModel>? RequestClose;

    public TerminalViewModel(ITerminalSession session, TerminalSettings settings, SavedConnection? connectionInfo = null)
    {
        Session = session;
        Settings = settings;
        ConnectionInfo = connectionInfo;
        
        // Initialize Terminal State
        var options = new XTerm.Options.TerminalOptions
        {
            Scrollback = settings.ScrollbackLines,
            Cols = 80, 
            Rows = 24
        };
        Terminal = new XTerm.Terminal(options);
        
        // Subscribe session data directly to Terminal
        Session.DataReceived += OnDataReceived;
        
        if (session is SshSession ssh)
        {
            Title = ssh.Host;
            // Initialize SFTP if available
            Sftp = new SftpViewModel(ssh);
            _ = Sftp.RefreshFiles(); // Initial load
            
            // Start system info monitoring
            _ = MonitorSystemInfo();
        }
        else if (session is LocalShellSession || session is PtyLocalShellSession)
            Title = Services.LocalizationService.Instance["QuickLocal"];
        else
            Title = Services.LocalizationService.Instance["Terminal"];

        // Subscribe to events
        Session.Connected += OnConnected;
        Session.Disconnected += OnDisconnected;
        Session.ErrorOccurred += OnError;

        if (Session.IsConnected)
        {
            OnConnected();
        }
        else
        {
            _ = ConnectAsync();
        }
    }

    private long _prevIdleTime = 0;
    private long _prevTotalTime = 0;
    private long _prevRxBytes = 0;
    private long _prevTxBytes = 0;
    private long _prevDiskReadSectors = 0;
    private long _prevDiskWriteSectors = 0;
    private DateTime _prevCheckTime = DateTime.MinValue;
    private string _gpuVendor = "";
    private int _intelGpuLineIndex = 0;

    private async Task MonitorSystemInfo()
    {
        if (!(Session is SshSession sshSession))
            return;

        while (!IsConnected) await Task.Delay(100);

        try
        {
            var uname = await sshSession.ExecuteCommandAsync("uname");
            if (uname.Contains("Darwin"))
            {
                await MonitorMacSystemInfo(sshSession);
            }
            else
            {
                await MonitorLinuxSystemInfo(sshSession);
            }
        }
        catch 
        {
            // Fallback or error logging
        }
    }

    private async Task MonitorLinuxSystemInfo(SshSession sshSession)
    {
        try
        {
            // Get static info once
            var model = await sshSession.ExecuteCommandAsync("grep -m1 'model name' /proc/cpuinfo | cut -d: -f2");
            if (!string.IsNullOrWhiteSpace(model)) CpuModel = model.Trim();
            
            var hostname = await sshSession.ExecuteCommandAsync("hostname");
            if (!string.IsNullOrWhiteSpace(hostname)) Hostname = hostname.Trim();
            
            var osInfo = await sshSession.ExecuteCommandAsync("uname -s; cat /etc/os-release 2>/dev/null | grep PRETTY_NAME | cut -d= -f2 | tr -d '\"'");
            var osLines = osInfo.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (osLines.Length >= 2) SystemInfo = $"{osLines[0].Trim()} / {osLines[1].Trim()}";
            
            var coreCount = await sshSession.ExecuteCommandAsync("grep -c '^processor' /proc/cpuinfo");
            if (int.TryParse(coreCount.Trim(), out int cores)) CpuCoreCount = cores;
            
            var threadInfo = await sshSession.ExecuteCommandAsync("lscpu | grep '^Thread(s) per core' | awk '{print $4}'");
            if (int.TryParse(threadInfo.Trim(), out int threadsPerCore)) 
                CpuThreadCount = CpuCoreCount * threadsPerCore;
            else 
                CpuThreadCount = CpuCoreCount; // fallback
        } 
        catch { }

        while (IsConnected)
        {
            try
            {
                // Main monitoring command with process and port info
                // Use /proc/meminfo instead of free -b for more reliable memory parsing
                // Use separate markers for df -h (DISK_H) and df -B1 (DISK_B) to avoid parsing confusion
                var cmd = "echo '>>>STAT'; grep '^cpu ' /proc/stat; echo '>>>MEM'; cat /proc/meminfo | grep -E '^(MemTotal|MemFree|MemAvailable|Buffers|Cached|SwapTotal|SwapFree):'; echo '>>>DISK_H'; df -h / | tail -n 1; echo '>>>DISK_B'; df -B1 / | tail -n 1; echo '>>>NET'; cat /proc/net/dev; cat /proc/net/snmp | grep 'Tcp:' | tail -n 1; ss -tan | grep ESTAB | wc -l; echo '>>>UPTIME'; uptime; echo '>>>TEMP'; cat /sys/class/thermal/thermal_zone*/temp 2>/dev/null | head -n 1; echo '>>>DISKIO'; cat /proc/diskstats; echo '>>>GPU'; if command -v nvidia-smi >/dev/null 2>&1; then echo 'NVIDIA'; nvidia-smi --query-gpu=utilization.gpu,temperature.gpu,power.draw,name,memory.used,memory.total,fan.speed --format=csv,noheader,nounits 2>/dev/null; elif command -v rocm-smi >/dev/null 2>&1; then echo 'AMD'; rocm-smi --showuse --showtemp --showmeminfo vram --showpower --showproductname --csv 2>/dev/null; elif [ -d /sys/class/drm/card0/device ]; then echo 'INTEL'; cat /sys/class/drm/card0/device/vendor 2>/dev/null; cat /sys/class/drm/card0/gt/gt_cur_freq_mhz 2>/dev/null || echo '0'; cat /sys/class/drm/card0/device/mem_info_vram_used 2>/dev/null || echo '0'; cat /sys/class/drm/card0/device/mem_info_vram_total 2>/dev/null || echo '0'; else echo 'N/A'; fi; echo '>>>PROC'; ps aux --sort=-%cpu | head -n 11 | tail -n 10; echo '>>>PORTS'; ss -tlnp 2>/dev/null | tail -n +2 | head -n 20";
                
                var output = await sshSession.ExecuteCommandAsync(cmd);
                
                ParseSystemInfo(output);
                
                // Ping measurement using SSH round-trip time with high-precision Stopwatch
                try
                {
                    var sw = Stopwatch.StartNew();
                    await sshSession.ExecuteCommandAsync("echo");
                    sw.Stop();
                    PingLatency = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
                    PingStatus = $"{PingLatency:0.0} ms";
                }
                catch
                {
                    PingStatus = "-";
                }
            }
            catch
            {
                // Ignore errors
            }
            
            await Task.Delay(2000);
        }
    }

    private async Task MonitorMacSystemInfo(SshSession sshSession)
    {
        try
        {
            var model = await sshSession.ExecuteCommandAsync("sysctl -n machdep.cpu.brand_string");
            if (!string.IsNullOrWhiteSpace(model)) CpuModel = model.Trim();
            
            // Initial Net Stats
            var netInit = await sshSession.ExecuteCommandAsync("netstat -ib -n | grep Link");
            ParseMacNetwork(netInit, true);
        } 
        catch { }

        while (IsConnected)
        {
            try
            {
                // Mac Commands
                // CPU: top -l 1 -n 0 | grep "CPU usage"
                // MEM: sysctl hw.memsize && vm_stat
                // DISK: df -h /
                // NET: netstat -ib -n | grep Link
                // UPTIME: uptime
                var cmd = "echo '>>>CPU'; top -l 1 -n 0 | grep 'CPU usage'; echo '>>>MEM'; sysctl hw.memsize; vm_stat; echo '>>>DISK'; df -h / | tail -n 1; echo '>>>NET'; netstat -ib -n | grep Link; echo '>>>UPTIME'; uptime";
                
                var output = await sshSession.ExecuteCommandAsync(cmd);
                ParseMacSystemInfo(output);
                
                // Ping measurement using SSH round-trip time with high-precision Stopwatch
                try
                {
                    var sw = Stopwatch.StartNew();
                    await sshSession.ExecuteCommandAsync("echo");
                    sw.Stop();
                    PingLatency = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
                    PingStatus = $"{PingLatency:0.0} ms";
                }
                catch
                {
                    PingStatus = "-";
                }
            }
            catch
            {
                // Ignore errors
            }
            
            await Task.Delay(2000);
        }
    }

    private void ParseMacSystemInfo(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return;
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToArray();
        
        int section = 0;
        long totalMem = 0;
        long freePages = 0;
        long activePages = 0;
        long wiredPages = 0;
        long speculativePages = 0;
        double pageSize = 4096; // Default to 4KB

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
                        // CPU usage: 5.8% user, 9.6% sys, 84.6% idle
                        if (line.StartsWith("CPU usage:"))
                        {
                            var parts = line.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                            // Find 'idle'
                            for (int i = 0; i < parts.Length; i++)
                            {
                                if (parts[i] == "idle" && i > 0)
                                {
                                    var valStr = parts[i - 1].Replace("%", "");
                                    if (double.TryParse(valStr, out double idle))
                                    {
                                        CpuUsage = 100.0 - idle;
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
                             if (parts.Length > 1) long.TryParse(parts[1].Trim(), out totalMem);
                        }
                        else if (line.StartsWith("Pages free:"))
                             long.TryParse(line.Split(':')[1].Trim().TrimEnd('.'), out freePages);
                        else if (line.StartsWith("Pages active:"))
                             long.TryParse(line.Split(':')[1].Trim().TrimEnd('.'), out activePages);
                        else if (line.StartsWith("Pages wired down:"))
                             long.TryParse(line.Split(':')[1].Trim().TrimEnd('.'), out wiredPages);
                        else if (line.StartsWith("Pages speculative:"))
                             long.TryParse(line.Split(':')[1].Trim().TrimEnd('.'), out speculativePages);
                        break;
                    case 3: // DISK
                        if (!line.StartsWith("Filesystem"))
                        {
                            var parts = line.Split(new[]{' '}, StringSplitOptions.RemoveEmptyEntries);
                            // MacOS df -h: Filesystem Size Used Avail Capacity iused ifree %iused Mounted on
                            // Capacity is usually index 4 (5th col) like "3%"
                            foreach (var p in parts)
                            {
                                if (p.EndsWith("%"))
                                {
                                    if (double.TryParse(p.TrimEnd('%'), out double cap))
                                    {
                                        DiskUsage = cap;
                                        break; 
                                    }
                                }
                            }
                        }
                        break;
                    case 4: // NET
                        // Aggregated in ParseMacNetwork helper logic inside loop
                        // But here we need to parse individual lines and sum
                        // We will buffer them and process at the end of section or on next section?
                        // Simplified: we process line by line and sum.
                        break;
                    case 5: // UPTIME
                        // 14:26  up 2 days, 3:45, 3 users...
                        var upIdx = line.IndexOf("up ");
                        if (upIdx != -1)
                        {
                            var endIdx = line.IndexOf(", ", upIdx + 3); // finding end of days or time
                            // Uptime format varies. Just taking the substring 'up ......' until 'users' or comma?
                            // Simple: just take the whole string after "up "
                             Uptime = line.Substring(upIdx);
                        }
                        break;
                }
            } 
            catch {}
        }

        // Calculate RAM
        if (totalMem > 0)
        {
            // MacOS: Used = (Active + Wired) approx, or Total - Free - Speculative
            // Using Total - Free - Speculative is "App Memory + Wired + Compressed", closer to "Used" in activity monitor
            // Note: Speculative is basically cached/free.
            long freeBytes = (freePages + speculativePages) * (long)pageSize;
            long usedBytes = totalMem - freeBytes;
            RamUsage = ((double)usedBytes / totalMem) * 100.0;
        }
        
        // Process Network Section buffer specifically
        ParseMacNetwork(output, false);
    }

    private void ParseMacNetwork(string fullOutput, bool initOnly)
    {
        // Extract NET section
        var lines = fullOutput.Split('\n');
        bool inNet = false;
        long currentRx = 0;
        long currentTx = 0;
        
        if (initOnly) inNet = true;

        foreach (var line in lines)
        {
            if (line.Trim() == ">>>NET") { inNet = true; continue; }
            if (line.StartsWith(">>>") && line.Trim() != ">>>NET") { inNet = false; continue; }
            
            if (inNet)
            {
                // en0 1500 <Link#4> ... 12345 0 999999 12345 0 888888 0
                // Expecting "Link" in line to avoid IP duplicates
                if (line.Contains("Link"))
                {
                    var parts = line.Split(new[]{' '}, StringSplitOptions.RemoveEmptyEntries);
                    // Indices for netstat -ib: Name(0) Mtu(1) Net(2) Address(3) Ipkts(4) Ierrs(5) Ibytes(6) Opkts(7) Oerrs(8) Obytes(9) Coll(10)
                    if (parts.Length >= 10)
                    {
                        if (long.TryParse(parts[6], out long rx)) currentRx += rx;
                        if (long.TryParse(parts[9], out long tx)) currentTx += tx;
                    }
                }
            }
        }

        if (initOnly)
        {
             _prevRxBytes = currentRx;
             _prevTxBytes = currentTx;
             _prevCheckTime = DateTime.Now;
             return;
        }

        var now = DateTime.Now;
        if (_prevCheckTime != DateTime.MinValue)
        {
            var seconds = (now - _prevCheckTime).TotalSeconds;
            if (seconds > 0)
            {
                NetworkDown = FormatSpeed((currentRx - _prevRxBytes) / seconds);
                NetworkUp = FormatSpeed((currentTx - _prevTxBytes) / seconds);
            }
        }
        _prevCheckTime = now;
        _prevRxBytes = currentRx;
        _prevTxBytes = currentTx;
    }

    private void ParseSystemInfo(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return;
        
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToArray();
        int section = 0; 

        long currentRx = 0;
        long currentTx = 0;
        long currentDiskRead = 0;
        long currentDiskWrite = 0;
        
        // 临时列表：先收集，解析完毕后统一做差量更新
        var newProcesses = new List<ProcessInfo>();
        var newPorts = new List<PortInfo>();

        foreach (var line in lines)
        {
            if (line == ">>>STAT") { section = 1; continue; }
            if (line == ">>>MEM") { section = 2; continue; }
            if (line == ">>>DISK_H") { section = 3; continue; }  // df -h (human readable)
            if (line == ">>>DISK_B") { section = 11; continue; } // df -B1 (bytes)
            if (line == ">>>NET") { section = 4; continue; }
            if (line == ">>>UPTIME") { section = 5; continue; }
            if (line == ">>>TEMP") { section = 6; continue; }
            if (line == ">>>DISKIO") { section = 7; continue; }
            if (line == ">>>GPU") { section = 8; _gpuVendor = ""; _intelGpuLineIndex = 0; continue; }
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
                                        CpuUsage = (1.0 - (double)deltaIdle / deltaTotal) * 100.0;
                                    }
                                }
                                _prevIdleTime = idle;
                                _prevTotalTime = total;
                            }
                        }
                        break;
                    case 2: // MEM - /proc/meminfo format: "MemTotal:       16384000 kB"
                        if (line.Contains(":"))
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
                                        MemTotal = valueGB;
                                        break;
                                    case "MemFree":
                                        MemFree = valueGB;
                                        break;
                                    case "MemAvailable":
                                        // Calculate RAM usage based on available memory
                                        if (MemTotal > 0)
                                            RamUsage = ((MemTotal - valueGB) / MemTotal) * 100.0;
                                        // Also calculate MemUsed here since we have all the info
                                        MemUsed = MemTotal - valueGB;
                                        break;
                                    case "Buffers":
                                        MemBuffers = valueGB;
                                        break;
                                    case "Cached":
                                        MemCached = valueGB;
                                        break;
                                    case "SwapTotal":
                                        SwapTotal = valueGB;
                                        break;
                                    case "SwapFree":
                                        if (SwapTotal > 0)
                                            SwapUsed = SwapTotal - valueGB;
                                        break;
                                }
                            }
                        }
                        break;
                    case 3: // DISK_H - df -h (human readable)
                        if (!line.StartsWith("Filesystem"))
                        {
                             var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                             // df -h output: Filesystem Size Used Avail Use% Mounted
                             if (parts.Length >= 6 && parts[4].Contains("%"))
                             {
                                 var useStr = parts[4].Replace("%", ""); 
                                 if (double.TryParse(useStr, out double usage))
                                     DiskUsage = usage;
                                 DiskPath = parts[5]; // Mount point
                             }
                        }
                        break;
                    case 11: // DISK_B - df -B1 (bytes)
                        if (!line.StartsWith("Filesystem"))
                        {
                             var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                             // df -B1 output: Filesystem 1B-blocks Used Available Use% Mounted
                             if (parts.Length >= 4)
                             {
                                 if (long.TryParse(parts[1], out long totalBytes))
                                      DiskTotal = totalBytes / (1024.0 * 1024 * 1024);
                                 if (long.TryParse(parts[2], out long usedBytes))
                                      DiskUsedBytes = usedBytes / (1024.0 * 1024 * 1024);
                              }
                        }
                        break;
                    case 4: // NET
                        if (line.Contains(":") && !line.Trim().StartsWith("Tcp"))
                        {
                             // /proc/net/dev format
                             if (!line.Trim().StartsWith("lo") && !line.Contains("loopback"))
                             {
                                 var raw = line.Substring(line.IndexOf(':') + 1).Trim();
                                 var nums = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                 if (nums.Length >= 9)
                                 {
                                     currentRx += long.Parse(nums[0]);
                                     currentTx += long.Parse(nums[8]);
                                     if (nums.Length >= 3) TotalPacketsRecv += long.Parse(nums[1]);
                                     if (nums.Length >= 10) TotalPacketsSent += long.Parse(nums[9]);
                                 }
                             }
                        }
                        else if (line.StartsWith("Tcp:") && line.Contains(" "))
                        {
                            // /proc/net/snmp Tcp line has connection stats
                            // Format: Tcp: RtoAlgorithm RtoMin RtoMax MaxConn ActiveOpens PassiveOpens AttemptFails EstabResets CurrEstab ...
                            // We want CurrEstab (index 9)
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 10 && int.TryParse(parts[9], out int estab))
                                TcpConnections = estab;
                        }
                        else if (!line.Contains(":") && int.TryParse(line.Trim(), out int connCount))
                        {
                            // This is from "ss -tan | grep ESTAB | wc -l"
                            TcpConnections = connCount;
                        }
                        break;
                    case 5: // UPTIME
                        // uptime output: " 14:26:03 up 2 days,  3:45,  3 users,  load average: 0.52, 0.58, 0.59"
                        if (line.Contains("load average:"))
                        {
                            var loadIdx = line.IndexOf("load average:");
                            var loadStr = line.Substring(loadIdx + 13).Trim();
                            var loads = loadStr.Split(',');
                            if (loads.Length >= 1 && double.TryParse(loads[0].Trim(), out double l1)) Load1 = l1;
                            if (loads.Length >= 2 && double.TryParse(loads[1].Trim(), out double l5)) Load5 = l5;
                            if (loads.Length >= 3 && double.TryParse(loads[2].Trim(), out double l15)) Load15 = l15;
                            
                            // Parse uptime string
                            var upIdx = line.IndexOf("up ");
                            if (upIdx != -1)
                            {
                                var endIdx = line.IndexOf(",", upIdx);
                                if (endIdx == -1) endIdx = line.IndexOf("user", upIdx);
                                if (endIdx > upIdx)
                                {
                                    Uptime = line.Substring(upIdx, endIdx - upIdx).Trim();
                                }
                            }
                        }
                        else
                        {
                            Uptime = line;
                        }
                        break;
                    case 6: // TEMP
                         if (double.TryParse(line, out double temp))
                         {
                             CpuTemp = temp / 1000.0;
                         }
                         break;
                    case 7: // DISKIO
                        // 8       0 sda 123 456 789 ...
                        var dParts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (dParts.Length >= 10)
                        {
                            var dev = dParts[2];
                            if (dev.StartsWith("sd") || dev.StartsWith("nvme") || dev.StartsWith("vd"))
                            {
                                if (!char.IsDigit(dev[dev.Length - 1])) // Try to avoid partitions if possible, but sda1 is fine to skip if sda exists.
                                {
                                    // Actually usually sda counts total, sda1 counts partition.
                                    // Let's stick to devices ending with letter or 'n' digit?
                                    // simpler: just sum everything that looks like a disk? No, that double counts.
                                    // Let's just sum sd[a-z], vd[a-z], nvme[0-9]n[0-9]
                                    bool isDisk = false;
                                    if ((dev.StartsWith("sd") || dev.StartsWith("vd")) && dev.Length == 3 && char.IsLetter(dev[2])) isDisk = true;
                                    if (dev.StartsWith("nvme") && dev.Contains("n") && !dev.Contains("p")) isDisk = true; // nvme0n1

                                    if (isDisk)
                                    {
                                         if (long.TryParse(dParts[5], out long r) && long.TryParse(dParts[9], out long w))
                                         {
                                             currentDiskRead += r;
                                             currentDiskWrite += w;
                                         }
                                    }
                                }
                            }
                        }
                        break;
                    case 8: // GPU
                        if (line == "N/A")
                        {
                            GpuDetected = false;
                        }
                        else if (line == "NVIDIA" || line == "AMD" || line == "INTEL")
                        {
                            _gpuVendor = line;
                            GpuDetected = true;
                        }
                        else if (_gpuVendor == "NVIDIA")
                        {
                            // Format: utilization.gpu, temperature.gpu, power.draw, name, memory.used, memory.total, fan.speed
                            var gParts = line.Split(',');
                            if (gParts.Length >= 4)
                            {
                                if(double.TryParse(gParts[0], out double gu)) GpuUsage = gu;
                                if(double.TryParse(gParts[1], out double gt)) GpuTemp = gt;
                                if(double.TryParse(gParts[2], out double gp)) GpuPower = gp;
                                GpuModel = gParts[3].Trim();
                                
                                if (gParts.Length >= 6)
                                {
                                    if (double.TryParse(gParts[4], out double memUsed)) 
                                        GpuMemoryUsed = memUsed / 1024.0; // Convert MB to GB
                                    if (double.TryParse(gParts[5], out double memTotal))
                                        GpuMemoryTotal = memTotal / 1024.0;
                                }
                                if (gParts.Length >= 7)
                                {
                                    // fan.speed may return "[Not Supported]" for some cards
                                    if (int.TryParse(gParts[6].Trim(), out int fan))
                                        GpuFanSpeed = fan;
                                }
                            }
                        }
                        else if (_gpuVendor == "AMD")
                        {
                            // rocm-smi CSV output: parse key-value lines
                            ParseAmdGpuLine(line);
                        }
                        else if (_gpuVendor == "INTEL")
                        {
                            ParseIntelGpuLine(line);
                        }
                        break;
                    
                    case 9: // PROC - ps aux output
                        // Format: USER PID %CPU %MEM VSZ RSS TTY STAT START TIME COMMAND
                        var procParts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (procParts.Length >= 11)
                        {
                            var procInfo = new ProcessInfo
                            {
                                User = procParts[0],
                                Pid = int.TryParse(procParts[1], out int pid) ? pid : 0,
                                CpuPercent = double.TryParse(procParts[2], out double cpu) ? cpu : 0,
                                MemPercent = double.TryParse(procParts[3], out double mem) ? mem : 0,
                                Name = string.Join(" ", procParts.Skip(10))
                            };
                            newProcesses.Add(procInfo);
                        }
                        break;
                    
                    case 10: // PORTS - ss -tlnp output
                        // Format: State Recv-Q Send-Q Local Address:Port Peer Address:Port Process
                        var portParts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (portParts.Length >= 4 && portParts[0] == "LISTEN")
                        {
                            var localAddr = portParts[3];
                            var colonIdx = localAddr.LastIndexOf(':');
                            if (colonIdx >= 0 && int.TryParse(localAddr.Substring(colonIdx + 1), out int port))
                            {
                                var portInfo = new PortInfo
                                {
                                    Port = port,
                                    Protocol = "TCP",
                                    State = "LISTEN",
                                    LocalAddress = localAddr.Substring(0, colonIdx),
                                    Process = portParts.Length > 5 ? portParts[5] : ""
                                };
                                newPorts.Add(portInfo);
                            }
                        }
                        break;
                }
            }
            catch {}
        }
        
        var now = DateTime.Now;
        if (_prevCheckTime != DateTime.MinValue)
        {
            var seconds = (now - _prevCheckTime).TotalSeconds;
            if (seconds > 0)
            {
                NetworkDown = FormatSpeed((currentRx - _prevRxBytes) / seconds);
                NetworkUp = FormatSpeed((currentTx - _prevTxBytes) / seconds);
                
                DiskReadSpeed = FormatSpeed((currentDiskRead - _prevDiskReadSectors) * 512.0 / seconds);
                DiskWriteSpeed = FormatSpeed((currentDiskWrite - _prevDiskWriteSectors) * 512.0 / seconds);
                
                // Calculate IOPS (operations per second)
                DiskReadIops = (long)((currentDiskRead - _prevDiskReadSectors) / seconds);
                DiskWriteIops = (long)((currentDiskWrite - _prevDiskWriteSectors) / seconds);
            }
        }
        
        // Format network totals
        NetTotalRecv = FormatSize(currentRx);
        NetTotalSent = FormatSize(currentTx);
        
        _prevCheckTime = now;
        _prevRxBytes = currentRx;
        _prevTxBytes = currentTx;
        _prevDiskReadSectors = currentDiskRead;
        _prevDiskWriteSectors = currentDiskWrite;
        
        // 差量更新进程列表和端口列表（在 UI 线程一次性完成，避免 Clear 导致滚动重置）
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            DiffUpdateProcesses(newProcesses);
            DiffUpdatePorts(newPorts);
        });
    }

    /// <summary>
    /// 按位置更新进程列表：逐索引就地更新属性，多余删除，不足添加。
    /// 保持新数据的排序顺序，不触发 CollectionChanged.Reset，ScrollViewer 滚动位置不丢失。
    /// 必须在 UI 线程调用。
    /// </summary>
    private void DiffUpdateProcesses(List<ProcessInfo> newItems)
    {
        // 1. 就地更新已有位置
        int commonCount = Math.Min(TopProcesses.Count, newItems.Count);
        for (int i = 0; i < commonCount; i++)
        {
            TopProcesses[i].UpdateFrom(newItems[i]);
        }
        
        // 2. 如果新数据更少，从末尾删除多余项
        while (TopProcesses.Count > newItems.Count)
        {
            TopProcesses.RemoveAt(TopProcesses.Count - 1);
        }
        
        // 3. 如果新数据更多，添加新项
        for (int i = commonCount; i < newItems.Count; i++)
        {
            TopProcesses.Add(newItems[i]);
        }
    }

    /// <summary>
    /// 按位置更新端口列表：逐索引就地更新属性，多余删除，不足添加。
    /// 必须在 UI 线程调用。
    /// </summary>
    private void DiffUpdatePorts(List<PortInfo> newItems)
    {
        // 1. 就地更新已有位置
        int commonCount = Math.Min(ListeningPorts.Count, newItems.Count);
        for (int i = 0; i < commonCount; i++)
        {
            ListeningPorts[i].UpdateFrom(newItems[i]);
        }
        
        // 2. 如果新数据更少，从末尾删除多余项
        while (ListeningPorts.Count > newItems.Count)
        {
            ListeningPorts.RemoveAt(ListeningPorts.Count - 1);
        }
        
        // 3. 如果新数据更多，添加新项
        for (int i = commonCount; i < newItems.Count; i++)
        {
            ListeningPorts.Add(newItems[i]);
        }
    }

    private void ParseAmdGpuLine(string line)
    {
        // rocm-smi CSV output format varies, try to extract key metrics
        // Typical output contains lines like: "card0, 85.0, 72.0, 150.0, ..."
        // or key-value pairs from different flags
        if (line.Contains("GPU use") || line.Contains("GPU Activity"))
        {
            // Extract percentage
            var parts = line.Split(new[] { ':', ',' }, StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                var trimmed = part.Trim().TrimEnd('%');
                if (double.TryParse(trimmed, out double val) && val >= 0 && val <= 100)
                {
                    GpuUsage = val;
                    break;
                }
            }
        }
        else if (line.Contains("Temperature") || line.Contains("Temp"))
        {
            var parts = line.Split(new[] { ':', ',' }, StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                var trimmed = part.Trim().TrimEnd('C', 'c', ' ');
                if (double.TryParse(trimmed, out double val) && val > 0 && val < 200)
                {
                    GpuTemp = val;
                    break;
                }
            }
        }
        else if (line.Contains("Power") || line.Contains("Average Graphics Package Power"))
        {
            var parts = line.Split(new[] { ':', ',' }, StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                var trimmed = part.Trim().TrimEnd('W', 'w', ' ');
                if (double.TryParse(trimmed, out double val) && val > 0)
                {
                    GpuPower = val;
                    break;
                }
            }
        }
        else if (line.Contains("Card series") || line.Contains("Card model") || (line.Contains("GPU") && line.Contains("Radeon")))
        {
            var parts = line.Split(new[] { ':' }, 2);
            if (parts.Length == 2)
                GpuModel = parts[1].Trim();
            else if (line.Contains("Radeon"))
                GpuModel = line.Trim();
        }
        else if (line.Contains("VRAM Total Memory") || line.Contains("vram Total"))
        {
            var parts = line.Split(new[] { ':', ',' }, StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (long.TryParse(part.Trim(), out long val) && val > 0)
                {
                    GpuMemoryTotal = val / (1024.0 * 1024.0 * 1024.0); // Bytes to GB
                    break;
                }
            }
        }
        else if (line.Contains("VRAM Total Used Memory") || line.Contains("vram Used"))
        {
            var parts = line.Split(new[] { ':', ',' }, StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (long.TryParse(part.Trim(), out long val) && val > 0)
                {
                    GpuMemoryUsed = val / (1024.0 * 1024.0 * 1024.0); // Bytes to GB
                    break;
                }
            }
        }
    }

    private void ParseIntelGpuLine(string line)
    {
        // Intel GPU via sysfs: 4 lines in order:
        // Line 0: vendor (e.g. "0x8086")
        // Line 1: current frequency MHz
        // Line 2: VRAM used bytes
        // Line 3: VRAM total bytes
        switch (_intelGpuLineIndex)
        {
            case 0:
                if (line.Contains("8086"))
                    GpuModel = "Intel Integrated Graphics";
                else
                    GpuModel = "Intel GPU";
                _intelGpuLineIndex++;
                break;
            case 1:
                if (double.TryParse(line.Trim(), out double freq) && freq > 0)
                {
                    // Use frequency as a rough "usage" indicator (higher freq = more usage)
                    // Not a real percentage, but gives some information
                    GpuModel = $"Intel GPU @ {freq:0} MHz";
                }
                _intelGpuLineIndex++;
                break;
            case 2:
                if (long.TryParse(line.Trim(), out long vramUsed) && vramUsed > 0)
                    GpuMemoryUsed = vramUsed / (1024.0 * 1024.0 * 1024.0);
                _intelGpuLineIndex++;
                break;
            case 3:
                if (long.TryParse(line.Trim(), out long vramTotal) && vramTotal > 0)
                    GpuMemoryTotal = vramTotal / (1024.0 * 1024.0 * 1024.0);
                _intelGpuLineIndex = 0;
                break;
        }
    }

    private string FormatSize(double bytes)
    {
        if (bytes < 1024) return $"{bytes:0} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024:0.0} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024 / 1024:0.0} MB";
        return $"{bytes / 1024 / 1024 / 1024:0.0} GB";
    }

    private string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{bytesPerSec:0} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024:0.0} KB/s";
        return $"{bytesPerSec / 1024 / 1024:0.0} MB/s";
    }

    public void Dispose()
    {
        // Unsubscribe events
        Session.Connected -= OnConnected;
        Session.Disconnected -= OnDisconnected;
        Session.ErrorOccurred -= OnError;
        Session.DataReceived -= OnDataReceived; // Unsubscribe data

        try
        {
            Session.Dispose();
        }
        catch { /* ignore */ }
        
        GC.SuppressFinalize(this);
    }

    private void OnDataReceived(string data)
    {
        // Write directly to terminal buffer
        Terminal.Write(data);
        ScreenUpdated?.Invoke();
    }

    public void SendInput(string input)
    {
        if (IsConnected)
        {
            Session.Send(input);
        }
    }

    public void Resize(int rows, int cols, int width, int height)
    {
        if (IsConnected)
        {
            Session.Resize(rows, cols, width, height);
        }
    }

    private async Task ConnectAsync()
    {
        if (IsConnecting) return;

        IsConnecting = true;
        ShowOverlay = true;
        StatusMessage = LocalizationService.Instance["StatusConnecting"];
        
        try
        {
            await Session.ConnectAsync();
            // OnConnected will be called by the session
        }
        catch (Exception ex)
        {
            StatusMessage = $"{LocalizationService.Instance["ConnectionFailed"]}{ex.Message}";
            IsConnected = false;
            ShowOverlay = true;
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private void ToggleSftp() => IsSftpVisible = !IsSftpVisible;

    [RelayCommand]
    private void ToggleSystemInfo() => IsSystemInfoVisible = !IsSystemInfoVisible;

    [RelayCommand]
    private async Task Reconnect()
    {
         await ConnectAsync();
    }

    [RelayCommand]
    private void Disconnect()
    {
        try
        {
            if (Session.IsConnected)
                Session.Dispose();
        }
        catch { /* ignore */ }
    }

    [RelayCommand]
    private void Close()
    {
        RequestClose?.Invoke(this);
    }

    private void OnConnected()
    {
        IsConnected = true;
        IsConnecting = false;
        ShowOverlay = false;
        StatusMessage = LocalizationService.Instance["StatusConnected"];
    }

    private void OnDisconnected()
    {
        IsConnected = false;
        IsConnecting = false;
        ShowOverlay = true;
        StatusMessage = LocalizationService.Instance["SessionDisconnected"];
        Terminal.Write("\r\n[DISCONNECTED]\r\n");
    }

    private void OnError(Exception ex)
    {
        IsConnected = false;
        IsConnecting = false;
        ShowOverlay = true;
        StatusMessage = $"Error: {ex.Message}";
    }
}
