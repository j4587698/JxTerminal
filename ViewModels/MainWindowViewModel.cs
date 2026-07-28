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
    [ObservableProperty]
    private ViewModelBase _activeContent = null!;

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

    [ObservableProperty]
    private TerminalSettings _settings;

    public MainWindowViewModel()
    {
        // Load Settings Synchronously (or async init pattern)
        // Since constructor is sync, we'll start with default and then load.
        _settings = new TerminalSettings();
        LoadSettings();

        TerminalList = new TerminalListViewModel();
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
            Command = NavigateToPlaceholderCommand,
            CommandParameter = "Snippets"
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
    public void NavigateToSettings() => ActiveContent = new SettingsViewModel(Settings, this);

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
    {
        var tcs = new TaskCompletionSource<string?>();
        var l = Services.LocalizationService.Instance;

        var input = new TextBox
        {
            PasswordChar = '\u25cf',
            PlaceholderText = l["Password"]
        };

        var confirm = new Button
        {
            Content = l["Confirm"],
            Classes = { "Primary" },
            IsDefault = true
        };
        var cancel = new Button
        {
            Content = l["Cancel"],
            Classes = { "Ghost" },
            IsCancel = true
        };

        confirm.Click += (_, _) => { CloseDialog(); tcs.TrySetResult(input.Text ?? string.Empty); };
        cancel.Click += (_, _) => { CloseDialog(); tcs.TrySetResult(null); };

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

    private async void LoadSettings()
    {
        Settings = await JxTerminal.Services.DataStore.LoadSettingsAsync();
        Settings.ResolveColorScheme();
        // Hook up auto-save
        Settings.PropertyChanged += async (s, e) => {
             // Avoid saving on transient changes if possible, or debounce. 
             // For now, save on every change.
             await JxTerminal.Services.DataStore.SaveSettingsAsync(Settings);
        };
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
                session = new PtyLocalShellSession();
            }
            else if (connInfo.Type == ConnectionType.Telnet)
            {
                session = new TelnetSession(connInfo.Host, connInfo.Port);
            }
            else if (connInfo.Type == ConnectionType.Serial)
            {
                session = new SerialSession(
                    connInfo.SerialPortName,
                    connInfo.BaudRate,
                    connInfo.DataBits,
                    connInfo.Parity,
                    connInfo.StopBits,
                    connInfo.Handshake);
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
                    connInfo.Passphrase);
            }

            if (session is SshSession ssh)
            {
                 ssh.HostKeyVerifier = VerifyHostKey;
                 session.ErrorOccurred += OnConnectionError;
            }

            if (session is SerialSession serialSession)
            {
                serialSession.ErrorOccurred += OnConnectionError;
            }
            // TelnetSession checking?
            if (session is TelnetSession telnetSession)
            {
                telnetSession.ErrorOccurred += OnConnectionError;
            }


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
    /// Called on the connection worker thread; marshals the trust prompt to the UI thread and
    /// blocks until the user answers.
    /// </summary>
    private bool VerifyHostKey(string hostDisplay, string fingerprint, string? knownFingerprint)
    {
        var l = JxTerminal.Services.LocalizationService.Instance;

        var title = knownFingerprint == null ? l["HostKeyNewTitle"] : l["HostKeyChangedTitle"];
        var message = knownFingerprint == null
            ? string.Format(l["HostKeyNewMessage"], hostDisplay, fingerprint)
            : string.Format(l["HostKeyChangedMessage"], hostDisplay, knownFingerprint, fingerprint);

        return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () => ShowConfirmDialog(title, message, l["Trust"], danger: knownFingerprint != null))
            .GetAwaiter()
            .GetResult();
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
