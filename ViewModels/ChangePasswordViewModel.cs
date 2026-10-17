using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JxTerminal.Services;
using JxTerminal.Services.Sync;

namespace JxTerminal.ViewModels;

public partial class ChangePasswordViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _currentPassword = "";

    [ObservableProperty]
    private string _newPassword = "";

    [ObservableProperty]
    private string _confirmPassword = "";

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _isBusy;

    public event EventHandler<bool>? RequestClose;

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(this, false);

    [RelayCommand]
    private async Task Confirm()
    {
        if (IsBusy) return;

        var l = LocalizationService.Instance;
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(CurrentPassword))
        {
            ErrorMessage = l["SyncErrorCurrentPasswordIncorrect"];
            return;
        }

        if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 6)
        {
            ErrorMessage = l["SyncErrorPasswordTooShort"];
            return;
        }

        if (NewPassword != ConfirmPassword)
        {
            ErrorMessage = l["SyncPasswordMismatch"];
            return;
        }

        IsBusy = true;
        try
        {
            await SyncService.Instance.ChangePasswordAsync(CurrentPassword, NewPassword);
            RequestClose?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            ErrorMessage = l[msg];
        }
        finally
        {
            IsBusy = false;
        }
    }
}
