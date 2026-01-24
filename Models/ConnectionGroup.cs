using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JxTerminal.Models;

public partial class ConnectionGroup : ObservableObject
{
    [ObservableProperty]
    private string _name;

    public ObservableCollection<SavedConnection> Connections { get; } = new();

    public ConnectionGroup(string name)
    {
        Name = name;
    }
}
