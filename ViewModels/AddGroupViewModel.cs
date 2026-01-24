using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace JxTerminal.ViewModels;

public partial class AddGroupViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _groupName = "";

    public void Close() => RequestClose?.Invoke(this, null);
    public event EventHandler<object?>? RequestClose;

    [RelayCommand]
    public void Confirm()
    {
        if (!string.IsNullOrWhiteSpace(GroupName))
            RequestClose?.Invoke(this, GroupName);
    }

    [RelayCommand]
    public void Cancel() => RequestClose?.Invoke(this, null);
}
