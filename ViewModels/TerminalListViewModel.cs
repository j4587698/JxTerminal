using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JxTerminal.ViewModels;

public partial class TerminalListViewModel : ViewModelBase
{
    public ObservableCollection<TerminalViewModel> Tabs { get; } = new();

    [ObservableProperty]
    private TerminalViewModel? _selectedTab;
}
