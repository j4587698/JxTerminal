using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace JxTerminal.ViewModels;

public partial class MenuItemViewModel : ViewModelBase
{
    [ObservableProperty] private string _header = "";
    [ObservableProperty] private StreamGeometry? _icon;
    [ObservableProperty] private string _key = "";
    [ObservableProperty] private bool _isSeparator;
    [ObservableProperty] private ObservableCollection<MenuItemViewModel> _children = new();

    // Command to execute when this item is clicked
    public ICommand? Command { get; set; }
    public object? CommandParameter { get; set; }

    partial void OnKeyChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            UpdateHeader();
            Services.LocalizationService.Instance.PropertyChanged += (_, _) => UpdateHeader();
        }
    }

    private void UpdateHeader()
    {
        if (!string.IsNullOrEmpty(Key))
        {
            Header = Services.LocalizationService.Instance[Key];
        }
    }
}
