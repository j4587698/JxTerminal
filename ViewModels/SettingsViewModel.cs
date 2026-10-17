using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JxTerminal.Models;
using JxTerminal.Services;
using JxTerminal.Services.Terminal;
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
    public ObservableCollection<LocalizedWrapper<FolderTransferMode>> AvailableFolderTransferModes { get; }

    [ObservableProperty]
    private LocalizedWrapper<FolderTransferMode> _selectedFolderTransferMode;

    partial void OnSelectedFolderTransferModeChanged(LocalizedWrapper<FolderTransferMode> value)
    {
        if (value != null && Settings.FolderTransferMode != value.Value)
        {
            Settings.FolderTransferMode = value.Value;
        }
    }

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
    private int _selectedTabIndex;

    public bool IsTabGeneral => SelectedTabIndex == 0;
    public bool IsTabTerminal => SelectedTabIndex == 1;
    public bool IsTabAi => SelectedTabIndex == 2;
    public bool IsTabColorScheme => SelectedTabIndex == 3;
    public bool IsTabAbout => SelectedTabIndex == 4;
    public bool IsTabSync => SelectedTabIndex == 5;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTabGeneral));
        OnPropertyChanged(nameof(IsTabTerminal));
        OnPropertyChanged(nameof(IsTabAi));
        OnPropertyChanged(nameof(IsTabColorScheme));
        OnPropertyChanged(nameof(IsTabAbout));
        OnPropertyChanged(nameof(IsTabSync));
    }

    public SyncViewModel Sync { get; }

    public string AppDataDirectory => PathConfig.AppDataRoot;

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            var dir = PathConfig.AppDataRoot;
            if (System.IO.Directory.Exists(dir))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
        }
        catch (System.Exception ex)
        {
            AppLog.Error($"Failed to open data folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SetAiPreset(string preset)
    {
        switch (preset?.ToLowerInvariant())
        {
            case "openai":
                Settings.AiEndpoint = "https://api.openai.com/v1";
                Settings.AiModel = "gpt-4o-mini";
                break;
            case "deepseek":
                Settings.AiEndpoint = "https://api.deepseek.com/v1";
                Settings.AiModel = "deepseek-chat";
                break;
            case "ollama":
                Settings.AiEndpoint = "http://localhost:11434/v1";
                Settings.AiModel = "qwen2.5-coder";
                break;
            case "qwen":
                Settings.AiEndpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1";
                Settings.AiModel = "qwen-plus";
                break;
        }
    }

    [RelayCommand]
    private void SelectColorOption(AccentColorOption opt)
    {
        if (opt != null) SelectedColor = opt;
    }

    /// <summary>
    /// Whether the current color scheme is editable (user-defined, not built-in).
    /// </summary>
    [ObservableProperty]
    private bool _isSchemeEditable;

    public SettingsViewModel(TerminalSettings settings, MainWindowViewModel mainViewModel)
    {
        Settings = settings;
        _mainViewModel = mainViewModel;
        Sync = new SyncViewModel(mainViewModel);
        
        // Wrap Languages
        AvailableLanguages = new ObservableCollection<LocalizedWrapper<string>>
        {
            new LocalizedWrapper<string>("en-US", "English"),
            new LocalizedWrapper<string>("zh-CN", "Chinese")
        };
        
        var currentCulture = string.IsNullOrWhiteSpace(Settings.Language) ? LocalizationService.Instance.CultureCode : Settings.Language;
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

        var currentAccent = string.IsNullOrWhiteSpace(Settings.AccentColor) 
            ? LuminaThemeManager.ToHex(LuminaThemeManager.CurrentAccentColor) 
            : Settings.AccentColor;
        _selectedColor = AvailableColors.FirstOrDefault(c => c.Hex.Equals(currentAccent, System.StringComparison.OrdinalIgnoreCase))
                         ?? AvailableColors[0];

        // Wrap Themes
        AvailableThemes = new ObservableCollection<LocalizedWrapper<ThemeVariant>>
        {
            new LocalizedWrapper<ThemeVariant>(ThemeVariant.Dark, "Dark"),
            new LocalizedWrapper<ThemeVariant>(ThemeVariant.Light, "Light")
        };
        
        var themeName = string.IsNullOrWhiteSpace(Settings.ThemeMode) ? "Dark" : Settings.ThemeMode;
        _selectedBaseTheme = AvailableThemes.FirstOrDefault(t => t.Value.ToString().Equals(themeName, System.StringComparison.OrdinalIgnoreCase))
                             ?? AvailableThemes[0];

        // Load System Fonts (monospaced by default)
        AvailableFonts = new ObservableCollection<string>(FontHelper.GetMonospaceFonts());
        
        // Ensure current font is in the list
        if (!AvailableFonts.Contains(Settings.FontFamily) && !string.IsNullOrWhiteSpace(Settings.FontFamily))
        {
            AvailableFonts.Insert(0, Settings.FontFamily);
        }

        // Color Schemes: load built-in presets
        AvailableColorSchemes = new ObservableCollection<TerminalColorScheme>(
            TerminalColorSchemes.GetAllPresets());

        // Load all saved custom schemes from Settings.CustomColorSchemes
        foreach (var cs in Settings.CustomColorSchemes)
        {
            cs.IsBuiltIn = false;
            if (!AvailableColorSchemes.Any(s => s.Name == cs.Name))
            {
                AvailableColorSchemes.Add(cs);
            }
        }

        // Backward compatibility: If active scheme is custom and not yet in list
        if (Settings.ColorScheme != null && !Settings.ColorScheme.IsBuiltIn)
        {
            if (!AvailableColorSchemes.Any(s => s.Name == Settings.ColorScheme.Name))
            {
                AvailableColorSchemes.Add(Settings.ColorScheme);
            }
            if (!Settings.CustomColorSchemes.Any(s => s.Name == Settings.ColorScheme.Name))
            {
                Settings.CustomColorSchemes.Add(Settings.ColorScheme);
            }
        }

        // Select the current scheme
        _selectedColorScheme = AvailableColorSchemes.FirstOrDefault(s => s.Name == Settings.ColorSchemeName)
                               ?? AvailableColorSchemes[0];
        _isSchemeEditable = _selectedColorScheme != null && !_selectedColorScheme.IsBuiltIn;

        // Session options
        AvailableLocalShells = new ObservableCollection<string> { "Auto", "PowerShell", "Cmd", "Wsl", "Bash" };
        _selectedLocalShell = AvailableLocalShells.Contains(Settings.LocalShellKind)
            ? Settings.LocalShellKind
            : AvailableLocalShells[0];

        AvailableEncodings = new ObservableCollection<string> { "UTF-8", "GBK", "GB18030", "Big5", "Shift-JIS", "EUC-KR", "Latin-1" };
        var enc = string.IsNullOrWhiteSpace(Settings.RemoteEncoding) ? "UTF-8" : Settings.RemoteEncoding;
        if (!AvailableEncodings.Contains(enc)) AvailableEncodings.Add(enc);
        _selectedEncoding = enc;

        // Cursor shape (localized display, stable string value)
        AvailableCursorStyles = new ObservableCollection<LocalizedWrapper<string>>
        {
            new("Block", "CursorBlock"),
            new("Bar", "CursorBar"),
            new("Underline", "CursorUnderline")
        };
        _selectedCursorStyle = AvailableCursorStyles.FirstOrDefault(c => c.Value == Settings.CursorStyle)
                               ?? AvailableCursorStyles[0];

        // Folder Transfer Modes
        AvailableFolderTransferModes = new ObservableCollection<LocalizedWrapper<FolderTransferMode>>
        {
            new(FolderTransferMode.TarGz, "FolderTransferTar"),
            new(FolderTransferMode.Zip, "FolderTransferZip"),
            new(FolderTransferMode.Standard, "FolderTransferStandard")
        };
        _selectedFolderTransferMode = AvailableFolderTransferModes.FirstOrDefault(m => m.Value == Settings.FolderTransferMode)
            ?? AvailableFolderTransferModes[0];
    }

    public ObservableCollection<string> AvailableLocalShells { get; }
    public ObservableCollection<string> AvailableEncodings { get; }
    public ObservableCollection<LocalizedWrapper<string>> AvailableCursorStyles { get; }

    [ObservableProperty]
    private string _selectedLocalShell;

    [ObservableProperty]
    private string _selectedEncoding;

    [ObservableProperty]
    private LocalizedWrapper<string> _selectedCursorStyle;

    partial void OnSelectedLocalShellChanged(string value)
    {
        if (_isUpdating || value == null) return;
        Settings.LocalShellKind = value;
    }

    partial void OnSelectedEncodingChanged(string value)
    {
        if (_isUpdating || value == null) return;
        Settings.RemoteEncoding = value;
    }

    partial void OnSelectedCursorStyleChanged(LocalizedWrapper<string> value)
    {
        if (_isUpdating || value == null) return;
        Settings.CursorStyle = value.Value;
    }

    partial void OnSelectedColorSchemeChanged(TerminalColorScheme? value)
    {
        if (_isUpdating || value == null) return;
        
        Settings.ApplyScheme(value);
        IsSchemeEditable = !value.IsBuiltIn;
        _ = DataStore.SaveSettingsAsync(Settings);
    }

    partial void OnSelectedLanguageChanged(LocalizedWrapper<string> value)
    {
        if (_isUpdating || value == null) return;

        _isUpdating = true;
        try
        {
            Settings.Language = value.Value;
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
            Settings.AccentColor = value.Hex;
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
            Settings.ThemeMode = value.Value.ToString();
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
        if (!Settings.CustomColorSchemes.Any(s => s.Name == clone.Name))
        {
            Settings.CustomColorSchemes.Add(clone);
        }

        SelectedColorScheme = clone;
        _ = DataStore.SaveSettingsAsync(Settings);
    }

    /// <summary>
    /// Delete the currently selected custom scheme (only non-built-in).
    /// </summary>
    [RelayCommand]
    private void DeleteScheme()
    {
        if (SelectedColorScheme == null || SelectedColorScheme.IsBuiltIn) return;

        var toRemove = SelectedColorScheme;
        Settings.CustomColorSchemes.RemoveAll(s => s.Name == toRemove.Name);
        AvailableColorSchemes.Remove(toRemove);

        SelectedColorScheme = AvailableColorSchemes.FirstOrDefault(s => s.IsBuiltIn) ?? AvailableColorSchemes[0];
        _ = DataStore.SaveSettingsAsync(Settings);
    }

    [RelayCommand]
    private void PickFont()
    {
        var vm = new FontPickerViewModel(FontHelper.GetMonospaceFonts(), FontHelper.GetAllFonts(), Settings.FontFamily);
        var view = new FontPickerView() { DataContext = vm };

        void OnClose(object? sender, string? selectedFont)
        {
            vm.RequestClose -= OnClose;

            if (selectedFont != null)
            {
                var newFont = string.IsNullOrWhiteSpace(selectedFont)
                    ? TerminalSettings.DefaultFontFamily
                    : selectedFont;

                if (!AvailableFonts.Contains(newFont))
                {
                    AvailableFonts.Insert(0, newFont);
                }

                Settings.FontFamily = newFont;
            }

            _mainViewModel.CloseDialog();
        }
        vm.RequestClose += OnClose;

        _mainViewModel.ShowDialog(view, Services.LocalizationService.Instance["SelectFont"]);
    }
}
