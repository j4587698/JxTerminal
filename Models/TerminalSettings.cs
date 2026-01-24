using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;

namespace JxTerminal.Models;

public partial class TerminalSettings : ObservableObject
{
    [ObservableProperty]
    private Color _backgroundColor = Color.FromRgb(0, 0, 0);

    [ObservableProperty]
    private Color _foregroundColor = Color.FromRgb(229, 229, 229);

    [ObservableProperty]
    private string _fontFamily = "DejaVu Sans Mono, Liberation Mono, Consolas, monospace";

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
    private IImage? _backgroundImage;

    partial void OnBackgroundImagePathChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !File.Exists(value))
        {
            BackgroundImage = null;
            return;
        }

        try
        {
            BackgroundImage = new Bitmap(value);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading background image: {ex.Message}");
            BackgroundImage = null;
        }
    }
}
