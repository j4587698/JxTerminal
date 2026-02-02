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

    public ObservableCollection<LocalizedWrapper<SukiColorTheme>> AvailableColors { get; }
    public ObservableCollection<LocalizedWrapper<ThemeVariant>> AvailableThemes { get; }
    public ObservableCollection<string> AvailableFonts { get; }

    [ObservableProperty]
    private LocalizedWrapper<SukiColorTheme> _selectedColor;

    [ObservableProperty]
    private LocalizedWrapper<ThemeVariant> _selectedBaseTheme;

    [ObservableProperty]
    private string _selectedSection = "General"; // "General" or "Terminal"

    public SettingsViewModel(TerminalSettings settings, ISukiDialogManager dialogManager)
    {
        Settings = settings;
        _dialogManager = dialogManager;
        
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

        // Load System Fonts
        AvailableFonts = new ObservableCollection<string>(FontManager.Current.SystemFonts.OrderBy(f => f.Name).Select(f => f.Name));
        
        // Ensure current font is in the list (or add it if it's a fallback/custom string)
        if (!AvailableFonts.Contains(Settings.FontFamily))
        {
             // If the current setting is a list (e.g. "Consolas, Monospace"), we might just want to keep it as is, 
             // but ComboBox selection might fail. 
             // Ideally we pick the first match or add it. 
             // For now, let's add it so it shows up.
             AvailableFonts.Insert(0, Settings.FontFamily);
        }

        System.Diagnostics.Debug.WriteLine($"[SettingsViewModel] Settings HashCode: {Settings.GetHashCode()}");
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

    [RelayCommand]
    private void PickFont()
    {
        var vm = new FontPickerViewModel(AvailableFonts, Settings.FontFamily);
        var view = new FontPickerView() { DataContext = vm };

        ISukiDialog? dialog = null;

        void OnClose(object? sender, string? selectedFont)
        {
             vm.RequestClose -= OnClose;
             
             // If user clicked confirm (indicated by the fact that we are in OnClose via event)
             // but selectedFont is null/empty, we can either keep current or reset.
             // The user specifically asked to go back to default if nothing is selected.
             if (selectedFont != null) // selectedFont is null on Cancel, but empty string could mean "nothing selected"
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

