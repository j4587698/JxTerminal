using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;
using JxTerminal.Models;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LuminaUI.Controls;
using Material.Icons;

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
        else if (value is SettingsViewModel)
        {
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

    [ObservableProperty]
    private object? _dialogContent;

    [ObservableProperty]
    private bool _isDialogOpen;

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
             SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Key == "HostList");
        };

        TerminalList.DuplicateRequested += async (conn) => await Connect(conn);

        HostList = new HostListViewModel(this);
        ActiveContent = HostList;

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
            Command = NavigateToPlaceholderCommand,
            CommandParameter = "Transfer"
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
            Command = NavigateToPlaceholderCommand,
            CommandParameter = "Keys"
        });

        MenuItems.Add(new MenuItemViewModel
        {
            Key = "Settings",
            IconKind = MaterialIconKind.Cog,
            Command = NavigateToSettingsCommand
        });
    }

    [RelayCommand]
    public void NavigateToHostList() => ActiveContent = HostList;

    [RelayCommand]
    public void NavigateToTerminal() => ActiveContent = TerminalList;

    [RelayCommand]
    public void NavigateToSettings() => ActiveContent = SettingsPage ??= new SettingsViewModel(Settings, this);

    private SettingsViewModel? SettingsPage;

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
            Content = content
        };
        IsDialogOpen = true;
    }

    public void ShowMessageDialog(string title, string message)
    {
        ShowDialog(
            new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520,
                Margin = new Avalonia.Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Stretch
            },
            title);
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
            MaxWidth = 520,
            Margin = new Avalonia.Thickness(4)
        };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap
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
            MinWidth = 360,
            MaxWidth = 520,
            Margin = new Avalonia.Thickness(4)
        };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap
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

        // Debounced auto-save: color picker drags fire dozens of notifications per second.
        var saveCts = new System.Threading.CancellationTokenSource();
        vm.Settings.PropertyChanged += (s, e) =>
        {
            saveCts.Cancel();
            saveCts = new System.Threading.CancellationTokenSource();
            var token = saveCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500, token);
                    await JxTerminal.Services.DataStore.SaveSettingsAsync(vm.Settings);
                }
                catch (OperationCanceledException) { }
            });
        };
        return vm;
    }

    [ObservableProperty]
    private bool _isBusy;

    public async Task Connect(SavedConnection connection)
    {
        if (IsBusy) return;
        IsBusy = true;
        
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

                session = new SshSession(
                    connInfo.Host,
                    connInfo.Port,
                    connInfo.Username,
                    password,
                    connInfo.AuthenticationType,
                    connInfo.KeyFilePath,
                    connInfo.Passphrase,
                    Settings.RemoteEncoding);
            }

            if (session is SshSession ssh)
            {
                 ssh.HostKeyVerifier = VerifyHostKey;
            }

            // Observe session errors for every transport so failures are never silent
            // (the local-shell path in particular reports through this event).
            Exception? sessionError = null;
            session.ErrorOccurred += OnConnectionError;
            session.ErrorOccurred += ex => sessionError ??= ex;

            await session.ConnectAsync();

            if (session.IsConnected)
            {
                if (session is SshSession) session.ErrorOccurred -= OnConnectionError;
                if (session is SerialSession) session.ErrorOccurred -= OnConnectionError;
                if (session is TelnetSession) session.ErrorOccurred -= OnConnectionError;

                var vm = new TerminalViewModel(session, Settings, connInfo);
                TerminalList.Tabs.Add(vm);
                TerminalList.SelectedTab = vm;
                ActiveContent = TerminalList; 
            }
            else
            {
                // Connection attempt failed; release the socket/handles it may already hold.
                session.ErrorOccurred -= OnConnectionError;
                (session as IDisposable)?.Dispose();

                if (sessionError != null)
                {
                    var l = JxTerminal.Services.LocalizationService.Instance;
                    ShowMessageDialog(l["ConnectionFailedTitle"], $"{l["ConnectionFailed"]}{sessionError.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            var l = JxTerminal.Services.LocalizationService.Instance;
            ShowMessageDialog(l["ConnectionFailedTitle"], $"{l["ConnectionFailed"]}{ex.Message}");
        }
        finally
        {
            IsBusy = false;
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
