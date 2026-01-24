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
    private TerminalSettings _settings = new();

    [ObservableProperty]
    private SftpViewModel? _currentSftp;

    public MainWindowViewModel()
    {
        TerminalList = new TerminalListViewModel();
        HostList = new HostListViewModel(this);
        ActiveContent = HostList;

        // Optimize: Move SVG paths here or use a better resource strategy
        var hostListIcon = StreamGeometry.Parse("M3,13H5V11H3V13M3,17H5V15H3V17M3,9H5V7H3V9M7,13H21V11H7V13M7,17H21V15H7V17M7,9H21V7H7V9M7,5V3H21V5H7Z");
        var termIcon = StreamGeometry.Parse("M20,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V6C22,4.89 21.1,4 20,4M20,18H4V6H20V18M7.5,15L9,16.5L14.5,11L9,5.5L7.5,7L11.5,11L7.5,15M13,15H19V17H13V15Z");
        var settingsIcon = StreamGeometry.Parse("M19.14,12.94C19.14,12.94 19.14,12.94 19.14,12.94L19.14,12.94C19.14,12.94 19.14,12.94 19.14,12.94M12,8A4,4 0 0,0 8,12A4,4 0 0,0 12,16A4,4 0 0,0 16,12A4,4 0 0,0 12,8M19.43,12.97C19.46,12.65 19.5,12.33 19.5,12C19.5,11.67 19.46,11.35 19.43,11.03L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.21,8.95 2.27,9.22 2.46,9.37L4.57,11.03C4.54,11.35 4.5,11.67 4.5,12C4.5,12.33 4.54,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.21,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.04 4.95,18.95L7.44,17.95C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.68 16.04,18.34 16.56,17.95L19.05,18.95C19.27,19.04 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z");

        MenuItems.Add(new MenuItemViewModel
        {
            Key = "HostList",
            Icon = hostListIcon,
            Command = NavigateToHostListCommand
        });

        MenuItems.Add(new MenuItemViewModel
        {
            Key = "Terminal",
            Icon = termIcon,
            Command = NavigateToTerminalCommand
        });

        MenuItems.Add(new MenuItemViewModel
        {
            Key = "Settings",
            Icon = settingsIcon,
            Command = NavigateToSettingsCommand
        });
    }

    [RelayCommand]
    public void NavigateToHostList() => ActiveContent = HostList;

    [RelayCommand]
    public void NavigateToTerminal() => ActiveContent = TerminalList;

    [RelayCommand]
    public void NavigateToSettings() => ActiveContent = new SettingsViewModel(Settings);

    [ObservableProperty]
    private bool _isBusy;

    public async Task Connect(SavedConnection connection)
    {
        if (IsBusy) return;
        IsBusy = true;

        ITerminalSession session;

        try
        {
            if (connection.Type == ConnectionType.Local)
            {
                session = new PtyLocalShellSession();
            }
            else if (connection.Type == ConnectionType.Telnet)
            {
                session = new TelnetSession(connection.Host, connection.Port);
            }
            else if (connection.Type == ConnectionType.Serial)
            {
                session = new SerialSession(connection.SerialPortName, connection.BaudRate);
            }
            else
            {
                string password = connection.Password;
                if (connection.AuthenticationType == SshAuthenticationType.Ask)
                {
                    // Placeholder for interactive prompt, currently relying on error feedback or provided password
                }

                session = new SshSession(
                    connection.Host, 
                    connection.Port, 
                    connection.Username, 
                    password, 
                    connection.AuthenticationType, 
                    connection.KeyFilePath, 
                    connection.Passphrase);
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

                var vm = new TerminalViewModel(session, Settings);
                TerminalList.Tabs.Add(vm);
                TerminalList.SelectedTab = vm;
                ActiveContent = TerminalList; 

                if (session is SshSession sshSession && sshSession.SftpClient != null)
                {
                    CurrentSftp = new SftpViewModel(sshSession);
                }
                else
                {
                    CurrentSftp = null;
                }
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
