using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;
using JxTerminal.Models;
using Avalonia.Media;
using SukiUI.Dialogs;
using SukiUI.Controls;
using System.Linq;
using Material.Icons;

namespace JxTerminal.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase _activeContent;

    partial void OnActiveContentChanged(ViewModelBase value)
    {
        if (value is HostListViewModel)
            SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Key == "HostList");
        else if (value is TerminalListViewModel)
            SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Key == "Terminal");
    }

    public HostListViewModel HostList { get; }
    public TerminalListViewModel TerminalList { get; }

    public ObservableCollection<MenuItemViewModel> MenuItems { get; } = new();

    [ObservableProperty]
    private MenuItemViewModel? _selectedMenuItem;

    public ISukiDialogManager DialogManager { get; } = new SukiDialogManager();

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
    public void NavigateToSettings() => ActiveContent = new SettingsViewModel(Settings, DialogManager);

    [RelayCommand]
    public void NavigateToPlaceholder(string title) => ActiveContent = new PlaceholderViewModel(JxTerminal.Services.LocalizationService.Instance[title]);

    private async void LoadSettings()
    {
        Settings = await JxTerminal.Services.DataStore.LoadSettingsAsync();
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
            var dialog = DialogManager.CreateDialog()
                .WithTitle("Connection Failed")
                .WithContent($"Failed to connect to host: {ex.Message}");
            
            dialog.SetCanDismissWithBackgroundClick(true);
            dialog.TryShow();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnConnectionError(Exception ex)
    {
         Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () => {
             var dialog = DialogManager.CreateDialog()
                .WithTitle("Connection Error")
                .WithContent(ex.Message);
             
             dialog.SetCanDismissWithBackgroundClick(true);
             dialog.TryShow();
         });
    }
}
