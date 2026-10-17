using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JxTerminal.Controls;
using JxTerminal.Models;
using JxTerminal.Services;
using JxTerminal.Services.Dialog;

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

    public bool ShowDisconnectedDot => !IsConnected && !IsConnecting;

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDisconnectedDot));
    }

    partial void OnIsConnectingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDisconnectedDot));
    }

    // SFTP & System Monitor Modules
    [ObservableProperty]
    private SftpViewModel? _sftp;

    [ObservableProperty]
    private SystemMonitorViewModel? _monitor;

    /// <summary>AI assistant panel state (available for every session type).</summary>
    public AiAssistantViewModel AiAssistant { get; }

    [ObservableProperty]
    private bool _isAiVisible;

    [ObservableProperty]
    private bool _isSidebarVisible;

    /// <summary>
    /// For backwards compatibility with existing UI bindings.
    /// </summary>
    public bool IsSftpVisible
    {
        get => IsSidebarVisible;
        set => IsSidebarVisible = value;
    }

    partial void OnIsSidebarVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSftpVisible));
        if (Monitor != null)
        {
            Monitor.IsPollingActive = value;
        }
    }

    // Find in terminal
    [ObservableProperty]
    private bool _isSearchVisible;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _searchMatchCase;

    partial void OnIsSearchVisibleChanged(bool value)
    {
        if (!value) SearchText = string.Empty;
    }

    [RelayCommand]
    private void ToggleSearch() => IsSearchVisible = !IsSearchVisible;

    [RelayCommand]
    private void CloseSearch() => IsSearchVisible = false;

    public bool IsSshSession => Session is SshSession;

    public ITerminalSession Session { get; }
    public TerminalSettings Settings { get; }
    public SavedConnection? ConnectionInfo { get; }
    
    // The terminal state (buffer, cursor, etc.) persists here
    public XTerm.Terminal Terminal { get; private set; }

    public event Action? ScreenUpdated;
    public event Action<TerminalViewModel>? RequestClose;

    public AnsiModeTracker Modes { get; } = new();

    [ObservableProperty]
    private string _currentWorkingDirectory = string.Empty;

    [ObservableProperty]
    private bool _isActiveTab;

    [ObservableProperty]
    private bool _hasUnseenOutput;

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
        Modes.CurrentDirectoryChanged += OnTerminalDirectoryChanged;
        
        if (session is SshSession ssh)
        {
            Title = !string.IsNullOrWhiteSpace(connectionInfo?.Name)
                ? connectionInfo.Name
                : string.IsNullOrWhiteSpace(ssh.Username)
                    ? $"{ssh.Host}:{ssh.Port}"
                    : $"{ssh.Username}@{ssh.Host}";

            // Initialize SFTP if available (file list loads once connected, see OnConnected)
            Sftp = new SftpViewModel(
                ssh,
                ConfirmAsync,
                PromptAsync,
                openInTerminal: OpenPathInTerminal,
                getTerminalCwd: ProbeCurrentWorkingDirectoryAsync,
                settings: Settings);

            // Initialize System Monitor
            Monitor = new SystemMonitorViewModel(ssh);
            Monitor.OnSftpTabSelected = async () =>
            {
                if (Sftp != null)
                {
                    await Sftp.SyncWithTerminalCommand.ExecuteAsync(null);
                }
            };
        }
        else if (session is PtyLocalShellSession)
        {
            Title = !string.IsNullOrWhiteSpace(connectionInfo?.Name)
                ? connectionInfo.Name
                : LocalizationService.Instance["QuickLocal"];
        }
        else
        {
            Title = !string.IsNullOrWhiteSpace(connectionInfo?.Name)
                ? connectionInfo.Name
                : LocalizationService.Instance["Terminal"];
        }

        // Subscribe to events
        Session.Connected += OnConnected;
        Session.Disconnected += OnDisconnected;
        Session.ErrorOccurred += OnError;

        // AI assistant: bridges the model's tools to this terminal's buffer and input.
        AiAssistant = new AiAssistantViewModel(
            settings,
            ReadTerminalTextAsync,
            SendInput,
            ConfirmAsync,
            DescribeSession(),
            close: () => IsAiVisible = false);

        // Recreate the terminal when the scrollback setting changes at runtime.
        Settings.PropertyChanged += OnSettingsPropertyChanged;

        if (Session.IsConnected)
        {
            OnConnected();
        }
        else
        {
            _ = ConnectAsync();
        }
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalSettings.ScrollbackLines))
        {
            RebuildTerminal();
        }
    }

    /// <summary>Modal confirm dialog routed through IDialogService.</summary>
    public Task<bool> ConfirmAsync(string title, string message)
        => DialogService.Instance.ShowConfirmAsync(title, message);

    /// <summary>Modal text prompt routed through IDialogService.</summary>
    public Task<string?> PromptAsync(string title, string message)
        => DialogService.Instance.ShowInputAsync(title, message);

    private void RebuildTerminal()
    {
        var old = Terminal;
        var options = new XTerm.Options.TerminalOptions
        {
            Scrollback = Math.Max(0, Settings.ScrollbackLines),
            Cols = old.Cols,
            Rows = old.Rows
        };
        Terminal = new XTerm.Terminal(options);
        ScreenUpdated?.Invoke();
    }

    // Session data arrives on background threads (SSH.NET receive loop, PTY reader,
    // serial port thread). The XTerm buffer is not thread-safe, so chunks are queued
    // here and applied on the UI thread. Coalescing also prevents one dispatcher
    // round-trip per 4KB chunk when output streams at high throughput.
    private readonly object _pendingDataLock = new();
    private readonly StringBuilder _pendingData = new();
    private int _flushScheduled;
    private bool _disposed;

    private void OnDataReceived(string data)
    {
        if (string.IsNullOrEmpty(data)) return;

        lock (_pendingDataLock)
        {
            _pendingData.Append(data);
        }

        if (Interlocked.CompareExchange(ref _flushScheduled, 1, 0) == 0)
        {
            // Default priority: Render-priority jobs only run when a render pass is
            // already scheduled, which would deadlock the pipeline (the flush is what
            // requests the render in the first place).
            Dispatcher.UIThread.Post(FlushPendingData);
        }
    }

    private void FlushPendingData()
    {
        Interlocked.Exchange(ref _flushScheduled, 0);
        if (_disposed) return;

        string data;
        lock (_pendingDataLock)
        {
            if (_pendingData.Length == 0) return;
            data = _pendingData.ToString();
            _pendingData.Clear();
        }

        Modes.Process(data);
        if (!IsActiveTab) HasUnseenOutput = true;

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

    /// <summary>Sends a cd command to navigate the active terminal to the specified remote path.</summary>
    public void OpenPathInTerminal(string remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath) || !IsConnected) return;
        SendInput($"cd \"{remotePath}\"\n");
    }

    private void OnTerminalDirectoryChanged(string newDir)
    {
        if (string.IsNullOrWhiteSpace(newDir)) return;
        CurrentWorkingDirectory = newDir;
        if (Sftp != null)
        {
            Sftp.SetCurrentPathFromTerminal(newDir, autoRefresh: true);
        }
    }

    /// <summary>
    /// Fallback active probe for systems that do not emit OSC 7 directory sequences.
    /// Inspects the cwd of the user's interactive shell processes without touching the terminal screen.
    /// </summary>
    public async Task<string?> ProbeCurrentWorkingDirectoryAsync()
    {
        if (Session is SshSession ssh && ssh.IsConnected)
        {
            try
            {
                var cmd = "for p in $(pgrep -u $(id -u) -x 'bash|zsh|fish|sh' 2>/dev/null); do " +
                          "target=$(readlink /proc/$p/cwd 2>/dev/null); " +
                          "if [ -n \"$target\" ] && [ -d \"$target\" ]; then echo \"$target\"; break; fi; done";
                var result = await ssh.ExecuteCommandAsync(cmd);
                var path = result.Trim();
                if (!string.IsNullOrEmpty(path) && path.StartsWith('/'))
                {
                    CurrentWorkingDirectory = path;
                    return path;
                }
            }
            catch { /* ignore probe failure */ }
        }
        return null;
    }

    private int _lastRows = 24;
    private int _lastCols = 80;
    private int _lastWidth = 800;
    private int _lastHeight = 600;

    public void Resize(int rows, int cols, int width, int height)
    {
        if (rows <= 0 || cols <= 0) return;
        _lastRows = rows;
        _lastCols = cols;
        if (width > 0) _lastWidth = width;
        if (height > 0) _lastHeight = height;

        Session.Resize(rows, cols, width, height);
    }

    public bool MouseTracking => Modes.MouseTracking;
    public bool MouseAnyEvent => Modes.MouseAnyEvent;
    public bool SgrMouse => Modes.SgrMouse;
    public bool ApplicationCursorKeys => Modes.ApplicationCursorKeys;
    public bool BracketedPaste => Modes.BracketedPaste;

    public Avalonia.Point? SelectionStart { get; set; }
    public Avalonia.Point? SelectionEnd { get; set; }

    private async Task ConnectAsync()
    {
        if (IsConnecting) return;

        IsConnecting = true;
        ShowOverlay = true;
        StatusMessage = LocalizationService.Instance["StatusConnecting"];
        
        try
        {
            await Session.ConnectAsync();
            if (_disposed) return;
            if (!Session.IsConnected)
            {
                IsConnected = false;
                IsConnecting = false;
                ShowOverlay = true;
                if (string.IsNullOrEmpty(StatusMessage) || StatusMessage == LocalizationService.Instance["StatusConnecting"])
                {
                    StatusMessage = LocalizationService.Instance["ConnectionFailed"];
                }
            }
        }
        catch (Exception ex)
        {
            if (_disposed) return;
            var l = LocalizationService.Instance;
            var msg = ex is System.Security.SecurityException && ex.Message == "HostKeyRejected"
                ? l["HostKeyRejected"]
                : ex.Message;
            StatusMessage = $"{l["ConnectionFailed"]}{msg}";
            IsConnected = false;
            ShowOverlay = true;
            IsConnecting = false;
        }
    }

    [RelayCommand]
    public void ToggleSftp()
    {
        IsSidebarVisible = !IsSidebarVisible;
        if (IsSidebarVisible)
        {
            IsAiVisible = false;
        }
    }

    /// <summary>Shows or hides the AI assistant panel.</summary>
    [RelayCommand]
    public void ToggleAi()
    {
        IsAiVisible = !IsAiVisible;
        if (IsAiVisible)
        {
            IsSidebarVisible = false;
        }
    }

    /// <summary>
    /// Describes this session for the AI system prompt (transport + target + shell).
    /// </summary>
    private string DescribeSession()
    {
        var os = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux";
        return Session switch
        {
            SshSession ssh => $"SSH session to {ssh.Username}@{ssh.Host}:{ssh.Port} (remote host, likely Linux/Unix). Commands run on the REMOTE machine.",
            TelnetSession tn => $"Telnet session to {tn.Host}:{tn.Port} (remote host). Commands run on the REMOTE machine.",
            SerialSession se => $"Serial console on {se.PortName} @ {se.BaudRate} baud (attached device). Commands run on the DEVICE.",
            PtyLocalShellSession => $"Local shell on the user's own {os} machine. Commands run LOCALLY.",
            _ => $"Terminal session on {os}."
        };
    }

    /// <summary>
    /// Reads terminal text for the AI read_terminal tool. Marshals to the UI thread
    /// because the XTerm buffer is only written there.
    /// lines &gt; 0 reads that many trailing viewport lines; 0 reads the visible screen.
    /// </summary>
    private async Task<string> ReadTerminalTextAsync(int lines)
        => await Dispatcher.UIThread.InvokeAsync(() => ReadTerminalText(lines));

    private string ReadTerminalText(int lines)
    {
        var buffer = Terminal.Buffer;
        if (buffer?.Lines == null) return "";

        int end = Math.Min(buffer.Lines.Length, buffer.YDisp + Terminal.Rows);
        int start = lines > 0 ? Math.Max(0, end - lines) : buffer.YDisp;
        start = Math.Max(0, Math.Min(start, end));

        var sb = new StringBuilder();
        for (int i = start; i < end; i++)
        {
            if (buffer.Lines[i] is XTerm.Buffer.BufferLine line)
            {
                sb.AppendLine(line.TranslateToString(true).TrimEnd());
            }
        }

        // Drop trailing blank lines so the model sees only meaningful output.
        return sb.ToString().TrimEnd();
    }

    [RelayCommand]
    public async Task Reconnect()
    {
        await ConnectAsync();
    }

    [RelayCommand]
    public void Disconnect()
    {
        try
        {
            if (Session.IsConnected)
                Session.Dispose();
        }
        catch { /* ignore */ }
    }

    [RelayCommand]
    public void Close()
    {
        RequestClose?.Invoke(this);
    }

    private void OnConnected()
    {
        // Immediately synchronize actual terminal rows & cols to the connected backend session
        int rows = Terminal?.Rows > 0 ? Terminal.Rows : _lastRows;
        int cols = Terminal?.Cols > 0 ? Terminal.Cols : _lastCols;
        Session.Resize(rows, cols, _lastWidth, _lastHeight);

        void ApplyUi()
        {
            IsConnected = true;
            IsConnecting = false;
            ShowOverlay = false;
            StatusMessage = LocalizationService.Instance["StatusConnected"];

            // The file list can only load after the SFTP channel is up.
            if (Sftp != null) _ = Sftp.RefreshFiles();
        }

        if (Dispatcher.UIThread.CheckAccess() || Avalonia.Application.Current == null)
        {
            ApplyUi();
        }
        else
        {
            Dispatcher.UIThread.Post(ApplyUi);
        }
    }

    private void OnDisconnected()
    {
        if (!Dispatcher.UIThread.CheckAccess() && Avalonia.Application.Current != null)
        {
            Dispatcher.UIThread.Post(OnDisconnected);
            return;
        }
        if (_disposed) return;

        IsConnected = false;
        IsConnecting = false;
        ShowOverlay = true;
        StatusMessage = LocalizationService.Instance["SessionDisconnected"];
        Terminal.Write($"\r\n[{LocalizationService.Instance["SessionDisconnected"]}]\r\n");
    }

    private void OnError(Exception ex)
    {
        if (!Dispatcher.UIThread.CheckAccess() && Avalonia.Application.Current != null)
        {
            Dispatcher.UIThread.Post(() => OnError(ex));
            return;
        }
        if (_disposed) return;

        IsConnected = false;
        IsConnecting = false;
        ShowOverlay = true;
        var l = LocalizationService.Instance;
        var msg = ex is System.Security.SecurityException && ex.Message == "HostKeyRejected"
            ? l["HostKeyRejected"]
            : ex.Message;
        StatusMessage = $"{l["ConnectionFailed"]}{msg}";
    }

    public void Dispose()
    {
        _disposed = true;
        Monitor?.Dispose();
        AiAssistant?.Dispose();
        Settings.PropertyChanged -= OnSettingsPropertyChanged;

        Session.Connected -= OnConnected;
        Session.Disconnected -= OnDisconnected;
        Session.ErrorOccurred -= OnError;
        Session.DataReceived -= OnDataReceived;
        Modes.CurrentDirectoryChanged -= OnTerminalDirectoryChanged;

        try
        {
            Session.Dispose();
        }
        catch { /* ignore */ }

        GC.SuppressFinalize(this);
    }
}
