using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;
using TinyDb.Attributes;

namespace JxTerminal.Models;

[Entity("connection_groups")]
public partial class ConnectionGroup : ObservableObject
{
    [Id]
    public Guid Id { get; set; } = Guid.NewGuid();
    private string _name = "";
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    [JsonInclude]
    [BsonIgnore]
    public ObservableCollection<SavedConnection> Connections { get; set; } = new();

    public List<SavedConnection> SavedConnections
    {
        get => new(Connections);
        set
        {
            Connections.Clear();
            if (value != null)
            {
                foreach (var item in value) Connections.Add(item);
            }
        }
    }

    public ConnectionGroup()
    {
        Name = "New Group";
    }

    public ConnectionGroup(string name)
    {
        Name = name;
    }
}
