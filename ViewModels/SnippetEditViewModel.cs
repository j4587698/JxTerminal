using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JxTerminal.Models;
using System;

namespace JxTerminal.ViewModels;

/// <summary>
/// Dialog view model for creating or editing a command snippet.
/// </summary>
public partial class SnippetEditViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _command = "";

    /// <summary>True when creating a new snippet, false when editing an existing one.</summary>
    public bool IsNew { get; }

    public string TitleKey => IsNew ? "NewSnippet" : "RenameItem";

    public event EventHandler<object?>? RequestClose;

    public SnippetEditViewModel(SnippetItem? existing = null)
    {
        IsNew = existing == null;
        if (existing != null)
        {
            _name = existing.Name;
            _command = existing.Command;
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        if (!string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Command))
            RequestClose?.Invoke(this, this);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(this, null);
}
