using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using JxTerminal.Models;
using JxTerminal.Services;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using JxTerminal.Views;
using Material.Icons;

namespace JxTerminal.ViewModels;

public enum NavigationType
{
    AllHosts,
    GroupRoot,
    GroupItem
}

public partial class NavigationItem : ObservableObject
{    [ObservableProperty] private string _title = "";
    [ObservableProperty] private MaterialIconKind _icon;
    [ObservableProperty] private NavigationType _type;
    [ObservableProperty] private ConnectionGroup? _group;
    [ObservableProperty] private int _count;
    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private bool _isSelected;

    public ObservableCollection<NavigationItem> Children { get; } = new();

    public NavigationItem(string title, MaterialIconKind icon, NavigationType type, ConnectionGroup? group = null)
    {
        Title = title;
        Icon = icon;
        Type = type;
        Group = group;
        if (group != null)
        {
            Count = group.Connections.Count;
            // Assuming we might want to listen to collection changes in the future for live updates
        }
    }
}

/// <summary>
/// A protocol choice in the host list filter bar. Keeps the strongly typed value separate from
/// the localized label so that switching language never breaks filtering.
/// </summary>
public partial class ProtocolFilterOption : ObservableObject
{
    public ConnectionType? Type { get; }

    [ObservableProperty]
    private string _display = "";

    public ProtocolFilterOption(ConnectionType? type)
    {
        Type = type;
        Refresh();
    }

    public void Refresh()
    {
        Display = Type switch
        {
            null => LocalizationService.Instance["ProtocolFilterAll"],
            ConnectionType.Ssh => "SSH",
            ConnectionType.Telnet => "Telnet",
            ConnectionType.Serial => LocalizationService.Instance["Serial"],
            _ => LocalizationService.Instance["Local"]
        };
    }
}

public partial class HostListViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _mainViewModel;

    public ObservableCollection<ConnectionGroup> Groups { get; private set; } = new();

    // Tree Navigation Source
    public ObservableCollection<NavigationItem> NavigationItems { get; } = new();

    [ObservableProperty]
    private NavigationItem? _selectedNavigationItem;

    partial void OnSelectedNavigationItemChanged(NavigationItem? value)
    {
        if (value == null) return;

        if (value.Type == NavigationType.AllHosts || value.Type == NavigationType.GroupRoot)
        {
            SelectedGroup = null;
        }
        else if (value.Type == NavigationType.GroupItem)
        {
            SelectedGroup = value.Group;
        }
    }

    // Filtered view for the UI
    public ObservableCollection<ConnectionGroup> DisplayGroups { get; } = new();

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private ConnectionGroup? _selectedGroup;

    [ObservableProperty]
    private ProtocolFilterOption? _selectedProtocolFilter;

    public ObservableCollection<ProtocolFilterOption> ProtocolFilters { get; } = new()
    {
        new ProtocolFilterOption(null),
        new ProtocolFilterOption(ConnectionType.Ssh),
        new ProtocolFilterOption(ConnectionType.Telnet),
        new ProtocolFilterOption(ConnectionType.Serial),
        new ProtocolFilterOption(ConnectionType.Local)
    };

    partial void OnSearchTextChanged(string value) => UpdateDisplay();
    partial void OnSelectedGroupChanged(ConnectionGroup? value) => UpdateDisplay();
    partial void OnSelectedProtocolFilterChanged(ProtocolFilterOption? value) => UpdateDisplay();

    // Callback to open Add/Edit Connection UI
    // First param: List of groups, Second param: Connection to edit (nullable)
    public Action<List<string>, SavedConnection?>? RequestConnectionUI { get; set; }

    public Action? RequestAddGroupUI { get; set; }

    public HostListViewModel(MainWindowViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
        SelectedProtocolFilter = ProtocolFilters[0];
        LocalizationService.Instance.PropertyChanged += (_, _) => {
            RebuildNavigationTree();
            foreach (var filter in ProtocolFilters) filter.Refresh();
        };
        LoadData();
    }

    private async void LoadData()
    {
        var loaded = await DataStore.LoadConnectionsAsync();
        Groups.Clear();
        foreach (var group in loaded)
        {
            Groups.Add(group);
        }
        RebuildNavigationTree();
        UpdateDisplay();
    }

    private void RebuildNavigationTree()
    {
        NavigationItems.Clear();

        // Root Node: All Hosts
        var allHostsNode = new NavigationItem(LocalizationService.Instance["AllHosts"], MaterialIconKind.ViewDashboardOutline, NavigationType.AllHosts)
        {
            IsSelected = true // Default selection
        };
        NavigationItems.Add(allHostsNode);

        // Group Root Node
        var groupsRoot = new NavigationItem(LocalizationService.Instance["Group"], MaterialIconKind.FolderMultipleOutline, NavigationType.GroupRoot);
        
        foreach (var group in Groups)
        {
            var groupNode = new NavigationItem(group.Name, MaterialIconKind.FolderOutline, NavigationType.GroupItem, group);
            groupsRoot.Children.Add(groupNode);
        }

        NavigationItems.Add(groupsRoot);
    }

    private void UpdateDisplay()
    {
        DisplayGroups.Clear();

        // 1. Filter by Search Text
        var search = SearchText?.Trim().ToLower();
        bool hasSearch = !string.IsNullOrEmpty(search);

        // 2. Filter by Selected Group
        var sourceGroups = SelectedGroup != null 
            ? new[] { SelectedGroup } 
            : Groups.ToArray();

        foreach (var group in sourceGroups)
        {
            // Filter connections within the group
            var matchingConnections = group.Connections.AsEnumerable();

            // A. Protocol Filter
            var typeFilter = SelectedProtocolFilter?.Type;
            if (typeFilter.HasValue)
            {
                matchingConnections = matchingConnections.Where(c => c.Type == typeFilter.Value);
            }

            // B. Search Text Filter
            if (hasSearch)
            {
                matchingConnections = matchingConnections.Where(c => 
                    c.Name.ToLower().Contains(search!) || 
                    c.Host.ToLower().Contains(search!) || 
                    c.Username.ToLower().Contains(search!));
            }

            var filteredList = matchingConnections.ToList();

            if (filteredList.Any())
            {
                // Create a temporary group for display
                var tempGroup = new ConnectionGroup(group.Name);
                foreach(var c in filteredList) tempGroup.Connections.Add(c);
                DisplayGroups.Add(tempGroup);
            }
        }
    }

    [RelayCommand]
    public void SelectAllHosts()
    {
        SelectedGroup = null;
    }

    [RelayCommand]
    public void SelectGroup(ConnectionGroup group)
    {
        SelectedGroup = group;
    }

    private async void SaveData()
    {
        await DataStore.SaveConnectionsAsync(Groups);
        RebuildNavigationTree();
        UpdateDisplay();
    }

    [RelayCommand]
    public async Task AddGroup()
    {
        var vm = new AddGroupViewModel();
        var view = new AddGroupView() { DataContext = vm };
        view.Margin = new Thickness(24);
        var tcs = new TaskCompletionSource<string?>();

        void OnClose(object? sender, object? result)
        {
             vm.RequestClose -= OnClose;
             tcs.TrySetResult(result as string);
             _mainViewModel.CloseDialog();
        }
        vm.RequestClose += OnClose;

        _mainViewModel.ShowDialog(view);

        var resultName = await tcs.Task;

        if (!string.IsNullOrWhiteSpace(resultName))
        {
            CreateGroup(resultName);
        }
    }

    public void CreateGroup(string name)
    {
        if (!string.IsNullOrWhiteSpace(name) && !Groups.Any(g => g.Name == name))
        {
            Groups.Add(new ConnectionGroup(name));
            SaveData(); // SaveData calls UpdateDisplay
        }
    }

    [RelayCommand]
    public void DeleteGroup(ConnectionGroup group)
    {
        if (Groups.Contains(group))
        {
            Groups.Remove(group);
            SaveData(); // SaveData calls UpdateDisplay
        }
    }

    [RelayCommand]
    public void AddConnection()
    {
        var groupNames = Groups.Select(g => g.Name).ToList();
        OpenConnectionDialog(groupNames, null);
    }

    [RelayCommand]
    public void EditConnection(SavedConnection connection)
    {
        var groupNames = Groups.Select(g => g.Name).ToList();
        OpenConnectionDialog(groupNames, connection);
    }

    private async void OpenConnectionDialog(List<string> groupNames, SavedConnection? existing)
    {
        var vm = existing != null
            ? new AddConnectionViewModel(existing, groupNames)
            : new AddConnectionViewModel(groupNames);

        var view = new AddConnectionView() { DataContext = vm };
        view.Margin = new Thickness(24);
        var tcs = new TaskCompletionSource<SavedConnection?>();

        void OnClose(object? sender, object? result)
        {
             vm.RequestClose -= OnClose;
             tcs.TrySetResult(result as SavedConnection);
             _mainViewModel.CloseDialog();
        }
        vm.RequestClose += OnClose;

        _mainViewModel.ShowDialog(view);

        var connection = await tcs.Task;

        if (connection != null)
        {
            HandleConnectionResult(connection);
        }
    }
    
    [RelayCommand]
    public void DeleteConnection(SavedConnection connection)
    {
        foreach (var group in Groups)
        {
            if (group.Connections.Contains(connection))
            {
                group.Connections.Remove(connection);
                SaveData(); // SaveData calls UpdateDisplay
                return;
            }
        }
    }

    public void HandleConnectionResult(SavedConnection result)
    {
        // Find existing connection across all groups
        SavedConnection? existing = null;
        ConnectionGroup? existingGroup = null;

        foreach (var group in Groups)
        {
            existing = group.Connections.FirstOrDefault(c => c.Id == result.Id);
            if (existing != null)
            {
                existingGroup = group;
                break;
            }
        }

        if (existing != null && existingGroup != null)
        {
            // Remove from old group
            existingGroup.Connections.Remove(existing);
        }

        // Add to new (or same) group based on result.GroupName
        AddConnectionToGroup(result);

        SaveData(); // SaveData calls UpdateDisplay
    }

    private void AddConnectionToGroup(SavedConnection connection)
    {
        var group = Groups.FirstOrDefault(g => g.Name == connection.GroupName);
        if (group == null)
        {
            group = new ConnectionGroup(connection.GroupName);
            Groups.Add(group);
        }
        group.Connections.Add(connection);
    }

    [RelayCommand]
    public async Task Connect(SavedConnection connection)
    {
        if (connection == null) return;
        await _mainViewModel.Connect(connection);
    }

    [RelayCommand]
    public async Task QuickConnectLocal()
    {
        var localConnection = new SavedConnection
        {
            Name = "Local Shell",
            Type = ConnectionType.Local
        };
        await _mainViewModel.Connect(localConnection);
    }
}
