using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JxTerminal.Models;

namespace JxTerminal.ViewModels;

public partial class TerminalListViewModel : ViewModelBase
{
    public ObservableCollection<TerminalViewModel> Tabs { get; } = new();

    [ObservableProperty]
    private TerminalViewModel? _selectedTab;

    partial void OnSelectedTabChanged(TerminalViewModel? value)
    {
        foreach (var tab in Tabs)
            tab.IsActiveTab = tab == value;

        if (value != null)
            value.HasUnseenOutput = false;
    }

    [ObservableProperty]
    private bool _hasTabs;

    public event Action? RequestCreateConnection;
    public event Action<SavedConnection>? DuplicateRequested;

    public TerminalListViewModel()
    {
        Tabs.CollectionChanged += Tabs_CollectionChanged;
        HasTabs = Tabs.Count > 0;
    }

    private void Tabs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasTabs = Tabs.Count > 0;
        
        if (e.NewItems != null)
        {
            foreach (TerminalViewModel newItem in e.NewItems)
            {
                newItem.RequestClose += OnTabRequestClose;
            }
        }

        if (e.OldItems != null)
        {
            foreach (TerminalViewModel oldItem in e.OldItems)
            {
                oldItem.RequestClose -= OnTabRequestClose;
            }
        }
    }

    private void OnTabRequestClose(TerminalViewModel tab)
    {
        Tabs.Remove(tab);
        tab.IsActiveTab = false;
        tab.Dispose(); // Ensure resources are freed

        // Re-tag the remaining tabs so exactly one is marked active.
        if (SelectedTab != null) SelectedTab.IsActiveTab = true;
    }

    [RelayCommand]
    private void CreateConnection()
    {
        RequestCreateConnection?.Invoke();
    }

    [RelayCommand]
    private void CloseCurrentTab()
    {
        if (SelectedTab != null) OnTabRequestClose(SelectedTab);
    }

    [RelayCommand]
    private void NextTab() => CycleTab(1);

    [RelayCommand]
    private void PreviousTab() => CycleTab(-1);

    /// <summary>Ctrl+1..9 support: jumps to the Nth tab (1-based), ignoring out-of-range.</summary>
    public void SelectTabByIndex(int index)
    {
        if (index < 0 || index >= Tabs.Count) return;
        SelectedTab = Tabs[index];
    }

    [RelayCommand]
    private void SelectTabIndex(string? indexStr)
    {
        if (int.TryParse(indexStr, out int index)) SelectTabByIndex(index);
    }

    private void CycleTab(int offset)
    {
        if (Tabs.Count < 2 || SelectedTab == null) return;
        var index = Tabs.IndexOf(SelectedTab);
        if (index < 0) return;
        SelectedTab = Tabs[((index + offset) % Tabs.Count + Tabs.Count) % Tabs.Count];
    }

    public void CloseTab(TerminalViewModel tab) => OnTabRequestClose(tab);

    [RelayCommand]
    private void DuplicateTab(TerminalViewModel tab)
    {
        if (tab?.ConnectionInfo != null)
        {
            DuplicateRequested?.Invoke(tab.ConnectionInfo);
        }
    }

    [RelayCommand]
    private void CloseOtherTabs(TerminalViewModel tab)
    {
        var toRemove = Tabs.Where(t => t != tab).ToList();
        foreach (var t in toRemove)
        {
            OnTabRequestClose(t);
        }
    }

    [RelayCommand]
    private void CloseLeftTabs(TerminalViewModel tab)
    {
        int index = Tabs.IndexOf(tab);
        if (index <= 0) return;

        var toRemove = new System.Collections.Generic.List<TerminalViewModel>();
        for (int i = 0; i < index; i++)
        {
            toRemove.Add(Tabs[i]);
        }
        
        foreach (var t in toRemove)
        {
            OnTabRequestClose(t);
        }
    }

    [RelayCommand]
    private void CloseRightTabs(TerminalViewModel tab)
    {
        int index = Tabs.IndexOf(tab);
        if (index < 0 || index >= Tabs.Count - 1) return;

        var toRemove = new System.Collections.Generic.List<TerminalViewModel>();
        for (int i = index + 1; i < Tabs.Count; i++)
        {
            toRemove.Add(Tabs[i]);
        }

        foreach (var t in toRemove)
        {
            OnTabRequestClose(t);
        }
    }
}
