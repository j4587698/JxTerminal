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
                session = new SerialSession(connInfo.SerialPortName, connInfo.BaudRate);
            }
            else
            {
                string password = connInfo.Password;
                if (connInfo.AuthenticationType == SshAuthenticationType.Ask)
                {
                    // Placeholder for interactive prompt, currently relying on error feedback or provided password
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

            if (session is SshSession)
            {
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
        }
        catch (Exception ex)
        {
            ShowMessageDialog("Connection Failed", $"Failed to connect to host: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnConnectionError(Exception ex)
    {
         Avalonia.Threading.Dispatcher.UIThread.Post(() =>
             ShowMessageDialog("Connection Error", ex.Message));
    }
}
