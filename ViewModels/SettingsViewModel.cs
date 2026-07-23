using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JxTerminal.Models;
using JxTerminal.Services;
using JxTerminal.Views;
using LuminaUI.Theming;
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

public class AccentColorOption : ObservableObject
{
    public string Key { get; }
    public string Hex { get; }
    public Color Color { get; }
    public string DisplayName => LocalizationService.Instance[Key];

    public AccentColorOption(string key, string hex)
    {
        Key = key;
        Hex = hex;
        Color = Color.Parse(hex);
        LocalizationService.Instance.PropertyChanged += (_, _) => OnPropertyChanged(nameof(DisplayName));
    }
}

public partial class SettingsViewModel : ViewModelBase
{
    public TerminalSettings Settings { get; }
    private readonly MainWindowViewModel _mainViewModel;

    /// <summary>
    /// Guard flag to prevent re-entrant updates when theme changes trigger control rebuilds.
    /// </summary>
    private bool _isUpdating;

    public ObservableCollection<AccentColorOption> AvailableColors { get; }
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
    private AccentColorOption _selectedColor;

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

    public SettingsViewModel(TerminalSettings settings, MainWindowViewModel mainViewModel)
    {
        Settings = settings;
        _mainViewModel = mainViewModel;
        
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
        AvailableColors = new ObservableCollection<AccentColorOption>
        {
            new("Blue", "#2563EB"),
            new("Green", "#16A34A"),
            new("Orange", "#F97316"),
            new("Red", "#E11D48"),
            new("Purple", "#7C3AED"),
            new("Cyan", "#0891B2")
        };

        var currentAccent = LuminaThemeManager.ToHex(LuminaThemeManager.CurrentAccentColor);
        _selectedColor = AvailableColors.FirstOrDefault(c => c.Hex.Equals(currentAccent, System.StringComparison.OrdinalIgnoreCase))
                         ?? AvailableColors[0];

        // Wrap Themes
        AvailableThemes = new ObservableCollection<LocalizedWrapper<ThemeVariant>>
        {
            new LocalizedWrapper<ThemeVariant>(ThemeVariant.Dark, "Dark"),
            new LocalizedWrapper<ThemeVariant>(ThemeVariant.Light, "Light")
        };
        
        _selectedBaseTheme = AvailableThemes.FirstOrDefault(t => t.Value == Application.Current?.RequestedThemeVariant)
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

    partial void OnSelectedColorChanged(AccentColorOption value)
    {
        if (_isUpdating || value == null) return;

        _isUpdating = true;
        try
        {
            LuminaThemeManager.TrySetAccentColor(value.Hex);
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
            Application.Current!.RequestedThemeVariant = value.Value;
            LuminaThemeManager.SetThemeMode(value.Value == ThemeVariant.Light
                ? LuminaThemeMode.Light
                : LuminaThemeMode.Dark);
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
        view.Margin = new Thickness(24);

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
             
             _mainViewModel.CloseDialog();
        }
        vm.RequestClose += OnClose;

        _mainViewModel.ShowDialog(view);
    }
}
