using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading;
using System.Threading.Tasks;
using JxTerminal.Models;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using LuminaUI.Controls;
using LuminaUI.Theming;
using Material.Icons;
using JxTerminal.Services;

namespace JxTerminal.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>
    /// Process-wide instance so view models without a direct reference (terminal tabs,
    /// SFTP panel) can still route dialogs through the main shell.
    /// </summary>
    public static MainWindowViewModel? Instance { get; private set; }

    [ObservableProperty]
    private ViewModelBase _activeContent = null!;

    /// <summary>Window title mirrors the active terminal tab, e.g. "root@web1 - JxTerminal".</summary>
    public string WindowTitle
    {
        get
        {
            var l = Services.LocalizationService.Instance;
            var tab = TerminalList?.SelectedTab;
            return tab == null ? l["AppName"] : $"{tab.Title} - {l["AppName"]}";
        }
    }

    partial void OnActiveContentChanged(ViewModelBase value)
    {
        if (value is HostListViewModel)
        {
            SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Key == "HostList");
            SelectedNavigationKey = "HostList";
        }
        else if (value is TerminalListViewModel)
        {
            SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Key == "Terminal");
            SelectedNavigationKey = "Terminal";
        }
        else if (value is TransferCenterViewModel)
        {
            SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Key == "Transfer");
            SelectedNavigationKey = "Transfer";
        }
        else if (value is SnippetsViewModel)
        {
            SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Key == "Snippets");
            SelectedNavigationKey = "Snippets";
        }
        else if (value is KeyManagerViewModel)
        {
            SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Key == "Keys");
            SelectedNavigationKey = "Keys";
        }
        else if (value is SettingsViewModel)
        {
            SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Key == "Settings");
            SelectedNavigationKey = "Settings";
        }
    }

    public HostListViewModel HostList { get; } = null!;
    public TerminalListViewModel TerminalList { get; } = null!;

    public ObservableCollection<MenuItemViewModel> MenuItems { get; } = new();

    [ObservableProperty]
    private MenuItemViewModel? _selectedMenuItem;

    [ObservableProperty]
    private string? _selectedNavigationKey = "HostList";

    partial void OnSelectedNavigationKeyChanged(string? value)
    {
        switch (value)
        {
            case "HostList" when ActiveContent is not HostListViewModel:
                NavigateToHostList();
                break;
            case "Terminal" when ActiveContent is not TerminalListViewModel:
                NavigateToTerminal();
                break;
            case "Transfer" when ActiveContent is not TransferCenterViewModel:
                NavigateToTransfer();
                break;
            case "Snippets" when ActiveContent is not SnippetsViewModel:
                NavigateToSnippets();
                break;
            case "Keys" when ActiveContent is not KeyManagerViewModel:
                NavigateToKeys();
                break;
            case "Settings" when ActiveContent is not SettingsViewModel:
                NavigateToSettings();
                break;
        }
    }

    [ObservableProperty]
    private object? _dialogContent;

    [ObservableProperty]
    private bool _isDialogOpen;

    /// <summary>
    /// Releases the current dialog's awaiter when the dialog is dismissed without a
    /// result (Esc / overlay click via the TwoWay IsDialogOpen binding, or programmatic
    /// close). Consumed on every close; dialogs that produce results complete their
    /// TaskCompletionSource first, so the fallback is a no-op for them.
    /// </summary>
    private Action? _dialogDismissFallback;

    public void RegisterDialogDismissFallback(Action? onDismissed)
        => _dialogDismissFallback = onDismissed;

    partial void OnIsDialogOpenChanged(bool value)
    {
        if (!value)
        {
            DialogContent = null;
            var fallback = Interlocked.Exchange(ref _dialogDismissFallback, null);
            fallback?.Invoke();
        }
    }

    /// <summary>
    /// True only while a background operation is running AND no modal dialog is
    /// open. The connecting overlay uses this so it never covers a dialog that
    /// needs user input (password / host-key trust).
    /// </summary>
    public bool IsBusyOverlayVisible => IsBusy && !IsDialogOpen;

    [ObservableProperty]
    private TerminalSettings _settings;

    public MainWindowViewModel()
    {
        Instance = this;

        // Keep the busy-overlay visibility in sync: it must hide whenever a modal
        // dialog is open, otherwise the full-window scrim would trap the dialog.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IsBusy) or nameof(IsDialogOpen))
                OnPropertyChanged(nameof(IsBusyOverlayVisible));
        };

        // Load Settings Synchronously (or async init pattern)
        // Since constructor is sync, we'll start with default and then load.
        _settings = new TerminalSettings();

        TerminalList = new TerminalListViewModel();
        TerminalList.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TerminalList.SelectedTab))
                OnPropertyChanged(nameof(WindowTitle));
        };
        Services.LocalizationService.Instance.PropertyChanged += (_, _) =>
            OnPropertyChanged(nameof(WindowTitle));
        TerminalList.RequestCreateConnection += () => {
             ActiveContent = HostList;
             SelectedNavigationKey = "HostList";
             SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Key == "HostList");
        };

        TerminalList.DuplicateRequested += async (conn) => await Connect(conn);

        MenuItems.Add(new MenuItemViewModel
        {
            Key = "HostList",
            IconKind = MaterialIconKind.ServerNetwork,
            Command = NavigateToHostListCommand
        });

        MenuItems.Add(new MenuItemViewModel
        {
            Key = "Terminal",
            IconKind = MaterialIconKind.ConsoleLine,
            Command = NavigateToTerminalCommand
        });

        MenuItems.Add(new MenuItemViewModel
        {
            Key = "Transfer",
            IconKind = MaterialIconKind.FolderSwap,
            Command = NavigateToTransferCommand
        });

        MenuItems.Add(new MenuItemViewModel
        {
            Key = "Snippets",
            IconKind = MaterialIconKind.ScriptText,
            Command = NavigateToSnippetsCommand
        });

        MenuItems.Add(new MenuItemViewModel
        {
            Key = "Keys",
            IconKind = MaterialIconKind.KeyVariant,
            Command = NavigateToKeysCommand
        });

        MenuItems.Add(new MenuItemViewModel
        {
            Key = "Settings",
            IconKind = MaterialIconKind.Cog,
            Command = NavigateToSettingsCommand
        });

        HostList = new HostListViewModel(this);
        ActiveContent = HostList;

        // Wire up auto-sync snapshot applied event to refresh the UI
        Services.Sync.SyncService.Instance.SnapshotApplied += () =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplySyncedData(restored: true));
        };
    }

    [RelayCommand]
    public void NavigateToHostList() => ActiveContent = HostList;

    /// <summary>
    /// Ctrl+K / title-bar search box: jump to the host list and focus its search field.
    /// </summary>
    [RelayCommand]
    public void FocusHostSearch()
    {
        ActiveContent = HostList;
        HostList.RequestSearchFocus();
    }

    [RelayCommand]
    public void NavigateToTerminal() => ActiveContent = TerminalList;

    [RelayCommand]
    public void NavigateToSettings() => NavigateToSettingsTab(0);

    public void NavigateToSettingsTab(int tabIndex)
    {
        SelectedNavigationKey = "Settings";
        var page = SettingsPage ??= new SettingsViewModel(Settings, this);
        page.SelectedTabIndex = tabIndex;
        ActiveContent = page;
    }

    private SettingsViewModel? SettingsPage;

    [RelayCommand]
    public void NavigateToTransfer()
    {
        SelectedNavigationKey = "Transfer";
        ActiveContent = TransferCenterPage ??= new TransferCenterViewModel();
    }

    private TransferCenterViewModel? TransferCenterPage;

    [RelayCommand]
    public void NavigateToKeys()
    {
        SelectedNavigationKey = "Keys";
        ActiveContent = KeyManagerPage ??= new KeyManagerViewModel();
    }

    private KeyManagerViewModel? KeyManagerPage;

    [RelayCommand]
    public void NavigateToSnippets()
    {
        SelectedNavigationKey = "Snippets";
        ActiveContent = SnippetsPage ??= new SnippetsViewModel(this);
    }

    private SnippetsViewModel? SnippetsPage;

    /// <summary>
    /// Sends a command line to the active terminal tab (appending Enter) and brings the
    /// terminal page to front. No-op when no connected tab exists.
    /// </summary>
    public void SendToActiveTerminal(string command)
    {
        if (string.IsNullOrEmpty(command)) return;
        var tab = TerminalList.SelectedTab;
        if (tab == null) return;

        tab.SendInput(command.EndsWith('\r') || command.EndsWith('\n') ? command : command + "\r");
        ActiveContent = TerminalList;
    }

    [RelayCommand]
    public void NavigateToPlaceholder(string title)
    {
        SelectedNavigationKey = title;
        ActiveContent = new PlaceholderViewModel(JxTerminal.Services.LocalizationService.Instance[title]);
    }

    public void ShowDialog(Control content, string? title = null)
    {
        DialogContent = new LuminaDialog
        {
            Title = title,
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 600
        };
        IsDialogOpen = true;
    }

    public void ShowMessageDialog(string title, string message)
    {
        var panel = new StackPanel
        {
            Spacing = 16,
            MinWidth = 300,
            MaxWidth = 440
        };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20
        });
        var confirm = new Button
        {
            Content = Services.LocalizationService.Instance["Confirm"],
            Classes = { "Primary" },
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true
        };
        confirm.Click += (_, _) => CloseDialog();
        panel.Children.Add(confirm);

        ShowDialog(panel, title);
    }

    /// <summary>
    /// Shows a modal question and resolves to true when the user confirms.
    /// </summary>
    public Task<bool> ShowConfirmDialog(string title, string message, string confirmText, bool danger = false)
    {
        var tcs = new TaskCompletionSource<bool>();

        var confirm = new Button
        {
            Content = confirmText,
            Classes = { danger ? "Danger" : "Primary" }
        };
        var cancel = new Button
        {
            Content = Services.LocalizationService.Instance["Cancel"],
            Classes = { "Ghost" }
        };

        confirm.Click += (_, _) => { CloseDialog(); tcs.TrySetResult(true); };
        cancel.Click += (_, _) => { CloseDialog(); tcs.TrySetResult(false); };
        var panel = new StackPanel
        {
            Spacing = 16,
            MinWidth = 320,
            MaxWidth = 460
        };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20
        });
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);

        RegisterDialogDismissFallback(() => tcs.TrySetResult(false));
        ShowDialog(panel, title);
        return tcs.Task;
    }

    /// <summary>
    /// Prompts for a password. Resolves to null when the user cancels. The value is never persisted.
    /// </summary>
    public Task<string?> ShowPasswordDialog(string title, string message)
        => ShowInputDialog(title, message, isPassword: true);

    /// <summary>
    /// Generic single-line text prompt. Resolves to null when the user cancels.
    /// </summary>
    public Task<string?> ShowInputDialog(string title, string message, bool isPassword = false)
    {
        var tcs = new TaskCompletionSource<string?>();
        var l = Services.LocalizationService.Instance;

        var input = new TextBox
        {
            PlaceholderText = message
        };
        if (isPassword) input.PasswordChar = '\u25cf';

        void Complete(string? result)
        {
            CloseDialog();
            tcs.TrySetResult(result);
        }

        var confirm = new Button
        {
            Content = l["Confirm"],
            Classes = { "Primary" },
            IsDefault = true
        };
        confirm.Click += (_, _) => Complete(input.Text ?? string.Empty);

        var cancel = new Button
        {
            Content = l["Cancel"],
            Classes = { "Ghost" },
            IsCancel = true
        };
        cancel.Click += (_, _) => Complete(null);

        var panel = new StackPanel
        {
            Spacing = 16,
            MinWidth = 320,
            MaxWidth = 460
        };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20
        });
        panel.Children.Add(input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);

        RegisterDialogDismissFallback(() => tcs.TrySetResult(null));
        ShowDialog(panel, title);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => input.Focus(), Avalonia.Threading.DispatcherPriority.Input);
        return tcs.Task;
    }

    public void CloseDialog()
    {
        IsDialogOpen = false;
        DialogContent = null;
    }

    /// <summary>
    /// Creates the view model with settings fully loaded before the UI binds to them.
    /// Synchronous on purpose: OnFrameworkInitializationCompleted must create the main
    /// window before returning, otherwise the classic lifetime may exit with zero windows.
    /// </summary>
    public static MainWindowViewModel Create()
    {
        var vm = new MainWindowViewModel();
        vm.Settings = JxTerminal.Services.DataStore.LoadSettings();
        vm.Settings.ResolveColorScheme();

        vm.ApplyVisualPreferences(vm.Settings);
        vm.HookSettingsAutoSave();
        return vm;
    }

    /// <summary>
    /// Applies saved language, accent color and theme to the running application.
    /// </summary>
    private void ApplyVisualPreferences(TerminalSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Language))
        {
            LocalizationService.Instance.SetLanguage(settings.Language);
        }
        if (!string.IsNullOrWhiteSpace(settings.AccentColor))
        {
            LuminaThemeManager.TrySetAccentColor(settings.AccentColor);
        }
        if (!string.IsNullOrWhiteSpace(settings.ThemeMode))
        {
            var isLight = settings.ThemeMode.Equals("Light", StringComparison.OrdinalIgnoreCase);
            Application.Current!.RequestedThemeVariant = isLight ? ThemeVariant.Light : ThemeVariant.Dark;
            LuminaThemeManager.SetThemeMode(isLight ? LuminaThemeMode.Light : LuminaThemeMode.Dark);
        }
    }

    private System.Threading.CancellationTokenSource _saveCts = new();

    /// <summary>
    /// Subscribes to settings changes and persists them with a short debounce.
    /// </summary>
    private void HookSettingsAutoSave()
    {
        _saveCts.Cancel();
        _saveCts.Dispose();
        _saveCts = new System.Threading.CancellationTokenSource();

        Settings.PropertyChanged += (s, e) =>
        {
            // Cancel + dispose the previous generation so CTS instances don't pile up.
            var old = Interlocked.Exchange(ref _saveCts, new System.Threading.CancellationTokenSource());
            try { old.Cancel(); } catch (ObjectDisposedException) { }
            old.Dispose();

            var token = _saveCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500, token);
                    await JxTerminal.Services.DataStore.SaveSettingsAsync(Settings);
                }
                catch (OperationCanceledException) { }
            });
        };
    }

    /// <summary>
    /// Flushes any pending debounced settings immediately to the local database.
    /// Called during application exit or window closing to prevent settings loss.
    /// </summary>
    public void FlushSettings()
    {
        try
        {
            _saveCts.Cancel();
            JxTerminal.Services.DataStore.SaveSettings(Settings);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to flush settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Refreshes in-memory pages after a cloud restore: the host list is reloaded
    /// immediately and cached pages are invalidated so the next navigation shows the
    /// restored data. When <paramref name="restored"/> is true the active settings are
    /// also replaced and the visual preferences re-applied.
    /// </summary>
    public void ApplySyncedData(bool restored)
    {
        HostList.ReloadData();

        // Invalidate cached pages so they rebuild from the restored data store.
        SettingsPage = null;
        SnippetsPage = null;
        KeyManagerPage = null;

        if (restored)
        {
            Settings = JxTerminal.Services.DataStore.LoadSettings();
            Settings.ResolveColorScheme();
            ApplyVisualPreferences(Settings);
            HookSettingsAutoSave();
        }
    }

    [ObservableProperty]
    private bool _isBusy;

    public async Task Connect(SavedConnection connection, string? initialCommand = null)
    {
        var connInfo = connection.Clone();

        ITerminalSession session;

        try
        {
            if (connInfo.Type == ConnectionType.Local)
            {
                session = new PtyLocalShellSession(
                    string.IsNullOrWhiteSpace(connInfo.ShellPath) ? Settings.LocalShellKind : connInfo.ShellPath);
            }
            else if (connInfo.Type == ConnectionType.Telnet)
            {
                session = new TelnetSession(connInfo.Host, connInfo.Port, Settings.RemoteEncoding);
            }
            else if (connInfo.Type == ConnectionType.Serial)
            {
                session = new SerialSession(
                    connInfo.SerialPortName,
                    connInfo.BaudRate,
                    connInfo.DataBits,
                    connInfo.Parity,
                    connInfo.StopBits,
                    connInfo.Handshake,
                    Settings.RemoteEncoding);
            }
            else
            {
                string password = connInfo.Password;
                if (connInfo.AuthenticationType == SshAuthenticationType.Ask)
                {
                    var l = JxTerminal.Services.LocalizationService.Instance;
                    var target = string.IsNullOrWhiteSpace(connInfo.Username)
                        ? connInfo.Host
                        : $"{connInfo.Username}@{connInfo.Host}";

                    var entered = await ShowPasswordDialog(
                        l["PasswordPromptTitle"],
                        string.Format(l["PasswordPromptMessage"], target));

                    // Cancelled: abort the connection instead of trying an empty password.
                    if (entered == null) return;
                    password = entered;
                }

                string privateKeyContent = "";
                if (connInfo.KeyStoreId.HasValue)
                {
                    var key = Services.KeyStore.KeyStoreService.Instance.GetKey(connInfo.KeyStoreId.Value);
                    if (key == null)
                    {
                        // The stored key was deleted from the key store: fail with a clear
                        // message instead of silently falling back to an empty password.
                        var l = JxTerminal.Services.LocalizationService.Instance;
                        ShowMessageDialog(l["ConnectionFailedTitle"], l["KeyNotFound"]);
                        return;
                    }
                    privateKeyContent = key.PrivateKeyPem;
                }

                session = new SshSession(
                    connInfo.Host,
                    connInfo.Port,
                    connInfo.Username,
                    password,
                    connInfo.AuthenticationType,
                    connInfo.KeyFilePath,
                    connInfo.Passphrase,
                    Settings.RemoteEncoding,
                    privateKeyContent);
            }

            if (session is SshSession ssh)
            {
                 ssh.HostKeyVerifier = VerifyHostKey;
            }

            var vm = new TerminalViewModel(session, Settings, connInfo);

            if (!string.IsNullOrWhiteSpace(initialCommand))
            {
                void SendInitial()
                {
                    session.Connected -= SendInitial;
                    Task.Delay(300).ContinueWith(_ =>
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            vm.SendInput(initialCommand + "\n");
                        });
                    });
                }

                if (session.IsConnected) SendInitial();
                else session.Connected += SendInitial;
            }

            TerminalList.Tabs.Add(vm);
            TerminalList.SelectedTab = vm;
            ActiveContent = TerminalList;
            SelectedNavigationKey = "Terminal";
            SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Key == "Terminal");
        }
        catch (Exception ex)
        {
            var l = JxTerminal.Services.LocalizationService.Instance;
            ShowMessageDialog(l["ConnectionFailedTitle"], $"{l["ConnectionFailed"]}{ex.Message}");
        }
    }

    /// <summary>
    /// Awaits the trust prompt on the async connect pipeline. Runs on the UI-thread context of the
    /// awaiting caller, so there is no blocking wait between threads.
    /// </summary>
    private Task<bool> VerifyHostKey(string hostDisplay, string fingerprint, string? knownFingerprint)
    {
        var l = JxTerminal.Services.LocalizationService.Instance;

        var title = knownFingerprint == null ? l["HostKeyNewTitle"] : l["HostKeyChangedTitle"];
        var message = knownFingerprint == null
            ? string.Format(l["HostKeyNewMessage"], hostDisplay, fingerprint)
            : string.Format(l["HostKeyChangedMessage"], hostDisplay, knownFingerprint, fingerprint);

        return ShowConfirmDialog(title, message, l["Trust"], danger: knownFingerprint != null);
    }

    private void OnConnectionError(Exception ex)
    {
         var l = JxTerminal.Services.LocalizationService.Instance;
         var message = ex is System.Security.SecurityException && ex.Message == "HostKeyRejected"
             ? l["HostKeyRejected"]
             : ex.Message;

         Avalonia.Threading.Dispatcher.UIThread.Post(() =>
             ShowMessageDialog(l["ConnectionError"], message));
    }
}
