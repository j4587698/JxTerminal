using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.IO;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using JxTerminal.Models;
using JxTerminal.Services;
using JxTerminal.Services.ImportExport;
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
            group.Connections.CollectionChanged += (_, _) => Count = group.Connections.Count;
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

    /// <summary>True when the filtered host list is empty; drives the empty-state overlay.</summary>
    public bool HasNoVisibleHosts => DisplayGroups.Count == 0;

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

    /// <summary>Raised when the global search entry (Ctrl+K) wants the search box focused.</summary>
    public event Action? SearchFocusRequested;

    public void RequestSearchFocus() => SearchFocusRequested?.Invoke();

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

    /// <summary>
    /// Re-reads connections from the data store. Used after a cloud restore so the
    /// host list reflects the restored configuration immediately.
    /// </summary>
    public void ReloadData() => LoadData();

    private async void LoadData()
    {
        try
        {
            var loaded = await DataStore.LoadConnectionsAsync();
            Groups.Clear();
            foreach (var group in loaded)
            {
                Groups.Add(group);
            }
            RebuildNavigationTree();
            UpdateDisplay();
            _ = PingAllVisibleHostsAsync();

            if (DataStore.ConnectionsLoadFailed)
            {
                var l = LocalizationService.Instance;
                _mainViewModel.ShowMessageDialog(l["Warning"], l["ConnectionsFileCorrupt"]);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"LoadData failed: {ex.Message}");
        }
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

        OnPropertyChanged(nameof(HasNoVisibleHosts));
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
        try
        {
            await DataStore.SaveConnectionsAsync(Groups);
            RebuildNavigationTree();
            UpdateDisplay();
        }
        catch (Exception ex)
        {
            AppLog.Error($"SaveData failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task AddGroup()
    {
        var vm = new AddGroupViewModel();
        var view = new AddGroupView() { DataContext = vm };
        var tcs = new TaskCompletionSource<string?>();

        void OnClose(object? sender, object? result)
        {
             vm.RequestClose -= OnClose;
             tcs.TrySetResult(result as string);
             _mainViewModel.CloseDialog();
        }
        vm.RequestClose += OnClose;

        // External dismiss (Esc/overlay) resolves with null so the awaiter cannot hang.
        _mainViewModel.RegisterDialogDismissFallback(() => tcs.TrySetResult(null));
        _mainViewModel.ShowDialog(view, LocalizationService.Instance["CreateGroupTitle"]);

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
    public async Task DeleteGroup(ConnectionGroup group)
    {
        if (!Groups.Contains(group)) return;

        var l = LocalizationService.Instance;
        if (!await _mainViewModel.ShowConfirmDialog(
                l["DeleteConfirmTitle"],
                string.Format(l["DeleteConfirmMessage"], group.Name),
                l["Delete"],
                danger: true))
        {
            return;
        }

        Groups.Remove(group);
        SaveData(); // SaveData calls UpdateDisplay
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
        try
        {
            var vm = existing != null
                ? new AddConnectionViewModel(existing, groupNames)
                : new AddConnectionViewModel(groupNames);

            var view = new AddConnectionView() { DataContext = vm };
            var tcs = new TaskCompletionSource<SavedConnection?>();

            void OnClose(object? sender, object? result)
            {
                 vm.RequestClose -= OnClose;
                 tcs.TrySetResult(result as SavedConnection);
                 _mainViewModel.CloseDialog();
            }
            vm.RequestClose += OnClose;

            // External dismiss (Esc/overlay) resolves with null so the awaiter cannot hang.
            _mainViewModel.RegisterDialogDismissFallback(() => tcs.TrySetResult(null));
            _mainViewModel.ShowDialog(view, LocalizationService.Instance["ConnectionDetails"]);

            var connection = await tcs.Task;

            if (connection != null)
            {
                HandleConnectionResult(connection);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"OpenConnectionDialog failed: {ex.Message}");
        }
    }
    
    [RelayCommand]
    public async Task DeleteConnection(SavedConnection connection)
    {
        foreach (var group in Groups)
        {
            if (!group.Connections.Contains(connection)) continue;

            var l = LocalizationService.Instance;
            if (!await _mainViewModel.ShowConfirmDialog(
                    l["DeleteConfirmTitle"],
                    string.Format(l["DeleteConfirmMessage"], connection.Name),
                    l["Delete"],
                    danger: true))
            {
                return;
            }

            group.Connections.Remove(connection);
            SaveData(); // SaveData calls UpdateDisplay
            return;
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
    public void DuplicateConnection(SavedConnection connection)
    {
        if (connection == null) return;
        var copy = connection.Duplicate();
        AddConnectionToGroup(copy);
        SaveData();
    }

    [RelayCommand]
    public async Task ExportConnections()
    {
        var l = LocalizationService.Instance;
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.StorageProvider is { } storage)
            {
                var file = await storage.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = l["ExportConnections"],
                    SuggestedFileName = "jxterminal-connections.json"
                });

                if (file != null)
                {
                    var fullPath = file.Path.LocalPath;
                    await ImportExportService.Instance.ExportConnectionsAsync(fullPath, Groups);
                    _mainViewModel.ShowMessageDialog(l["Success"], l["ExportSuccess"]);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"ExportConnections failed: {ex.Message}");
            _mainViewModel.ShowMessageDialog(l["Error"], string.Format(l["ExportFailed"], ex.Message));
        }
    }

    [RelayCommand]
    public async Task ImportConnections()
    {
        var l = LocalizationService.Instance;
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.StorageProvider is { } storage)
            {
                var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = l["ImportConnections"],
                    AllowMultiple = false
                });

                if (files.Count > 0)
                {
                    var fullPath = files[0].Path.LocalPath;
                    var imported = await ImportExportService.Instance.ImportConnectionsAsync(fullPath);

                    int total = 0;
                    int decryptFailed = 0;
                    foreach (var g in imported)
                    {
                        // Regenerate IDs so importing the same file twice cannot produce
                        // duplicate identities that confuse edit/delete lookups.
                        foreach (var c in g.Connections)
                        {
                            c.Id = Guid.NewGuid();
                            total++;
                            if (c.PasswordDecryptFailed) decryptFailed++;
                        }

                        var existing = Groups.FirstOrDefault(x => x.Name == g.Name);
                        if (existing == null)
                        {
                            Groups.Add(g);
                        }
                        else
                        {
                            foreach (var c in g.Connections) existing.Connections.Add(c);
                        }
                    }
                    SaveData();
                    _mainViewModel.ShowMessageDialog(l["Success"], string.Format(l["ImportSuccess"], total));

                    // Passwords were encrypted with the origin machine's master key; on a
                    // different machine they cannot be decrypted.
                    if (decryptFailed > 0)
                    {
                        _mainViewModel.ShowMessageDialog(l["Warning"],
                            string.Format(l["ImportPasswordsLost"], decryptFailed));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"ImportConnections failed: {ex.Message}");
            _mainViewModel.ShowMessageDialog(l["Error"], string.Format(l["ImportFailed"], ex.Message));
        }
    }

    [RelayCommand]
    public async Task ImportSshConfig()
    {
        var l = LocalizationService.Instance;
        try
        {
            var defaultSshConfig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");

            string content = "";
            if (File.Exists(defaultSshConfig))
            {
                content = await File.ReadAllTextAsync(defaultSshConfig);
            }
            else if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.StorageProvider is { } storage)
            {
                var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = l["ImportSshConfig"],
                    AllowMultiple = false
                });
                if (files.Count > 0)
                {
                    content = await File.ReadAllTextAsync(files[0].Path.LocalPath);
                }
            }

            if (!string.IsNullOrWhiteSpace(content))
            {
                var connections = ImportExportService.Instance.ParseOpenSshConfig(content);
                if (connections.Count > 0)
                {
                    var group = Groups.FirstOrDefault(g => g.Name == "SSH Config");
                    if (group == null)
                    {
                        group = new ConnectionGroup("SSH Config");
                        Groups.Add(group);
                    }
                    foreach (var c in connections)
                    {
                        c.Id = Guid.NewGuid();
                        group.Connections.Add(c);
                    }
                    SaveData();
                    _mainViewModel.ShowMessageDialog(l["Success"], string.Format(l["ImportSuccess"], connections.Count));
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"ImportSshConfig failed: {ex.Message}");
            _mainViewModel.ShowMessageDialog(l["Error"], string.Format(l["ImportFailed"], ex.Message));
        }
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

    [RelayCommand]
    public void RefreshPing()
    {
        _ = PingAllVisibleHostsAsync();
    }

    [RelayCommand]
    public async Task PingSingle(SavedConnection connection)
    {
        if (connection == null) return;
        await PingHostAsync(connection);
    }

    public async Task PingAllVisibleHostsAsync()
    {
        var connectionsToPing = Groups
            .SelectMany(g => g.Connections)
            .Where(c => c.CanPing)
            .Distinct()
            .ToList();

        if (connectionsToPing.Count == 0) return;

        using var semaphore = new System.Threading.SemaphoreSlim(6);
        var tasks = connectionsToPing.Select(async conn =>
        {
            await semaphore.WaitAsync();
            try
            {
                await PingHostAsync(conn);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    public async Task PingHostAsync(SavedConnection connection)
    {
        if (!connection.CanPing) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            connection.PingStatus = PingStatus.Pinging;
        });

        var latency = await JxTerminal.Services.Network.PingService.MeasureLatencyAsync(connection.Host, connection.Port);

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (latency.HasValue)
            {
                connection.PingMs = latency.Value;
                connection.PingStatus = PingStatus.Success;
            }
            else
            {
                connection.PingMs = null;
                connection.PingStatus = PingStatus.Timeout;
            }
        });
    }
}
