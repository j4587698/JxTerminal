using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JxTerminal.Controls;
using JxTerminal.Models;
using JxTerminal.Services;
using System;
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

    // Detailed Hardware Info
    [ObservableProperty] private string _cpuModel = "Loading...";
    [ObservableProperty] private string _cpuClock = "";
    [ObservableProperty] private double _cpuTemp = 0;
    
    [ObservableProperty] private string _gpuModel = "None";
    [ObservableProperty] private double _gpuTemp = 0;
    [ObservableProperty] private int _gpuFanSpeed = 0; // RPM or %
    [ObservableProperty] private double _gpuPower = 0; // Watts
    
    [ObservableProperty] private string _diskReadSpeed = "0 B/s";
    [ObservableProperty] private string _diskWriteSpeed = "0 B/s";
    
    [ObservableProperty] private string _netTotalSent = "1.2 GB";
    [ObservableProperty] private string _netTotalRecv = "4.5 GB";

    [ObservableProperty]
    private int _selectedSidebarIndex = 0;

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
            Title = "Local Shell";
        else
            Title = "Terminal";

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
            var model = await sshSession.ExecuteCommandAsync("grep -m1 'model name' /proc/cpuinfo | cut -d: -f2");
            if (!string.IsNullOrWhiteSpace(model)) CpuModel = model.Trim();
        } 
        catch { }

        while (IsConnected)
        {
            try
            {
                var cmd = "echo '>>>STAT'; grep '^cpu ' /proc/stat; echo '>>>MEM'; free -b; echo '>>>DISK'; df -h / | tail -n 1; echo '>>>NET'; cat /proc/net/dev; echo '>>>UPTIME'; uptime -p; echo '>>>TEMP'; cat /sys/class/thermal/thermal_zone*/temp 2>/dev/null | head -n 1; echo '>>>DISKIO'; cat /proc/diskstats; echo '>>>GPU'; nvidia-smi --query-gpu=utilization.gpu,temperature.gpu,power.draw,name --format=csv,noheader,nounits 2>/dev/null || echo 'N/A'";
                
                var output = await sshSession.ExecuteCommandAsync(cmd);
                ParseSystemInfo(output);
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

        foreach (var line in lines)
        {
            if (line == ">>>STAT") { section = 1; continue; }
            if (line == ">>>MEM") { section = 2; continue; }
            if (line == ">>>DISK") { section = 3; continue; }
            if (line == ">>>NET") { section = 4; continue; }
            if (line == ">>>UPTIME") { section = 5; continue; }
            if (line == ">>>TEMP") { section = 6; continue; }
            if (line == ">>>DISKIO") { section = 7; continue; }
            if (line == ">>>GPU") { section = 8; continue; }

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
                    case 2: // MEM
                        if (line.StartsWith("Mem:"))
                        {
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2)
                            {
                                double total = double.Parse(parts[1]);
                                double available = 0;
                                if (parts.Length >= 7) available = double.Parse(parts[6]); 
                                else if (parts.Length >= 4) available = double.Parse(parts[3]) + (parts.Length >= 6 ? double.Parse(parts[5]) : 0);
                                
                                if (total > 0)
                                    RamUsage = ((total - available) / total) * 100.0;
                            }
                        }
                        break;
                    case 3: // DISK
                        if (!line.StartsWith("Filesystem"))
                        {
                             var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                             if (parts.Length >= 5)
                             {
                                 var useStr = parts[parts.Length - 2].Replace("%", ""); 
                                 if (double.TryParse(useStr, out double usage))
                                     DiskUsage = usage;
                             }
                        }
                        break;
                    case 4: // NET
                        if (line.Contains(":"))
                        {
                             if (!line.Trim().StartsWith("lo") && !line.Contains("loopback"))
                             {
                                 var raw = line.Substring(line.IndexOf(':') + 1).Trim();
                                 var nums = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                 if (nums.Length >= 9)
                                 {
                                     currentRx += long.Parse(nums[0]);
                                     currentTx += long.Parse(nums[8]);
                                 }
                             }
                        }
                        break;
                    case 5: // UPTIME
                        Uptime = line;
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
                        if (line != "N/A")
                        {
                            var gParts = line.Split(',');
                            if (gParts.Length >= 4)
                            {
                                if(double.TryParse(gParts[0], out double gu)) GpuUsage = gu;
                                if(double.TryParse(gParts[1], out double gt)) GpuTemp = gt;
                                if(double.TryParse(gParts[2], out double gp)) GpuPower = gp;
                                GpuModel = gParts[3].Trim();
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
            }
        }
        _prevCheckTime = now;
        _prevRxBytes = currentRx;
        _prevTxBytes = currentTx;
        _prevDiskReadSectors = currentDiskRead;
        _prevDiskWriteSectors = currentDiskWrite;
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
