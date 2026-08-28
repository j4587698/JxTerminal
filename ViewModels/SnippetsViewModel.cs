using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JxTerminal.Models;
using JxTerminal.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace JxTerminal.ViewModels;

/// <summary>
/// Snippets page: reusable command snippets that can be sent to the active terminal.
/// </summary>
public partial class SnippetsViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _main;

    public ObservableCollection<SnippetItem> Snippets { get; } = new();

    [ObservableProperty]
    private SnippetItem? _selectedSnippet;

    public bool HasNoSnippets => Snippets.Count == 0;

    public SnippetsViewModel(MainWindowViewModel main)
    {
        _main = main;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var loaded = await DataStore.LoadSnippetsAsync();
        Snippets.Clear();
        foreach (var item in loaded)
        {
            Snippets.Add(item);
        }
        OnPropertyChanged(nameof(HasNoSnippets));
    }

    private async Task SaveAsync()
    {
        await DataStore.SaveSnippetsAsync(Snippets);
        OnPropertyChanged(nameof(HasNoSnippets));
    }

    private Task<SnippetEditViewModel?> OpenEditorAsync(SnippetItem? existing)
    {
        var vm = new SnippetEditViewModel(existing);
        var view = new Views.SnippetEditView { DataContext = vm };
        view.Margin = new Avalonia.Thickness(24);
        var tcs = new TaskCompletionSource<SnippetEditViewModel?>();

        void OnClose(object? sender, object? result)
        {
            vm.RequestClose -= OnClose;
            tcs.TrySetResult(result as SnippetEditViewModel);
            _main.CloseDialog();
        }
        vm.RequestClose += OnClose;

        _main.ShowDialog(view, Services.LocalizationService.Instance[vm.TitleKey]);
        return tcs.Task;
    }

    [RelayCommand]
    public async Task AddSnippet()
    {
        var editor = await OpenEditorAsync(null);
        if (editor == null) return;

        Snippets.Add(new SnippetItem
        {
            Name = editor.Name.Trim(),
            Command = editor.Command
        });
        await SaveAsync();
    }

    [RelayCommand]
    public async Task EditSnippet()
    {
        var item = SelectedSnippet;
        if (item == null) return;

        var editor = await OpenEditorAsync(item);
        if (editor == null) return;

        // In-place update keeps the selection intact.
        item.Name = editor.Name.Trim();
        item.Command = editor.Command;
        await SaveAsync();
    }

    [RelayCommand]
    public async Task DeleteSnippet()
    {
        var item = SelectedSnippet;
        if (item == null) return;

        var l = Services.LocalizationService.Instance;
        if (!await _main.ShowConfirmDialog(
                l["DeleteConfirmTitle"],
                string.Format(l["DeleteConfirmMessage"], item.Name),
                l["Delete"],
                danger: true))
        {
            return;
        }

        Snippets.Remove(item);
        SelectedSnippet = Snippets.FirstOrDefault();
        await SaveAsync();
    }

    /// <summary>Sends the snippet to the active terminal tab and switches to it.</summary>
    [RelayCommand]
    private void SendToTerminal(SnippetItem? item)
    {
        if (item == null) return;
        _main.SendToActiveTerminal(item.Command);
    }
}
