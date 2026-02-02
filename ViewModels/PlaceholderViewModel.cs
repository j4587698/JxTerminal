using CommunityToolkit.Mvvm.ComponentModel;

namespace JxTerminal.ViewModels;

public partial class PlaceholderViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title;

    public PlaceholderViewModel(string title)
    {
        _title = title;
    }
}
