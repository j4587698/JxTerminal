using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using JxTerminal.Models;
using JxTerminal.Services;
using SukiUI;
using SukiUI.Models;
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

    public ObservableCollection<LocalizedWrapper<SukiColorTheme>> AvailableColors { get; }
    public ObservableCollection<LocalizedWrapper<ThemeVariant>> AvailableThemes { get; }

    [ObservableProperty]
    private LocalizedWrapper<SukiColorTheme> _selectedColor;

    [ObservableProperty]
    private LocalizedWrapper<ThemeVariant> _selectedBaseTheme;

    public SettingsViewModel(TerminalSettings settings)
    {
        Settings = settings;
        
        var theme = SukiTheme.GetInstance();
        
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
    }

    partial void OnSelectedColorChanged(LocalizedWrapper<SukiColorTheme> value)
    {
        if (value != null)
            SukiTheme.GetInstance().ChangeColorTheme(value.Value);
    }

    partial void OnSelectedBaseThemeChanged(LocalizedWrapper<ThemeVariant> value)
    {
        if (value != null)
        {
            // Application.Current!.RequestedThemeVariant = value.Value;
            SukiTheme.GetInstance().ChangeBaseTheme(value.Value);
        }
    }
}

