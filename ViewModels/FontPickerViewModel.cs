using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace JxTerminal.ViewModels;

public partial class FontPickerViewModel : ViewModelBase
{
    private readonly IReadOnlyList<string> _monospaceFonts;
    private readonly IReadOnlyList<string> _allSystemFonts;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _showAllFonts;

    [ObservableProperty]
    private ObservableCollection<string> _filteredFonts = new();

    [ObservableProperty]
    private string? _selectedFont;

    public event EventHandler<string?>? RequestClose;

    public FontPickerViewModel(IReadOnlyList<string> monospaceFonts, IReadOnlyList<string> allSystemFonts, string currentFont)
    {
        _monospaceFonts = monospaceFonts;
        _allSystemFonts = allSystemFonts;

        // Default is false (not opened).
        // If current font is not in monospace list but exists in all system fonts,
        // automatically turn on ShowAllFonts so the current selection is visible.
        bool isCurrentInMonospace = monospaceFonts.Any(f => f.Equals(currentFont, StringComparison.OrdinalIgnoreCase));
        _showAllFonts = !isCurrentInMonospace && allSystemFonts.Any(f => f.Equals(currentFont, StringComparison.OrdinalIgnoreCase));

        _selectedFont = currentFont;

        ApplyFilter();
    }

    public FontPickerViewModel(IEnumerable<string> fonts, string currentFont)
        : this(fonts.ToList(), fonts.ToList(), currentFont)
    {
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnShowAllFontsChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        var baseList = ShowAllFonts ? _allSystemFonts : _monospaceFonts;
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredFonts = new ObservableCollection<string>(baseList);
        }
        else
        {
            var filtered = baseList.Where(f => f.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            FilteredFonts = new ObservableCollection<string>(filtered);
        }

        // Keep selected item valid if possible
        if (!string.IsNullOrEmpty(SelectedFont) && FilteredFonts.Contains(SelectedFont))
        {
            // SelectedFont is in current filtered list
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
