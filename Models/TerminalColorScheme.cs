using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace JxTerminal.Models;

/// <summary>
/// Represents a terminal color scheme with ANSI 16 colors, foreground, background, cursor, and selection colors.
/// </summary>
public partial class TerminalColorScheme : ObservableObject, ICloneable
{
    private string _name = "Custom";
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>
    /// Whether this is a built-in preset (read-only) or user-defined.
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isBuiltIn;

    // Foreground & Background
    [ObservableProperty]
    [property: JsonIgnore]
    private Color _foreground = Color.FromRgb(229, 229, 229);

    [ObservableProperty]
    [property: JsonIgnore]
    private Color _background = Color.FromRgb(30, 30, 30);

    // Selection highlight color (with alpha)
    [ObservableProperty]
    [property: JsonIgnore]
    private Color _selectionBackground = Color.FromArgb(77, 255, 255, 255);

    // ANSI 16 colors (0-7 normal, 8-15 bright)
    [ObservableProperty] [property: JsonIgnore] private Color _black = Color.FromRgb(0, 0, 0);
    [ObservableProperty] [property: JsonIgnore] private Color _red = Color.FromRgb(205, 0, 0);
    [ObservableProperty] [property: JsonIgnore] private Color _green = Color.FromRgb(0, 205, 0);
    [ObservableProperty] [property: JsonIgnore] private Color _yellow = Color.FromRgb(205, 205, 0);
    [ObservableProperty] [property: JsonIgnore] private Color _blue = Color.FromRgb(0, 0, 238);
    [ObservableProperty] [property: JsonIgnore] private Color _magenta = Color.FromRgb(205, 0, 205);
    [ObservableProperty] [property: JsonIgnore] private Color _cyan = Color.FromRgb(0, 205, 205);
    [ObservableProperty] [property: JsonIgnore] private Color _white = Color.FromRgb(229, 229, 229);
    
    [ObservableProperty] [property: JsonIgnore] private Color _brightBlack = Color.FromRgb(127, 127, 127);
    [ObservableProperty] [property: JsonIgnore] private Color _brightRed = Color.FromRgb(255, 0, 0);
    [ObservableProperty] [property: JsonIgnore] private Color _brightGreen = Color.FromRgb(0, 255, 0);
    [ObservableProperty] [property: JsonIgnore] private Color _brightYellow = Color.FromRgb(255, 255, 0);
    [ObservableProperty] [property: JsonIgnore] private Color _brightBlue = Color.FromRgb(92, 92, 255);
    [ObservableProperty] [property: JsonIgnore] private Color _brightMagenta = Color.FromRgb(255, 0, 255);
    [ObservableProperty] [property: JsonIgnore] private Color _brightCyan = Color.FromRgb(0, 255, 255);
    [ObservableProperty] [property: JsonIgnore] private Color _brightWhite = Color.FromRgb(255, 255, 255);

    // JSON serialization via hex strings
    public string ForegroundHex { get => Foreground.ToString(); set { if (Color.TryParse(value, out var c)) Foreground = c; } }
    public string BackgroundHex { get => Background.ToString(); set { if (Color.TryParse(value, out var c)) Background = c; } }
    public string SelectionBackgroundHex { get => SelectionBackground.ToString(); set { if (Color.TryParse(value, out var c)) SelectionBackground = c; } }
    
    public string BlackHex { get => Black.ToString(); set { if (Color.TryParse(value, out var c)) Black = c; } }
    public string RedHex { get => Red.ToString(); set { if (Color.TryParse(value, out var c)) Red = c; } }
    public string GreenHex { get => Green.ToString(); set { if (Color.TryParse(value, out var c)) Green = c; } }
    public string YellowHex { get => Yellow.ToString(); set { if (Color.TryParse(value, out var c)) Yellow = c; } }
    public string BlueHex { get => Blue.ToString(); set { if (Color.TryParse(value, out var c)) Blue = c; } }
    public string MagentaHex { get => Magenta.ToString(); set { if (Color.TryParse(value, out var c)) Magenta = c; } }
    public string CyanHex { get => Cyan.ToString(); set { if (Color.TryParse(value, out var c)) Cyan = c; } }
    public string WhiteHex { get => White.ToString(); set { if (Color.TryParse(value, out var c)) White = c; } }
    
    public string BrightBlackHex { get => BrightBlack.ToString(); set { if (Color.TryParse(value, out var c)) BrightBlack = c; } }
    public string BrightRedHex { get => BrightRed.ToString(); set { if (Color.TryParse(value, out var c)) BrightRed = c; } }
    public string BrightGreenHex { get => BrightGreen.ToString(); set { if (Color.TryParse(value, out var c)) BrightGreen = c; } }
    public string BrightYellowHex { get => BrightYellow.ToString(); set { if (Color.TryParse(value, out var c)) BrightYellow = c; } }
    public string BrightBlueHex { get => BrightBlue.ToString(); set { if (Color.TryParse(value, out var c)) BrightBlue = c; } }
    public string BrightMagentaHex { get => BrightMagenta.ToString(); set { if (Color.TryParse(value, out var c)) BrightMagenta = c; } }
    public string BrightCyanHex { get => BrightCyan.ToString(); set { if (Color.TryParse(value, out var c)) BrightCyan = c; } }
    public string BrightWhiteHex { get => BrightWhite.ToString(); set { if (Color.TryParse(value, out var c)) BrightWhite = c; } }

    /// <summary>
    /// Builds the ANSI 256-color palette based on the current 16 color settings.
    /// Colors 0-15 come from the scheme, 16-231 are the 6x6x6 color cube, 232-255 are grayscale.
    /// </summary>
    public Color[] BuildAnsiPalette()
    {
        var palette = new Color[256];

        // Standard 16 colors from scheme
        palette[0] = Black;
        palette[1] = Red;
        palette[2] = Green;
        palette[3] = Yellow;
        palette[4] = Blue;
        palette[5] = Magenta;
        palette[6] = Cyan;
        palette[7] = White;
        palette[8] = BrightBlack;
        palette[9] = BrightRed;
        palette[10] = BrightGreen;
        palette[11] = BrightYellow;
        palette[12] = BrightBlue;
        palette[13] = BrightMagenta;
        palette[14] = BrightCyan;
        palette[15] = BrightWhite;

        // 216 color cube (16-231): 6x6x6 cube
        for (int r = 0; r < 6; r++)
        {
            for (int g = 0; g < 6; g++)
            {
                for (int b = 0; b < 6; b++)
                {
                    int index = 16 + (36 * r) + (6 * g) + b;
                    byte rv = (byte)(r == 0 ? 0 : 55 + r * 40);
                    byte gv = (byte)(g == 0 ? 0 : 55 + g * 40);
                    byte bv = (byte)(b == 0 ? 0 : 55 + b * 40);
                    palette[index] = Color.FromRgb(rv, gv, bv);
                }
            }
        }

        // Grayscale (232-255): 24 shades
        for (int i = 0; i < 24; i++)
        {
            byte v = (byte)(8 + i * 10);
            palette[232 + i] = Color.FromRgb(v, v, v);
        }

        return palette;
    }

    /// <summary>
    /// Gets the ANSI 16 color at the specified index (0-15).
    /// </summary>
    public Color GetAnsiColor(int index) => index switch
    {
        0 => Black, 1 => Red, 2 => Green, 3 => Yellow,
        4 => Blue, 5 => Magenta, 6 => Cyan, 7 => White,
        8 => BrightBlack, 9 => BrightRed, 10 => BrightGreen, 11 => BrightYellow,
        12 => BrightBlue, 13 => BrightMagenta, 14 => BrightCyan, 15 => BrightWhite,
        _ => throw new ArgumentOutOfRangeException(nameof(index), "Index must be 0-15")
    };

    /// <summary>
    /// Sets the ANSI 16 color at the specified index (0-15).
    /// </summary>
    public void SetAnsiColor(int index, Color color)
    {
        switch (index)
        {
            case 0: Black = color; break;
            case 1: Red = color; break;
            case 2: Green = color; break;
            case 3: Yellow = color; break;
            case 4: Blue = color; break;
            case 5: Magenta = color; break;
            case 6: Cyan = color; break;
            case 7: White = color; break;
            case 8: BrightBlack = color; break;
            case 9: BrightRed = color; break;
            case 10: BrightGreen = color; break;
            case 11: BrightYellow = color; break;
            case 12: BrightBlue = color; break;
            case 13: BrightMagenta = color; break;
            case 14: BrightCyan = color; break;
            case 15: BrightWhite = color; break;
        }
    }

    /// <summary>
    /// Names for the ANSI 16 colors.
    /// </summary>
    public static readonly string[] AnsiColorNames =
    {
        "Black", "Red", "Green", "Yellow", "Blue", "Magenta", "Cyan", "White",
        "Bright Black", "Bright Red", "Bright Green", "Bright Yellow", 
        "Bright Blue", "Bright Magenta", "Bright Cyan", "Bright White"
    };

    public object Clone()
    {
        return new TerminalColorScheme
        {
            Name = Name,
            IsBuiltIn = false, // clones are always user-defined
            Foreground = Foreground,
            Background = Background,
            SelectionBackground = SelectionBackground,
            Black = Black, Red = Red, Green = Green, Yellow = Yellow,
            Blue = Blue, Magenta = Magenta, Cyan = Cyan, White = White,
            BrightBlack = BrightBlack, BrightRed = BrightRed, BrightGreen = BrightGreen, BrightYellow = BrightYellow,
            BrightBlue = BrightBlue, BrightMagenta = BrightMagenta, BrightCyan = BrightCyan, BrightWhite = BrightWhite,
        };
    }

    /// <summary>
    /// Copy all color values from another scheme into this one.
    /// </summary>
    public void CopyFrom(TerminalColorScheme other)
    {
        Foreground = other.Foreground;
        Background = other.Background;
        SelectionBackground = other.SelectionBackground;
        Black = other.Black; Red = other.Red; Green = other.Green; Yellow = other.Yellow;
        Blue = other.Blue; Magenta = other.Magenta; Cyan = other.Cyan; White = other.White;
        BrightBlack = other.BrightBlack; BrightRed = other.BrightRed; BrightGreen = other.BrightGreen; BrightYellow = other.BrightYellow;
        BrightBlue = other.BrightBlue; BrightMagenta = other.BrightMagenta; BrightCyan = other.BrightCyan; BrightWhite = other.BrightWhite;
    }

    public override string ToString() => Name;
}

/// <summary>
/// Provides built-in terminal color scheme presets.
/// </summary>
public static class TerminalColorSchemes
{
    public static TerminalColorScheme Default => new()
    {
        Name = "Default",
        IsBuiltIn = true,
        Foreground = Color.FromRgb(229, 229, 229),
        Background = Color.FromRgb(30, 30, 30),
        SelectionBackground = Color.FromArgb(77, 255, 255, 255),
        Black = Color.FromRgb(0, 0, 0), Red = Color.FromRgb(205, 0, 0),
        Green = Color.FromRgb(0, 205, 0), Yellow = Color.FromRgb(205, 205, 0),
        Blue = Color.FromRgb(0, 0, 238), Magenta = Color.FromRgb(205, 0, 205),
        Cyan = Color.FromRgb(0, 205, 205), White = Color.FromRgb(229, 229, 229),
        BrightBlack = Color.FromRgb(127, 127, 127), BrightRed = Color.FromRgb(255, 0, 0),
        BrightGreen = Color.FromRgb(0, 255, 0), BrightYellow = Color.FromRgb(255, 255, 0),
        BrightBlue = Color.FromRgb(92, 92, 255), BrightMagenta = Color.FromRgb(255, 0, 255),
        BrightCyan = Color.FromRgb(0, 255, 255), BrightWhite = Color.FromRgb(255, 255, 255),
    };

    public static TerminalColorScheme Dracula => new()
    {
        Name = "Dracula",
        IsBuiltIn = true,
        Foreground = Color.FromRgb(248, 248, 242),
        Background = Color.FromRgb(40, 42, 54),
        SelectionBackground = Color.FromArgb(77, 68, 71, 90),
        Black = Color.FromRgb(33, 34, 44), Red = Color.FromRgb(255, 85, 85),
        Green = Color.FromRgb(80, 250, 123), Yellow = Color.FromRgb(241, 250, 140),
        Blue = Color.FromRgb(98, 114, 164), Magenta = Color.FromRgb(255, 121, 198),
        Cyan = Color.FromRgb(139, 233, 253), White = Color.FromRgb(248, 248, 242),
        BrightBlack = Color.FromRgb(98, 114, 164), BrightRed = Color.FromRgb(255, 110, 103),
        BrightGreen = Color.FromRgb(90, 247, 142), BrightYellow = Color.FromRgb(244, 249, 157),
        BrightBlue = Color.FromRgb(115, 128, 175), BrightMagenta = Color.FromRgb(255, 146, 208),
        BrightCyan = Color.FromRgb(154, 237, 254), BrightWhite = Color.FromRgb(255, 255, 255),
    };

    public static TerminalColorScheme SolarizedDark => new()
    {
        Name = "Solarized Dark",
        IsBuiltIn = true,
        Foreground = Color.FromRgb(131, 148, 150),
        Background = Color.FromRgb(0, 43, 54),
        SelectionBackground = Color.FromArgb(77, 7, 54, 66),
        Black = Color.FromRgb(7, 54, 66), Red = Color.FromRgb(220, 50, 47),
        Green = Color.FromRgb(133, 153, 0), Yellow = Color.FromRgb(181, 137, 0),
        Blue = Color.FromRgb(38, 139, 210), Magenta = Color.FromRgb(211, 54, 130),
        Cyan = Color.FromRgb(42, 161, 152), White = Color.FromRgb(238, 232, 213),
        BrightBlack = Color.FromRgb(0, 43, 54), BrightRed = Color.FromRgb(203, 75, 22),
        BrightGreen = Color.FromRgb(88, 110, 117), BrightYellow = Color.FromRgb(101, 123, 131),
        BrightBlue = Color.FromRgb(131, 148, 150), BrightMagenta = Color.FromRgb(108, 113, 196),
        BrightCyan = Color.FromRgb(147, 161, 161), BrightWhite = Color.FromRgb(253, 246, 227),
    };

    public static TerminalColorScheme SolarizedLight => new()
    {
        Name = "Solarized Light",
        IsBuiltIn = true,
        Foreground = Color.FromRgb(101, 123, 131),
        Background = Color.FromRgb(253, 246, 227),
        SelectionBackground = Color.FromArgb(77, 238, 232, 213),
        Black = Color.FromRgb(7, 54, 66), Red = Color.FromRgb(220, 50, 47),
        Green = Color.FromRgb(133, 153, 0), Yellow = Color.FromRgb(181, 137, 0),
        Blue = Color.FromRgb(38, 139, 210), Magenta = Color.FromRgb(211, 54, 130),
        Cyan = Color.FromRgb(42, 161, 152), White = Color.FromRgb(238, 232, 213),
        BrightBlack = Color.FromRgb(0, 43, 54), BrightRed = Color.FromRgb(203, 75, 22),
        BrightGreen = Color.FromRgb(88, 110, 117), BrightYellow = Color.FromRgb(101, 123, 131),
        BrightBlue = Color.FromRgb(131, 148, 150), BrightMagenta = Color.FromRgb(108, 113, 196),
        BrightCyan = Color.FromRgb(147, 161, 161), BrightWhite = Color.FromRgb(253, 246, 227),
    };

    public static TerminalColorScheme Monokai => new()
    {
        Name = "Monokai",
        IsBuiltIn = true,
        Foreground = Color.FromRgb(248, 248, 242),
        Background = Color.FromRgb(39, 40, 34),
        SelectionBackground = Color.FromArgb(77, 73, 72, 62),
        Black = Color.FromRgb(39, 40, 34), Red = Color.FromRgb(249, 38, 114),
        Green = Color.FromRgb(166, 226, 46), Yellow = Color.FromRgb(244, 191, 117),
        Blue = Color.FromRgb(102, 217, 239), Magenta = Color.FromRgb(174, 129, 255),
        Cyan = Color.FromRgb(161, 239, 228), White = Color.FromRgb(248, 248, 242),
        BrightBlack = Color.FromRgb(117, 113, 94), BrightRed = Color.FromRgb(249, 38, 114),
        BrightGreen = Color.FromRgb(166, 226, 46), BrightYellow = Color.FromRgb(244, 191, 117),
        BrightBlue = Color.FromRgb(102, 217, 239), BrightMagenta = Color.FromRgb(174, 129, 255),
        BrightCyan = Color.FromRgb(161, 239, 228), BrightWhite = Color.FromRgb(249, 248, 245),
    };

    public static TerminalColorScheme OneDark => new()
    {
        Name = "One Dark",
        IsBuiltIn = true,
        Foreground = Color.FromRgb(171, 178, 191),
        Background = Color.FromRgb(40, 44, 52),
        SelectionBackground = Color.FromArgb(77, 62, 68, 81),
        Black = Color.FromRgb(40, 44, 52), Red = Color.FromRgb(224, 108, 117),
        Green = Color.FromRgb(152, 195, 121), Yellow = Color.FromRgb(229, 192, 123),
        Blue = Color.FromRgb(97, 175, 239), Magenta = Color.FromRgb(198, 120, 221),
        Cyan = Color.FromRgb(86, 182, 194), White = Color.FromRgb(171, 178, 191),
        BrightBlack = Color.FromRgb(92, 99, 112), BrightRed = Color.FromRgb(190, 80, 70),
        BrightGreen = Color.FromRgb(152, 195, 121), BrightYellow = Color.FromRgb(209, 154, 102),
        BrightBlue = Color.FromRgb(97, 175, 239), BrightMagenta = Color.FromRgb(198, 120, 221),
        BrightCyan = Color.FromRgb(86, 182, 194), BrightWhite = Color.FromRgb(255, 255, 255),
    };

    public static TerminalColorScheme Nord => new()
    {
        Name = "Nord",
        IsBuiltIn = true,
        Foreground = Color.FromRgb(216, 222, 233),
        Background = Color.FromRgb(46, 52, 64),
        SelectionBackground = Color.FromArgb(77, 67, 76, 94),
        Black = Color.FromRgb(59, 66, 82), Red = Color.FromRgb(191, 97, 106),
        Green = Color.FromRgb(163, 190, 140), Yellow = Color.FromRgb(235, 203, 139),
        Blue = Color.FromRgb(129, 161, 193), Magenta = Color.FromRgb(180, 142, 173),
        Cyan = Color.FromRgb(136, 192, 208), White = Color.FromRgb(229, 233, 240),
        BrightBlack = Color.FromRgb(76, 86, 106), BrightRed = Color.FromRgb(191, 97, 106),
        BrightGreen = Color.FromRgb(163, 190, 140), BrightYellow = Color.FromRgb(235, 203, 139),
        BrightBlue = Color.FromRgb(129, 161, 193), BrightMagenta = Color.FromRgb(180, 142, 173),
        BrightCyan = Color.FromRgb(143, 188, 187), BrightWhite = Color.FromRgb(236, 239, 244),
    };

    public static TerminalColorScheme GruvboxDark => new()
    {
        Name = "Gruvbox Dark",
        IsBuiltIn = true,
        Foreground = Color.FromRgb(235, 219, 178),
        Background = Color.FromRgb(40, 40, 40),
        SelectionBackground = Color.FromArgb(77, 80, 73, 69),
        Black = Color.FromRgb(40, 40, 40), Red = Color.FromRgb(204, 36, 29),
        Green = Color.FromRgb(152, 151, 26), Yellow = Color.FromRgb(215, 153, 33),
        Blue = Color.FromRgb(69, 133, 136), Magenta = Color.FromRgb(177, 98, 134),
        Cyan = Color.FromRgb(104, 157, 106), White = Color.FromRgb(168, 153, 132),
        BrightBlack = Color.FromRgb(146, 131, 116), BrightRed = Color.FromRgb(251, 73, 52),
        BrightGreen = Color.FromRgb(184, 187, 38), BrightYellow = Color.FromRgb(250, 189, 47),
        BrightBlue = Color.FromRgb(131, 165, 152), BrightMagenta = Color.FromRgb(211, 134, 155),
        BrightCyan = Color.FromRgb(142, 192, 124), BrightWhite = Color.FromRgb(235, 219, 178),
    };

    public static TerminalColorScheme TokyoNight => new()
    {
        Name = "Tokyo Night",
        IsBuiltIn = true,
        Foreground = Color.FromRgb(169, 177, 214),
        Background = Color.FromRgb(26, 27, 38),
        SelectionBackground = Color.FromArgb(77, 51, 59, 91),
        Black = Color.FromRgb(21, 22, 30), Red = Color.FromRgb(247, 118, 142),
        Green = Color.FromRgb(158, 206, 106), Yellow = Color.FromRgb(224, 175, 104),
        Blue = Color.FromRgb(122, 162, 247), Magenta = Color.FromRgb(187, 154, 247),
        Cyan = Color.FromRgb(125, 207, 255), White = Color.FromRgb(169, 177, 214),
        BrightBlack = Color.FromRgb(65, 72, 104), BrightRed = Color.FromRgb(247, 118, 142),
        BrightGreen = Color.FromRgb(158, 206, 106), BrightYellow = Color.FromRgb(224, 175, 104),
        BrightBlue = Color.FromRgb(122, 162, 247), BrightMagenta = Color.FromRgb(187, 154, 247),
        BrightCyan = Color.FromRgb(125, 207, 255), BrightWhite = Color.FromRgb(200, 211, 245),
    };

    public static TerminalColorScheme CatppuccinMocha => new()
    {
        Name = "Catppuccin Mocha",
        IsBuiltIn = true,
        Foreground = Color.FromRgb(205, 214, 244),
        Background = Color.FromRgb(30, 30, 46),
        SelectionBackground = Color.FromArgb(77, 88, 91, 112),
        Black = Color.FromRgb(69, 71, 90), Red = Color.FromRgb(243, 139, 168),
        Green = Color.FromRgb(166, 227, 161), Yellow = Color.FromRgb(249, 226, 175),
        Blue = Color.FromRgb(137, 180, 250), Magenta = Color.FromRgb(245, 194, 231),
        Cyan = Color.FromRgb(148, 226, 213), White = Color.FromRgb(186, 194, 222),
        BrightBlack = Color.FromRgb(88, 91, 112), BrightRed = Color.FromRgb(243, 139, 168),
        BrightGreen = Color.FromRgb(166, 227, 161), BrightYellow = Color.FromRgb(249, 226, 175),
        BrightBlue = Color.FromRgb(137, 180, 250), BrightMagenta = Color.FromRgb(245, 194, 231),
        BrightCyan = Color.FromRgb(148, 226, 213), BrightWhite = Color.FromRgb(205, 214, 244),
    };

    /// <summary>
    /// Gets all built-in presets.
    /// </summary>
    public static List<TerminalColorScheme> GetAllPresets() => new()
    {
        Default, Dracula, SolarizedDark, SolarizedLight, Monokai, 
        OneDark, Nord, GruvboxDark, TokyoNight, CatppuccinMocha
    };
}
