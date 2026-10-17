using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JxTerminal.Models;
using JxTerminal.Services;
using JxTerminal.Services.Terminal;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace JxTerminal.ViewModels;

/// <summary>
/// Command Center: reusable command snippets and executed command history.
/// </summary>
public partial class SnippetsViewModel : ViewModelBase
{
    private readonly MainWindowViewModel? _main;

    public ObservableCollection<SnippetItem> Snippets { get; } = new();
    public ObservableCollection<SnippetItem> FilteredSnippets { get; } = new();

    public ObservableCollection<TerminalHistoryItem> HistoryItems { get; } = new();
    public ObservableCollection<TerminalHistoryItem> FilteredHistoryItems { get; } = new();

    [ObservableProperty]
    private int _selectedTabIndex; // 0 = Snippets, 1 = History

    public bool IsSnippetsTab => SelectedTabIndex == 0;
    public bool IsHistoryTab => SelectedTabIndex == 1;

    [ObservableProperty]
    private bool _isSnippetsTabSelected = true;

    [ObservableProperty]
    private bool _isHistoryTabSelected;

    partial void OnIsSnippetsTabSelectedChanged(bool value)
    {
        if (value && SelectedTabIndex != 0)
        {
            _isHistoryTabSelected = false;
            OnPropertyChanged(nameof(IsHistoryTabSelected));
            SelectedTabIndex = 0;
        }
    }

    partial void OnIsHistoryTabSelectedChanged(bool value)
    {
        if (value && SelectedTabIndex != 1)
        {
            _isSnippetsTabSelected = false;
            OnPropertyChanged(nameof(IsSnippetsTabSelected));
            SelectedTabIndex = 1;
        }
    }

    [RelayCommand]
    public void SelectTab(string tab)
    {
        if (tab == "History")
        {
            IsHistoryTabSelected = true;
        }
        else
        {
            IsSnippetsTabSelected = true;
        }
    }

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private SnippetItem? _selectedSnippet;

    [ObservableProperty]
    private TerminalHistoryItem? _selectedHistoryItem;

    public bool HasNoSnippets => FilteredSnippets.Count == 0;
    public bool HasNoHistory => FilteredHistoryItems.Count == 0;

    public int SnippetCount => Snippets.Count;
    public int HistoryCount => HistoryItems.Count;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsSnippetsTab));
        OnPropertyChanged(nameof(IsHistoryTab));
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private readonly System.Threading.SemaphoreSlim _loadLock = new(1, 1);

    public SnippetsViewModel(MainWindowViewModel? main = null)
    {
        _main = main;
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        await _loadLock.WaitAsync();
        try
        {
            await LoadSnippetsAsync();
            await LoadHistoryAsync();
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task LoadSnippetsAsync()
    {
        var loaded = await DataStore.LoadSnippetsAsync();
        Snippets.Clear();
        foreach (var item in loaded)
        {
            Snippets.Add(item);
        }
        ApplySnippetsFilter();
        OnPropertyChanged(nameof(SnippetCount));
    }

    private async Task LoadHistoryAsync()
    {
        var limit = DataStore.LoadSettings().HistoryRetentionLimit;
        if (limit <= 0) limit = 1000;
        var loaded = await DataStore.LoadCommandHistoryItemsAsync(limit);
        HistoryItems.Clear();
        foreach (var item in loaded)
        {
            HistoryItems.Add(item);
        }
        ApplyHistoryFilter();
        OnPropertyChanged(nameof(HistoryCount));
    }

    private void ApplyFilter()
    {
        if (IsSnippetsTab) ApplySnippetsFilter();
        else ApplyHistoryFilter();
    }

    private void ApplySnippetsFilter()
    {
        FilteredSnippets.Clear();
        var query = SearchText?.Trim() ?? "";
        var items = string.IsNullOrEmpty(query)
            ? (IEnumerable<SnippetItem>)Snippets
            : Snippets.Where(s => (s.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                  (s.Command?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

        foreach (var item in items)
        {
            FilteredSnippets.Add(item);
        }
        OnPropertyChanged(nameof(HasNoSnippets));
    }

    private void ApplyHistoryFilter()
    {
        FilteredHistoryItems.Clear();
        var query = SearchText?.Trim() ?? "";
        var items = string.IsNullOrEmpty(query)
            ? (IEnumerable<TerminalHistoryItem>)HistoryItems
            : HistoryItems.Where(h => h.Command.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var item in items)
        {
            FilteredHistoryItems.Add(item);
        }
        OnPropertyChanged(nameof(HasNoHistory));
    }

    private async Task SaveSnippetsAsync()
    {
        await DataStore.SaveSnippetsAsync(Snippets);
        _ = TerminalSuggestionService.Instance.RefreshSnippetsAsync();
        ApplySnippetsFilter();
        OnPropertyChanged(nameof(SnippetCount));
    }

    private Task<SnippetEditViewModel?> OpenEditorAsync(SnippetItem? existing, bool isNew = false)
    {
        if (_main == null) return Task.FromResult<SnippetEditViewModel?>(null);

        var vm = new SnippetEditViewModel(existing, isNew);
        var view = new Views.SnippetEditView { DataContext = vm };
        var tcs = new TaskCompletionSource<SnippetEditViewModel?>();

        void OnClose(object? sender, object? result)
        {
            vm.RequestClose -= OnClose;
            tcs.TrySetResult(result as SnippetEditViewModel);
            _main.CloseDialog();
        }
        vm.RequestClose += OnClose;

        // External dismiss (Esc/overlay) resolves with null so the awaiter cannot hang.
        _main.RegisterDialogDismissFallback(() => tcs.TrySetResult(null));
        _main.ShowDialog(view, Services.LocalizationService.Instance[vm.TitleKey]);
        return tcs.Task;
    }

    [RelayCommand]
    public async Task AddSnippet()
    {
        var editor = await OpenEditorAsync(null, isNew: true);
        if (editor == null) return;

        Snippets.Insert(0, new SnippetItem
        {
            Name = editor.Name.Trim(),
            Command = editor.Command
        });
        await SaveSnippetsAsync();
    }

    [RelayCommand]
    public async Task EditSnippet(SnippetItem? item = null)
    {
        item ??= SelectedSnippet;
        if (item == null) return;

        var editor = await OpenEditorAsync(item, isNew: false);
        if (editor == null) return;

        // In-place update keeps the selection intact.
        item.Name = editor.Name.Trim();
        item.Command = editor.Command;
        await SaveSnippetsAsync();
    }

    [RelayCommand]
    public async Task DeleteSnippet(SnippetItem? item = null)
    {
        item ??= SelectedSnippet;
        if (item == null) return;

        if (_main != null)
        {
            var l = Services.LocalizationService.Instance;
            if (!await _main.ShowConfirmDialog(
                    l["DeleteConfirmTitle"],
                    string.Format(l["DeleteConfirmMessage"], item.Name),
                    l["Delete"],
                    danger: true))
            {
                return;
            }
        }

        Snippets.Remove(item);
        if (SelectedSnippet == item)
        {
            SelectedSnippet = null;
        }
        await SaveSnippetsAsync();
    }

    [RelayCommand]
    public async Task CopyCommand(string? command)
    {
        if (!string.IsNullOrEmpty(command))
        {
            await ClipboardService.SetTextAsync(command);
        }
    }

    /// <summary>Sends a snippet or history command to the active terminal tab and switches to it.</summary>
    [RelayCommand]
    public void SendToTerminal(object? parameter)
    {
        if (parameter is SnippetItem s)
        {
            _main?.SendToActiveTerminal(s.Command);
        }
        else if (parameter is TerminalHistoryItem h)
        {
            _main?.SendToActiveTerminal(h.Command);
        }
        else if (parameter is string cmd)
        {
            _main?.SendToActiveTerminal(cmd);
        }
        else if (IsSnippetsTab && SelectedSnippet != null)
        {
            _main?.SendToActiveTerminal(SelectedSnippet.Command);
        }
        else if (IsHistoryTab && SelectedHistoryItem != null)
        {
            _main?.SendToActiveTerminal(SelectedHistoryItem.Command);
        }
    }

    [RelayCommand]
    public async Task SaveHistoryAsSnippet(TerminalHistoryItem? historyItem)
    {
        var item = historyItem ?? SelectedHistoryItem;
        if (item == null || string.IsNullOrWhiteSpace(item.Command)) return;

        var tempSnippet = new SnippetItem
        {
            Command = item.Command,
            Name = ""
        };

        var editor = await OpenEditorAsync(tempSnippet, isNew: true);
        if (editor == null) return;

        Snippets.Insert(0, new SnippetItem
        {
            Name = editor.Name.Trim(),
            Command = editor.Command
        });
        await SaveSnippetsAsync();
    }

    [RelayCommand]
    public async Task DeleteHistoryItem(TerminalHistoryItem? historyItem)
    {
        var item = historyItem ?? SelectedHistoryItem;
        if (item == null) return;

        await DataStore.DeleteCommandHistoryItemAsync(item.Id);
        HistoryItems.Remove(item);
        ApplyHistoryFilter();
        SelectedHistoryItem = FilteredHistoryItems.FirstOrDefault();
        OnPropertyChanged(nameof(HistoryCount));
    }

    [RelayCommand]
    public async Task ClearHistory()
    {
        if (HistoryItems.Count == 0) return;

        if (_main != null)
        {
            var l = Services.LocalizationService.Instance;
            if (!await _main.ShowConfirmDialog(
                    l["ClearHistoryConfirmTitle"],
                    l["ClearHistoryConfirmMessage"],
                    l["ClearHistory"],
                    danger: true))
            {
                return;
            }
        }

        await DataStore.ClearCommandHistoryAsync();
        HistoryItems.Clear();
        FilteredHistoryItems.Clear();
        SelectedHistoryItem = null;
        OnPropertyChanged(nameof(HasNoHistory));
        OnPropertyChanged(nameof(HistoryCount));
    }

    [RelayCommand]
    public async Task RestoreDefaultSnippets()
    {
        if (_main != null)
        {
            var l = Services.LocalizationService.Instance;
            if (!await _main.ShowConfirmDialog(
                    l["RestoreDefaultSnippetsConfirmTitle"],
                    l["RestoreDefaultSnippetsConfirmMessage"],
                    l["RestoreDefaultSnippets"]))
            {
                return;
            }
        }

        var presets = PresetCommands.GetDefaultSnippets();
        var existingCommands = new HashSet<string>(Snippets.Select(s => s.Command?.Trim() ?? ""), StringComparer.OrdinalIgnoreCase);

        foreach (var p in presets)
        {
            if (!existingCommands.Contains(p.Command.Trim()))
            {
                Snippets.Add(p);
            }
        }

        await SaveSnippetsAsync();
    }

    [RelayCommand]
    public async Task Refresh()
    {
        await LoadAsync();
    }
}
