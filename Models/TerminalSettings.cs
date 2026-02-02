using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;
using System.Text.Json.Serialization;

namespace JxTerminal.Models;

public partial class TerminalSettings : ObservableObject
{
    [ObservableProperty]
    [property: JsonIgnore]
    private Color _backgroundColor = Color.FromRgb(30, 30, 30);

    public string BackgroundColorHex
    {
        get => BackgroundColor.ToString();
        set
        {
            if (Color.TryParse(value, out var color))
            {
                BackgroundColor = color;
            }
        }
    }

    [ObservableProperty]
    [property: JsonIgnore]
    private Color _foregroundColor = Color.FromRgb(229, 229, 229);

    public string ForegroundColorHex
    {
        get => ForegroundColor.ToString();
        set
        {
            if (Color.TryParse(value, out var color))
            {
                ForegroundColor = color;
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
}
