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
using SukiUI.Controls;
using SukiUI.Dialogs;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using JxTerminal.Views;

namespace JxTerminal.ViewModels;

public partial class HostListViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _mainViewModel;

    public ObservableCollection<ConnectionGroup> Groups { get; private set; } = new();

    // Callback to open Add/Edit Connection UI
    // First param: List of groups, Second param: Connection to edit (nullable)
    public Action<List<string>, SavedConnection?>? RequestConnectionUI { get; set; }

    public Action? RequestAddGroupUI { get; set; }

    public HostListViewModel(MainWindowViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
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
    }

    private async void SaveData()
    {
        await DataStore.SaveConnectionsAsync(Groups);
    }

    [RelayCommand]
    public async Task AddGroup()
    {
        var vm = new AddGroupViewModel();
        var view = new AddGroupView() { DataContext = vm };
        var tcs = new TaskCompletionSource<string?>();

        ISukiDialog? dialog = null;

        void OnClose(object? sender, object? result)
        {
             vm.RequestClose -= OnClose;
             tcs.TrySetResult(result as string);
             if (dialog != null) _mainViewModel.DialogManager.TryDismissDialog(dialog);
        }
        vm.RequestClose += OnClose;

        var builder = _mainViewModel.DialogManager.CreateDialog();
        builder.SetContent(view);
        builder.SetCanDismissWithBackgroundClick(true);
        builder.SetOnDismissed(_ => {
             vm.RequestClose -= OnClose;
             tcs.TrySetResult(null);
        });

        builder.TryShow();
        dialog = builder.Dialog;

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
            SaveData();
        }
    }

    [RelayCommand]
    public void DeleteGroup(ConnectionGroup group)
    {
        if (Groups.Contains(group))
        {
            Groups.Remove(group);
            SaveData();
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
        var tcs = new TaskCompletionSource<SavedConnection?>();

        ISukiDialog? dialog = null;

        void OnClose(object? sender, object? result)
        {
             vm.RequestClose -= OnClose;
             tcs.TrySetResult(result as SavedConnection);
             if (dialog != null) _mainViewModel.DialogManager.TryDismissDialog(dialog);
        }
        vm.RequestClose += OnClose;

        var builder = _mainViewModel.DialogManager.CreateDialog();
        builder.SetContent(view);
        builder.SetCanDismissWithBackgroundClick(true);
        builder.SetOnDismissed(_ => {
             vm.RequestClose -= OnClose;
             tcs.TrySetResult(null);
        });

        builder.TryShow();
        dialog = builder.Dialog;

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
                SaveData();
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

        SaveData();
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