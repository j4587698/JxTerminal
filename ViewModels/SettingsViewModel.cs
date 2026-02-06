using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JxTerminal.Models;
using JxTerminal.Services;
using JxTerminal.Views;
using SukiUI;
using SukiUI.Dialogs;
using SukiUI.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace JxTerminal.ViewModels;

public class LocalizedWrapper<T> : ObservableObject
{
    public T Value { get; }
    public string Key { get; }
    public string DisplayName => LocalizationService.Instance[Key];

    public LocalizedWrapper(T value, string key)
    {
        Value = value;
        Key = key;
        LocalizationService.Instance.PropertyChanged += (_, _) => OnPropertyChanged(nameof(DisplayName));
    }
}

public partial class SettingsViewModel : ViewModelBase
{
    public TerminalSettings Settings { get; }
    private readonly ISukiDialogManager _dialogManager;

    /// <summary>
    /// Guard flag to prevent re-entrant updates when SukiUI theme changes trigger control rebuilds.
    /// </summary>
    private bool _isUpdating;

    public ObservableCollection<LocalizedWrapper<SukiColorTheme>> AvailableColors { get; }
    public ObservableCollection<LocalizedWrapper<ThemeVariant>> AvailableThemes { get; }
    public ObservableCollection<LocalizedWrapper<string>> AvailableLanguages { get; }
    public ObservableCollection<string> AvailableFonts { get; }

    /// <summary>
    /// All available color schemes (built-in presets + user custom schemes).
    /// </summary>
    public ObservableCollection<TerminalColorScheme> AvailableColorSchemes { get; }

    [ObservableProperty]
    private TerminalColorScheme? _selectedColorScheme;

    [ObservableProperty]
    private LocalizedWrapper<SukiColorTheme> _selectedColor;

    [ObservableProperty]
    private LocalizedWrapper<ThemeVariant> _selectedBaseTheme;

    [ObservableProperty]
    private LocalizedWrapper<string> _selectedLanguage;

    [ObservableProperty]
    private string _selectedSection = "General"; // "General" or "Terminal"

    /// <summary>
    /// Whether the current color scheme is editable (user-defined, not built-in).
    /// </summary>
    [ObservableProperty]
    private bool _isSchemeEditable;

    public SettingsViewModel(TerminalSettings settings, ISukiDialogManager dialogManager)
    {
        Settings = settings;
        _dialogManager = dialogManager;
        
        var theme = SukiTheme.GetInstance();
        
        // Wrap Languages
        AvailableLanguages = new ObservableCollection<LocalizedWrapper<string>>
        {
            new LocalizedWrapper<string>("en-US", "English"),
            new LocalizedWrapper<string>("zh-CN", "Chinese")
        };
        
        var currentCulture = LocalizationService.Instance.CultureCode;
        _selectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Value == currentCulture)
                            ?? AvailableLanguages[0];

        // Wrap Colors
        AvailableColors = new ObservableCollection<LocalizedWrapper<SukiColorTheme>>(
            theme.ColorThemes.Select(c => new LocalizedWrapper<SukiColorTheme>(c, c.DisplayName)));
            
        _selectedColor = AvailableColors.FirstOrDefault(c => c.Value.DisplayName == theme.ActiveColorTheme?.DisplayName) 
                         ?? AvailableColors[0];

        // Wrap Themes
        AvailableThemes = new ObservableCollection<LocalizedWrapper<ThemeVariant>>
        {
            new LocalizedWrapper<ThemeVariant>(ThemeVariant.Dark, "Dark"),
            new LocalizedWrapper<ThemeVariant>(ThemeVariant.Light, "Light")
        };
        
        _selectedBaseTheme = AvailableThemes.FirstOrDefault(t => t.Value == theme.ActiveBaseTheme) 
                             ?? AvailableThemes[0];

        // Load System Fonts
        AvailableFonts = new ObservableCollection<string>(FontManager.Current.SystemFonts.OrderBy(f => f.Name).Select(f => f.Name));
        
        // Ensure current font is in the list (or add it if it's a fallback/custom string)
        if (!AvailableFonts.Contains(Settings.FontFamily))
        {
             AvailableFonts.Insert(0, Settings.FontFamily);
        }

        // Color Schemes
        AvailableColorSchemes = new ObservableCollection<TerminalColorScheme>(
            TerminalColorSchemes.GetAllPresets());

        // If current settings have a custom (non-built-in) scheme, add it to the list
        if (Settings.ColorScheme != null && !Settings.ColorScheme.IsBuiltIn)
        {
            AvailableColorSchemes.Add(Settings.ColorScheme);
        }

        // Select the current scheme
        _selectedColorScheme = AvailableColorSchemes.FirstOrDefault(s => s.Name == Settings.ColorSchemeName)
                               ?? AvailableColorSchemes[0];
        _isSchemeEditable = _selectedColorScheme != null && !_selectedColorScheme.IsBuiltIn;

        System.Diagnostics.Debug.WriteLine($"[SettingsViewModel] Settings HashCode: {Settings.GetHashCode()}");
    }

    partial void OnSelectedColorSchemeChanged(TerminalColorScheme? value)
    {
        if (_isUpdating || value == null) return;
        
        Settings.ApplyScheme(value);
        IsSchemeEditable = !value.IsBuiltIn;
    }

    partial void OnSelectedLanguageChanged(LocalizedWrapper<string> value)
    {
        if (_isUpdating || value == null) return;

        _isUpdating = true;
        try
        {
            LocalizationService.Instance.SetLanguage(value.Value);
        }
        finally
        {
            _isUpdating = false;
        }
    }

    partial void OnSelectedColorChanged(LocalizedWrapper<SukiColorTheme> value)
    {
        if (_isUpdating || value == null) return;

        _isUpdating = true;
        try
        {
            SukiTheme.GetInstance().ChangeColorTheme(value.Value);
        }
        finally
        {
            _isUpdating = false;
        }
    }

    partial void OnSelectedBaseThemeChanged(LocalizedWrapper<ThemeVariant> value)
    {
        if (_isUpdating || value == null) return;

        _isUpdating = true;
        try
        {
            var theme = SukiTheme.GetInstance();
            var currentColor = theme.ActiveColorTheme;
            theme.ChangeBaseTheme(value.Value);
            // SukiUI's ChangeBaseTheme resets ActiveColorTheme to the default ThemeColor (Blue).
            // Re-apply the user's selected color theme to preserve it.
            if (currentColor != null)
                theme.ChangeColorTheme(currentColor);
        }
        finally
        {
            _isUpdating = false;
        }
    }

    /// <summary>
    /// Duplicate the currently selected scheme as a custom editable scheme.
    /// </summary>
    [RelayCommand]
    private void DuplicateScheme()
    {
        if (SelectedColorScheme == null) return;

        var clone = (TerminalColorScheme)SelectedColorScheme.Clone();
        clone.Name = SelectedColorScheme.Name + " (Custom)";
        clone.IsBuiltIn = false;

        // Ensure unique name
        int suffix = 1;
        string baseName = clone.Name;
        while (AvailableColorSchemes.Any(s => s.Name == clone.Name))
        {
            clone.Name = $"{baseName} {suffix++}";
        }

        AvailableColorSchemes.Add(clone);
        SelectedColorScheme = clone;
    }

    /// <summary>
    /// Delete the currently selected custom scheme (only non-built-in).
    /// </summary>
    [RelayCommand]
    private void DeleteScheme()
    {
        if (SelectedColorScheme == null || SelectedColorScheme.IsBuiltIn) return;

        var toRemove = SelectedColorScheme;
        SelectedColorScheme = AvailableColorSchemes.FirstOrDefault(s => s.IsBuiltIn) ?? AvailableColorSchemes[0];
        AvailableColorSchemes.Remove(toRemove);
    }

    [RelayCommand]
    private void PickFont()
    {
        var vm = new FontPickerViewModel(AvailableFonts, Settings.FontFamily);
        var view = new FontPickerView() { DataContext = vm };

        ISukiDialog? dialog = null;

        void OnClose(object? sender, string? selectedFont)
        {
             vm.RequestClose -= OnClose;
             
             if (selectedFont != null)
             {
                 if (string.IsNullOrWhiteSpace(selectedFont))
                 {
                     Settings.FontFamily = "DejaVu Sans Mono, Liberation Mono, Consolas, monospace";
                 }
                 else
                 {
                     Settings.FontFamily = selectedFont;
                 }
             }
             
             if (dialog != null) _dialogManager.TryDismissDialog(dialog);
        }
        vm.RequestClose += OnClose;

        var builder = _dialogManager.CreateDialog();
        builder.SetContent(view);
        builder.SetCanDismissWithBackgroundClick(true);
        builder.SetOnDismissed(_ => {
             vm.RequestClose -= OnClose;
        });

        builder.TryShow();
        dialog = builder.Dialog;
    }
}
