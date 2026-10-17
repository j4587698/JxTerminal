using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JxTerminal.Models;
using JxTerminal.Services;
using JxTerminal.Services.Sync;

namespace JxTerminal.ViewModels;

public partial class SyncViewModel : ViewModelBase
{
    private readonly MainWindowViewModel? _mainViewModel;
    private readonly Task _initTask;
    public Task InitializationTask => _initTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConfiguredServer))]
    [NotifyPropertyChangedFor(nameof(CanShowServerDisplay))]
    [NotifyPropertyChangedFor(nameof(CanShowServerEdit))]
    [NotifyPropertyChangedFor(nameof(CanCancelServerEdit))]
    private string _serverUrl = "";

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _registrationSecret = "";

    [ObservableProperty]
    private string _deviceName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowServerDisplay))]
    [NotifyPropertyChangedFor(nameof(CanShowServerEdit))]
    [NotifyPropertyChangedFor(nameof(CanCancelServerEdit))]
    [NotifyPropertyChangedFor(nameof(CanShowLoggedIn))]
    [NotifyPropertyChangedFor(nameof(CanShowLoginForm))]
    [NotifyPropertyChangedFor(nameof(CanShowDisconnected))]
    private bool _isEditingServer;

    [ObservableProperty]
    private string _editingServerUrl = "";

    [ObservableProperty]
    private string _editingDeviceName = "";

    [ObservableProperty]
    private bool _autoSyncEnabled = true;

    [ObservableProperty]
    private string _lastSyncTimeText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowLoggedIn))]
    [NotifyPropertyChangedFor(nameof(CanShowLoginForm))]
    [NotifyPropertyChangedFor(nameof(CanShowDisconnected))]
    private bool _isLoggedIn;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowLoggedIn))]
    [NotifyPropertyChangedFor(nameof(CanShowLoginForm))]
    [NotifyPropertyChangedFor(nameof(CanShowDisconnected))]
    private bool _isServerConnected;

    [ObservableProperty]
    private string _serverStatusText = "";

    [ObservableProperty]
    private string _serverStatusType = ""; // "Success", "Error", "Warning", ""

    public bool HasConfiguredServer => !string.IsNullOrWhiteSpace(ServerUrl);
    public bool CanShowServerDisplay => HasConfiguredServer && !IsEditingServer;
    public bool CanShowServerEdit => !HasConfiguredServer || IsEditingServer;
    public bool CanCancelServerEdit => HasConfiguredServer && IsEditingServer;

    public bool CanShowLoggedIn => !IsEditingServer && IsServerConnected && IsLoggedIn;
    public bool CanShowLoginForm => !IsEditingServer && IsServerConnected && !IsLoggedIn;
    public bool CanShowDisconnected => IsEditingServer || !IsServerConnected;

    public SyncViewModel(MainWindowViewModel? mainViewModel = null)
    {
        _mainViewModel = mainViewModel;
        _initTask = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await SyncService.Instance.LoadConfigAsync();
        var config = SyncService.Instance.Config;
        ServerUrl = config.ServerUrl;
        Username = config.Username;
        DeviceName = config.DeviceName;
        EditingServerUrl = config.ServerUrl;
        EditingDeviceName = string.IsNullOrWhiteSpace(config.DeviceName) ? Environment.MachineName : config.DeviceName;
        AutoSyncEnabled = config.AutoSyncEnabled;
        IsLoggedIn = SyncService.Instance.IsAuthenticated;
        IsEditingServer = !HasConfiguredServer;
        UpdateLastSyncDisplay();

        SyncService.Instance.SyncStatusChanged += OnSyncStatusChanged;
        SyncService.Instance.SyncCompleted += OnSyncCompleted;

        if (HasConfiguredServer)
        {
            _ = CheckConnectionSilentlyAsync();
        }
    }

    [RelayCommand]
    private void StartEditingServer()
    {
        EditingServerUrl = ServerUrl;
        EditingDeviceName = string.IsNullOrWhiteSpace(DeviceName) ? Environment.MachineName : DeviceName;
        IsEditingServer = true;
    }

    [RelayCommand]
    private void CancelEditingServer()
    {
        EditingServerUrl = ServerUrl;
        EditingDeviceName = DeviceName;
        IsEditingServer = false;
    }

    [RelayCommand]
    private async Task SaveServer()
    {
        if (IsBusy) return;
        var l = LocalizationService.Instance;
        var newUrl = EditingServerUrl?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(newUrl))
        {
            _mainViewModel?.ShowMessageDialog(l["Error"], l["SyncServerUrlEmpty"]);
            return;
        }

        var newDevice = string.IsNullOrWhiteSpace(EditingDeviceName)
            ? Environment.MachineName
            : EditingDeviceName.Trim();

        bool urlChanged = !string.Equals(
            newUrl.TrimEnd('/'),
            ServerUrl?.Trim().TrimEnd('/') ?? "",
            StringComparison.OrdinalIgnoreCase);

        IsBusy = true;
        try
        {
            if (urlChanged)
            {
                // Changing server completely resets the old session
                await SyncService.Instance.LogoutAsync();
                IsLoggedIn = false;
                Username = "";
                Password = "";
                RegistrationSecret = "";
                LastSyncTimeText = "";
                IsServerConnected = false;
                ServerStatusText = "";
                ServerStatusType = "";
            }

            ServerUrl = newUrl.TrimEnd('/');
            DeviceName = newDevice;
            IsEditingServer = false;

            await SaveConfigCoreAsync();

            await TestConnectionCoreAsync(showDialogOnSuccess: false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task CheckConnectionSilentlyAsync()
    {
        if (!SyncService.Instance.IsConfigured)
        {
            IsServerConnected = false;
            return;
        }

        var (ok, error) = await SyncService.Instance.TestConnectionDetailedAsync();
        var l = LocalizationService.Instance;
        if (ok)
        {
            IsServerConnected = true;
            if (IsLoggedIn)
            {
                var tokenValid = await SyncService.Instance.ValidateTokenAsync();
                if (!tokenValid && !SyncService.Instance.IsAuthenticated)
                {
                    IsLoggedIn = false;
                    ServerStatusText = $"{l["SyncConnected"]} · {l["SyncAuthInvalid"]}";
                    ServerStatusType = "Warning";
                    return;
                }
            }

            ServerStatusText = IsLoggedIn
                ? $"{l["SyncConnected"]} · {l["SyncLoggedInAs"]} ({Username})"
                : l["SyncConnected"];
            ServerStatusType = "Success";

            if (IsLoggedIn && AutoSyncEnabled)
            {
                _ = SyncService.Instance.SyncNowAsync();
            }
        }
        else
        {
            IsServerConnected = false;
            var errorMsg = !string.IsNullOrEmpty(error) ? l[error] : l["SyncConnectionFailed"];
            ServerStatusText = $"{l["SyncConnectionFailed"]} ({errorMsg})";
            ServerStatusType = "Error";
        }
    }

    partial void OnAutoSyncEnabledChanged(bool value)
    {
        if (SyncService.Instance.Config.AutoSyncEnabled != value)
        {
            SyncService.Instance.Config.AutoSyncEnabled = value;
            _ = SaveConfigCoreAsync();
            if (value && IsLoggedIn)
            {
                _ = SyncService.Instance.SyncNowAsync();
            }
        }
    }

    private void UpdateLastSyncDisplay()
    {
        var l = LocalizationService.Instance;
        var last = SyncService.Instance.Config.LastSyncTime;
        LastSyncTimeText = last.HasValue
            ? $"{l["LastSyncTime"]}: {last.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
            : $"{l["LastSyncTime"]}: {l["NeverSynced"]}";
    }

    private void OnSyncStatusChanged(string key)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var l = LocalizationService.Instance;
            StatusText = l[key];
        });
    }

    private void OnSyncCompleted(SyncResult result)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            UpdateLastSyncDisplay();
            var l = LocalizationService.Instance;
            if (result.Success)
            {
                StatusText = result.Action switch
                {
                    SyncActionTaken.Pushed => l["SyncPushed"],
                    SyncActionTaken.Pulled => l["SyncPulled"],
                    _ => l["SyncUpToDate"]
                };
            }
            else if (!string.IsNullOrEmpty(result.Message))
            {
                StatusText = $"{l["SyncFailed"]}: {l[result.Message]}";
            }
        });
    }

    private SyncConfig BuildConfig() => new()
    {
        ServerUrl = ServerUrl?.Trim() ?? "",
        Username = Username?.Trim() ?? "",
        DeviceName = DeviceName?.Trim() ?? "",
        Token = SyncService.Instance.Config.Token,
        TokenExpiresAt = SyncService.Instance.Config.TokenExpiresAt,
        EncryptedSyncKey = SyncService.Instance.Config.EncryptedSyncKey,
        AutoSyncEnabled = AutoSyncEnabled,
        LastSyncTime = SyncService.Instance.Config.LastSyncTime
    };

    private async Task SaveConfigCoreAsync()
    {
        var config = BuildConfig();
        await SyncService.Instance.SaveConfigAsync(config);
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "";
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            var l = LocalizationService.Instance;
            _mainViewModel?.ShowMessageDialog(l["Error"], $"{l["SyncFailed"]}: {l[ex.Message]}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveConfig()
    {
        await RunAsync(async () =>
        {
            await SaveConfigCoreAsync();
            StatusText = LocalizationService.Instance["SyncConfigSaved"];
            if (SyncService.Instance.IsConfigured)
            {
                _ = CheckConnectionSilentlyAsync();
            }
        });
    }

    [RelayCommand]
    private async Task Register()
    {
        await RunAsync(async () =>
        {
            var l = LocalizationService.Instance;
            await SaveConfigCoreAsync();

            var result = await SyncService.Instance.RegisterAsync(
                Username,
                Password,
                string.IsNullOrWhiteSpace(RegistrationSecret) ? null : RegistrationSecret.Trim());
            Password = "";
            Username = result.Username;
            await SaveConfigCoreAsync();
            IsLoggedIn = true;
            UpdateLastSyncDisplay();
            StatusText = l["SyncRegistered"];

            if (AutoSyncEnabled)
            {
                _ = SyncService.Instance.SyncNowAsync();
            }
        });
    }

    [RelayCommand]
    private async Task Login()
    {
        await RunAsync(async () =>
        {
            var l = LocalizationService.Instance;
            await SaveConfigCoreAsync();

            var result = await SyncService.Instance.LoginAsync(Username, Password);
            Password = "";
            Username = result.Username;
            await SaveConfigCoreAsync();
            IsLoggedIn = true;
            UpdateLastSyncDisplay();
            StatusText = l["SyncLoggedIn"];

            if (AutoSyncEnabled)
            {
                _ = SyncService.Instance.SyncNowAsync();
            }
        });
    }

    [RelayCommand]
    private async Task Logout()
    {
        await RunAsync(async () =>
        {
            await SyncService.Instance.LogoutAsync();
            IsLoggedIn = false;
            Username = "";
            Password = "";
            UpdateLastSyncDisplay();
            StatusText = LocalizationService.Instance["SyncLoggedOut"];
            if (IsServerConnected)
            {
                ServerStatusText = LocalizationService.Instance["SyncConnected"];
                ServerStatusType = "Success";
            }
        });
    }

    [RelayCommand]
    private async Task OpenChangePassword()
    {
        var l = LocalizationService.Instance;
        if (!IsServerConnected)
        {
            _mainViewModel?.ShowMessageDialog(l["Error"],
                $"{l["SyncServerDisconnectedPrompt"]}\n\n{l["SyncServerDisconnectedDesc"]}");
            return;
        }

        if (!IsLoggedIn)
        {
            _mainViewModel?.ShowMessageDialog(l["Error"], l["SyncNotLoggedIn"]);
            return;
        }

        var vm = new ChangePasswordViewModel();
        var view = new Views.ChangePasswordDialog { DataContext = vm };
        var tcs = new TaskCompletionSource<bool>();

        void OnClose(object? sender, bool success)
        {
            vm.RequestClose -= OnClose;
            tcs.TrySetResult(success);
            _mainViewModel?.CloseDialog();
        }
        vm.RequestClose += OnClose;

        _mainViewModel?.RegisterDialogDismissFallback(() => tcs.TrySetResult(false));
        _mainViewModel?.ShowDialog(view, l["SyncChangePassword"]);

        var changed = await tcs.Task;
        if (changed)
        {
            UpdateLastSyncDisplay();
            _mainViewModel?.ShowMessageDialog(l["Success"], l["SyncChangePasswordSuccess"]);
        }
    }

    private async Task<bool> TestConnectionCoreAsync(bool showDialogOnSuccess)
    {
        var l = LocalizationService.Instance;
        ServerStatusText = l["SyncStatusConnecting"];
        ServerStatusType = "";
        try
        {
            var (ok, error) = await SyncService.Instance.TestConnectionDetailedAsync();
            if (!ok)
            {
                IsServerConnected = false;
                var errorMsg = !string.IsNullOrEmpty(error) ? l[error] : l["SyncConnectionFailed"];

                ServerStatusText = $"{l["SyncConnectionFailed"]} ({errorMsg})";
                ServerStatusType = "Error";

                _mainViewModel?.ShowMessageDialog(l["Error"],
                    $"{l["SyncConnectionFailed"]}\n\n{l["SyncServerUrl"]}: {SyncService.Instance.BaseUrl}\n{l["Error"]}: {errorMsg}");
                return false;
            }

            IsServerConnected = true;

            if (IsLoggedIn)
            {
                var tokenValid = await SyncService.Instance.ValidateTokenAsync();
                if (!tokenValid && !SyncService.Instance.IsAuthenticated)
                {
                    IsLoggedIn = false;
                    ServerStatusText = $"{l["SyncConnected"]} · {l["SyncAuthInvalid"]}";
                    ServerStatusType = "Warning";
                    _mainViewModel?.ShowMessageDialog(l["Warning"],
                        $"{l["SyncConnected"]}\n\n{l["SyncAuthInvalid"]}");
                    return false;
                }
            }

            ServerStatusText = IsLoggedIn
                ? $"{l["SyncConnected"]} · {l["SyncLoggedInAs"]} ({Username})"
                : l["SyncConnected"];
            ServerStatusType = "Success";

            if (showDialogOnSuccess)
            {
                _mainViewModel?.ShowMessageDialog(l["Success"],
                    $"{l["SyncConnected"]}\n\n{l["SyncServerUrl"]}: {SyncService.Instance.BaseUrl}");
            }

            if (IsLoggedIn && AutoSyncEnabled)
            {
                _ = SyncService.Instance.SyncNowAsync();
            }

            return true;
        }
        catch (Exception ex)
        {
            IsServerConnected = false;
            ServerStatusText = $"{l["SyncConnectionFailed"]} ({ex.Message})";
            ServerStatusType = "Error";
            _mainViewModel?.ShowMessageDialog(l["Error"],
                $"{l["SyncConnectionFailed"]}\n\n{l["SyncServerUrl"]}: {SyncService.Instance.BaseUrl}\n{l["Error"]}: {ex.Message}");
            return false;
        }
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await SaveConfigCoreAsync();
            await TestConnectionCoreAsync(showDialogOnSuccess: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SyncNow()
    {
        await RunAsync(async () =>
        {
            var l = LocalizationService.Instance;
            StatusText = l["SyncStatusSyncing"];
            var result = await SyncService.Instance.SyncNowAsync();
            UpdateLastSyncDisplay();
            if (result.Success)
            {
                StatusText = result.Action switch
                {
                    SyncActionTaken.Pushed => l["SyncPushed"],
                    SyncActionTaken.Pulled => l["SyncPulled"],
                    _ => l["SyncUpToDate"]
                };
            }
            else
            {
                StatusText = $"{l["SyncFailed"]}: {l[result.Message]}";
            }
        });
    }
}
