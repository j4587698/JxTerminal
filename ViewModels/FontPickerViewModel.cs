using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace JxTerminal.ViewModels;

public partial class FontPickerViewModel : ViewModelBase
{
    private readonly IEnumerable<string> _allFonts;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private ObservableCollection<string> _filteredFonts;

    [ObservableProperty]
    private string? _selectedFont;

    public event EventHandler<string?>? RequestClose;

    public FontPickerViewModel(IEnumerable<string> fonts, string currentFont)
    {
        _allFonts = fonts;
        _filteredFonts = new ObservableCollection<string>(_allFonts);
        _selectedFont = _allFonts.Contains(currentFont) ? currentFont : null;
    }

    partial void OnSearchTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            FilteredFonts = new ObservableCollection<string>(_allFonts);
        }
        else
        {
            var filtered = _allFonts.Where(f => f.Contains(value, StringComparison.OrdinalIgnoreCase));
            FilteredFonts = new ObservableCollection<string>(filtered);
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        RequestClose?.Invoke(this, SelectedFont);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }
}
