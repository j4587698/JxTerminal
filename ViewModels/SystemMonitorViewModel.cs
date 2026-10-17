using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JxTerminal.Models;
using JxTerminal.Services.Monitoring;

namespace JxTerminal.ViewModels;

public partial class SystemMonitorViewModel : ViewModelBase, IDisposable
{
    private readonly SshSession _session;
    private readonly CancellationTokenSource _cts = new();
    private ISystemMonitorProvider _provider = new LinuxProcMonitorProvider();

    // Tab state
    [ObservableProperty] private int _selectedTabIndex = 0;
    [ObservableProperty] private bool _isTabOverview = true;
    [ObservableProperty] private bool _isTabProcessor = false;
    [ObservableProperty] private bool _isTabGpu = false;
    [ObservableProperty] private bool _isTabMemory = false;
    [ObservableProperty] private bool _isTabDisk = false;
    [ObservableProperty] private bool _isTabNetwork = false;
    [ObservableProperty] private bool _isTabSftp = false;

    // Overview & Hardware static info
    [ObservableProperty] private string _hostname = "Loading...";
    [ObservableProperty] private string _systemInfo = "Loading...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UptimeDisplay))]
    private string _uptime = "0d 0h 0m";

    [ObservableProperty] private string _cpuModel = "Loading...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuCoreCountDisplay))]
    private int _cpuCoreCount;

    [ObservableProperty] private int _cpuThreadCount;

    // CPU Metrics
    [ObservableProperty] private double _cpuUsage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCpuTemp))]
    private double _cpuTemp;

    /// <summary>False when the machine has no readable temperature sensor (value stays 0).</summary>
    public bool HasCpuTemp => CpuTemp > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadDisplay))]
    private double _load1;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadDisplay))]
    private double _load5;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadDisplay))]
    private double _load15;

    // RAM Metrics
    [ObservableProperty] private double _ramUsage;
    [ObservableProperty] private double _memTotal;
    [ObservableProperty] private double _memUsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemFreeCachedDisplay))]
    private double _memFree;

    [ObservableProperty] private double _memBuffers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemFreeCachedDisplay))]
    private double _memCached;

    [ObservableProperty] private double _swapTotal;
    [ObservableProperty] private double _swapUsed;

    // Disk Metrics
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskTitle))]
    private string _diskPath = "/";
    [ObservableProperty] private double _diskUsage;
    [ObservableProperty] private double _diskTotal;
    [ObservableProperty] private double _diskUsedBytes;
    [ObservableProperty] private string _diskReadSpeed = "0 B/s";
    [ObservableProperty] private string _diskWriteSpeed = "0 B/s";
    [ObservableProperty] private long _diskReadIops;
    [ObservableProperty] private long _diskWriteIops;

    // Network Metrics
    [ObservableProperty] private string _networkDown = "0 KB/s";
    [ObservableProperty] private string _networkUp = "0 KB/s";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetTotalRecvDisplay))]
    private string _netTotalRecv = "0 B";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetTotalSentDisplay))]
    private string _netTotalSent = "0 B";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPacketsDisplay))]
    private long _totalPacketsRecv;

    [ObservableProperty] private long _totalPacketsSent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TcpConnectionsDisplay))]
    private int _tcpConnections;

    [ObservableProperty] private double _pingLatency;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PingStatusDisplay))]
    private string _pingStatus = "-";

    // GPU Metrics
    [ObservableProperty] private bool _gpuDetected;
    [ObservableProperty] private string _gpuModel = "N/A";
    [ObservableProperty] private double _gpuUsage;
    [ObservableProperty] private double _gpuTemp;
    [ObservableProperty] private double _gpuPower;
    [ObservableProperty] private double _gpuMemoryUsed;
    [ObservableProperty] private double _gpuMemoryTotal;
    [ObservableProperty] private int _gpuFanSpeed;

    // Collections
    public ObservableCollection<ProcessInfo> TopProcesses { get; } = new();
    public ObservableCollection<PortInfo> ListeningPorts { get; } = new();

    /// <summary>
    /// Controls whether polling occurs (set to false when sidebar is hidden to conserve resources).
    /// </summary>
    [ObservableProperty] private bool _isPollingActive = true;

    public SystemMonitorViewModel(SshSession session)
    {
        _session = session;
        Services.LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
        _ = StartMonitoringLoopAsync();
    }

    // --- Localized composite display strings (XAML StringFormat cannot be localized) ---
    private static Services.LocalizationService L => Services.LocalizationService.Instance;

    public string UptimeDisplay => string.Format(L["RuntimeFormat"], Uptime);
    public string CpuCoreCountDisplay => string.Format(L["CoreCountFormat"], CpuCoreCount);
    public string LoadDisplay => $"{L["SystemLoad"]}{Load1:0.00} / {Load5:0.00} / {Load15:0.00}";
    public string MemFreeCachedDisplay => $"{L["AvailableLabel"]}{MemFree:0.0} GB  {L["CachedLabel"]}{MemCached:0.0} GB";
    public string DiskTitle => string.Format(L["DiskTitleFormat"], DiskPath);
    public string PingStatusDisplay => string.Format(L["LatencyFormat"], PingStatus);
    public string NetTotalRecvDisplay => string.Format(L["TotalCountFormat"], NetTotalRecv);
    public string NetTotalSentDisplay => string.Format(L["TotalCountFormat"], NetTotalSent);
    public string TcpConnectionsDisplay => string.Format(L["TcpConnectionsFormat"], TcpConnections);
    public string TotalPacketsDisplay => string.Format(L["TotalPacketsFormat"], TotalPacketsRecv);

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(UptimeDisplay));
        OnPropertyChanged(nameof(CpuCoreCountDisplay));
        OnPropertyChanged(nameof(LoadDisplay));
        OnPropertyChanged(nameof(MemFreeCachedDisplay));
        OnPropertyChanged(nameof(DiskTitle));
        OnPropertyChanged(nameof(PingStatusDisplay));
        OnPropertyChanged(nameof(NetTotalRecvDisplay));
        OnPropertyChanged(nameof(NetTotalSentDisplay));
        OnPropertyChanged(nameof(TcpConnectionsDisplay));
        OnPropertyChanged(nameof(TotalPacketsDisplay));
    }

    /// <summary>Invoked when user switches to the SFTP tab to allow lazy/sync actions.</summary>
    public Func<Task>? OnSftpTabSelected { get; set; }

    [RelayCommand]
    public void SelectTab(object? param)
    {
        if (param == null) return;
        if (int.TryParse(param.ToString(), out int index))
        {
            SelectedTabIndex = index;
            IsTabOverview = index == 0;
            IsTabProcessor = index == 1;
            IsTabGpu = index == 2;
            IsTabMemory = index == 3;
            IsTabDisk = index == 4;
            IsTabNetwork = index == 5;
            IsTabSftp = index == 6;

            if (index == 6)
            {
                _ = OnSftpTabSelected?.Invoke();
            }
        }
    }

    [RelayCommand]
    public async Task KillProcess(object? parameter)
    {
        if (parameter is ProcessInfo proc)
        {
            await KillProcessInternal(proc, force: false);
        }
    }

    [RelayCommand]
    public async Task ForceKillProcess(object? parameter)
    {
        if (parameter is ProcessInfo proc)
        {
            await KillProcessInternal(proc, force: true);
        }
    }

    private async Task KillProcessInternal(ProcessInfo proc, bool force)
    {
        if (!_session.IsConnected || proc.Pid <= 0) return;
        try
        {
            var signal = force ? "-9" : "-15";
            await _session.ExecuteCommandAsync($"kill {signal} {proc.Pid}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to kill process {proc.Pid}: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task CopyText(object? parameter)
    {
        if (parameter != null)
        {
            await Services.ClipboardService.SetTextAsync(parameter.ToString() ?? "");
        }
    }

    private async Task StartMonitoringLoopAsync()
    {
        var token = _cts.Token;

        // Wait until session is connected
        while (!_session.IsConnected && !token.IsCancellationRequested)
        {
            try { await Task.Delay(200, token); }
            catch (OperationCanceledException) { return; }
        }

        if (token.IsCancellationRequested) return;

        // Probe platform
        try
        {
            var uname = await _session.ExecuteCommandAsync("uname");
            if (uname.Contains("Darwin", StringComparison.OrdinalIgnoreCase))
            {
                _provider = new MacOsMonitorProvider();
            }
            else
            {
                _provider = new LinuxProcMonitorProvider();
            }

            var staticInfo = await _provider.ProbeStaticInfoAsync(_session, token);
            ApplyStaticInfo(staticInfo);
        }
        catch { /* ignore probe failure */ }

        // Sampling loop
        while (_session.IsConnected && !token.IsCancellationRequested)
        {
            try
            {
                if (IsPollingActive)
                {
                    var snapshotTask = _provider.SampleMetricsAsync(_session, token);
                    var pingTask = MeasurePingAsync(token);

                    await Task.WhenAll(snapshotTask, pingTask);

                    var snapshot = snapshotTask.Result;
                    if (snapshot != null)
                    {
                        // Observable properties must change on the UI thread.
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot));
                    }
                }

                await Task.Delay(2000, token);
            }
            catch (OperationCanceledException) { break; }
            catch { /* protect loop */ }
        }
    }

    private async Task MeasurePingAsync(CancellationToken token)
    {
        var host = _session.Host;
        if (string.IsNullOrWhiteSpace(host)) return;

        var latency = await JxTerminal.Services.Network.PingService.MeasureLatencyAsync(host, _session.Port, 1500, token);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (latency.HasValue)
            {
                PingLatency = latency.Value;
                PingStatus = $"{latency.Value} ms";
            }
            else if (PingLatency <= 0)
            {
                PingLatency = 0;
                PingStatus = "-";
            }
        });
    }

    private void ApplyStaticInfo(SystemStaticInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.Hostname)) Hostname = info.Hostname;
        if (!string.IsNullOrWhiteSpace(info.SystemInfo)) SystemInfo = info.SystemInfo;
        if (!string.IsNullOrWhiteSpace(info.CpuModel)) CpuModel = info.CpuModel;
        if (info.CpuCoreCount > 0) CpuCoreCount = info.CpuCoreCount;
        if (info.CpuThreadCount > 0) CpuThreadCount = info.CpuThreadCount;
    }

    private void ApplySnapshot(SystemSnapshotData data)
    {
        CpuUsage = data.CpuUsage;
        CpuTemp = data.CpuTemp;
        Load1 = data.Load1;
        Load5 = data.Load5;
        Load15 = data.Load15;

        MemTotal = data.MemTotal;
        MemUsed = data.MemUsed;
        MemFree = data.MemFree;
        MemBuffers = data.MemBuffers;
        MemCached = data.MemCached;
        SwapTotal = data.SwapTotal;
        SwapUsed = data.SwapUsed;
        RamUsage = data.RamUsage;

        DiskPath = data.DiskPath;
        DiskUsage = data.DiskUsage;
        DiskTotal = data.DiskTotal;
        DiskUsedBytes = data.DiskUsedBytes;
        DiskReadSpeed = data.DiskReadSpeed;
        DiskWriteSpeed = data.DiskWriteSpeed;
        DiskReadIops = data.DiskReadIops;
        DiskWriteIops = data.DiskWriteIops;

        NetworkDown = data.NetworkDown;
        NetworkUp = data.NetworkUp;
        NetTotalRecv = data.NetTotalRecv;
        NetTotalSent = data.NetTotalSent;
        TotalPacketsRecv = data.TotalPacketsRecv;
        TotalPacketsSent = data.TotalPacketsSent;
        TcpConnections = data.TcpConnections;

        GpuDetected = data.GpuDetected;
        GpuModel = data.GpuModel;
        GpuUsage = data.GpuUsage;
        GpuTemp = data.GpuTemp;
        GpuPower = data.GpuPower;
        GpuMemoryUsed = data.GpuMemoryUsed;
        GpuMemoryTotal = data.GpuMemoryTotal;
        GpuFanSpeed = data.GpuFanSpeed;

        if (!string.IsNullOrWhiteSpace(data.Uptime)) Uptime = data.Uptime;

        // Diff update collections on UI thread
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            DiffUpdateProcesses(data.TopProcesses);
            DiffUpdatePorts(data.ListeningPorts);
        });
    }

    private void DiffUpdateProcesses(List<ProcessInfo> newItems)
    {
        int commonCount = Math.Min(TopProcesses.Count, newItems.Count);
        for (int i = 0; i < commonCount; i++)
        {
            TopProcesses[i].UpdateFrom(newItems[i]);
        }
        while (TopProcesses.Count > newItems.Count)
        {
            TopProcesses.RemoveAt(TopProcesses.Count - 1);
        }
        for (int i = commonCount; i < newItems.Count; i++)
        {
            TopProcesses.Add(newItems[i]);
        }
    }

    private void DiffUpdatePorts(List<PortInfo> newItems)
    {
        int commonCount = Math.Min(ListeningPorts.Count, newItems.Count);
        for (int i = 0; i < commonCount; i++)
        {
            ListeningPorts[i].UpdateFrom(newItems[i]);
        }
        while (ListeningPorts.Count > newItems.Count)
        {
            ListeningPorts.RemoveAt(ListeningPorts.Count - 1);
        }
        for (int i = commonCount; i < newItems.Count; i++)
        {
            ListeningPorts.Add(newItems[i]);
        }
    }

    public void Dispose()
    {
        Services.LocalizationService.Instance.PropertyChanged -= OnLocalizationChanged;
        // Cancel only: the loop may still be awaiting Task.Delay with this token, and
        // disposing the CTS under it would surface a spurious ObjectDisposedException.
        _cts.Cancel();
    }
}
