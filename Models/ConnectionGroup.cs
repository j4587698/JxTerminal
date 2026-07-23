using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace JxTerminal.Models;

public partial class ConnectionGroup : ObservableObject
{
    private string _name = "";
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    [JsonInclude]
    public ObservableCollection<SavedConnection> Connections { get; set; } = new();

    public ConnectionGroup()
    {
        Name = "New Group";
    }

    public ConnectionGroup(string name)
    {
        Name = name;
    }
}
