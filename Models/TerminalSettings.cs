using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;
using System.Text.Json.Serialization;

namespace JxTerminal.Models;

public partial class TerminalSettings : ObservableObject
{
    /// <summary>
    /// The active color scheme. Contains all terminal colors including foreground, background, ANSI 16, selection, etc.
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private TerminalColorScheme _colorScheme = (TerminalColorScheme)TerminalColorSchemes.Default.Clone();

    /// <summary>
    /// Name of the active color scheme for serialization. 
    /// If it matches a built-in preset name, the preset will be loaded on deserialization.
    /// </summary>
    [ObservableProperty]
    private string _colorSchemeName = "Default";

    /// <summary>
    /// Custom color scheme data for serialization. Only serialized when using a non-built-in scheme.
    /// </summary>
    public TerminalColorScheme? CustomColorScheme
    {
        get => ColorScheme.IsBuiltIn ? null : ColorScheme;
        set
        {
            if (value != null)
            {
                ColorScheme = value;
            }
        }
    }

    partial void OnColorSchemeChanged(TerminalColorScheme? oldValue, TerminalColorScheme newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnColorSchemePropertyChanged;
        }
        if (newValue != null)
        {
            newValue.PropertyChanged += OnColorSchemePropertyChanged;
            ColorSchemeName = newValue.Name;
        }
        // Notify that derived properties have changed
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(ForegroundColor));
    }

    private void OnColorSchemePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // When any color in the scheme changes, propagate notifications
        if (e.PropertyName == nameof(TerminalColorScheme.Background))
            OnPropertyChanged(nameof(BackgroundColor));
        else if (e.PropertyName == nameof(TerminalColorScheme.Foreground))
            OnPropertyChanged(nameof(ForegroundColor));
        
        // Trigger a general change notification for auto-save
        OnPropertyChanged(nameof(ColorScheme));
    }

    /// <summary>
    /// Convenience property: gets the background color from the active color scheme.
    /// </summary>
    [JsonIgnore]
    public Color BackgroundColor => ColorScheme.Background;

    /// <summary>
    /// Convenience property: gets the foreground color from the active color scheme.
    /// </summary>
    [JsonIgnore]
    public Color ForegroundColor => ColorScheme.Foreground;

    // Legacy hex properties kept for backward compatibility during deserialization
    public string? BackgroundColorHex
    {
        get => null; // No longer serialize individually
        set
        {
            // Migration: if old settings had individual color hex, apply to scheme
            if (value != null && Color.TryParse(value, out var color))
            {
                ColorScheme.Background = color;
            }
        }
    }

    public string? ForegroundColorHex
    {
        get => null;
        set
        {
            if (value != null && Color.TryParse(value, out var color))
            {
                ColorScheme.Foreground = color;
            }
        }
    }

    private const string DefaultFontFamily = "DejaVu Sans Mono, Liberation Mono, Consolas, monospace";

    [ObservableProperty]
    private string _fontFamily = DefaultFontFamily;

    partial void OnFontFamilyChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) && value != DefaultFontFamily)
        {
            FontFamily = DefaultFontFamily;
        }
    }

    [ObservableProperty]
    private double _fontSize = 14;

    [ObservableProperty]
    private bool _enableColors = true;

    [ObservableProperty]
    private int _scrollbackLines = 1000;

    [ObservableProperty]
    private string? _backgroundImagePath;

    [ObservableProperty]
    private double _backgroundImageOpacity = 0.3;

    [ObservableProperty]
    [property: JsonIgnore]
    private IImage? _backgroundImage;

    partial void OnBackgroundImagePathChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            BackgroundImage = null;
            return;
        }

        var fullPath = Services.PathConfig.GetFullPath(value);

        if (!File.Exists(fullPath))
        {
            BackgroundImage = null;
            return;
        }

        try
        {
            BackgroundImage = new Bitmap(fullPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading background image: {ex.Message}");
            BackgroundImage = null;
        }
    }

    /// <summary>
    /// Apply a color scheme by name. If it matches a built-in preset, use that; otherwise keep current custom scheme.
    /// </summary>
    public void ApplyScheme(TerminalColorScheme scheme)
    {
        if (scheme.IsBuiltIn)
        {
            ColorScheme = (TerminalColorScheme)scheme.Clone();
            ColorScheme.IsBuiltIn = true;
        }
        else
        {
            ColorScheme = scheme;
        }
    }

    /// <summary>
    /// Resolve color scheme after deserialization. 
    /// Called after loading settings from JSON to link scheme name to actual preset.
    /// </summary>
    public void ResolveColorScheme()
    {
        // If CustomColorScheme was deserialized, it's already set
        if (ColorScheme != null && !string.IsNullOrEmpty(ColorScheme.Name) && ColorScheme.Name != "Default")
        {
            // Already resolved from CustomColorScheme setter
            return;
        }

        // Try to find a built-in preset matching the name
        var preset = TerminalColorSchemes.GetAllPresets()
            .Find(p => p.Name == ColorSchemeName);
        if (preset != null)
        {
            ColorScheme = (TerminalColorScheme)preset.Clone();
            ColorScheme.IsBuiltIn = true;
        }
    }
}
