using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Media;

namespace JxTerminal.Services.Terminal;

/// <summary>
/// Discovers and validates monospaced fonts using standard OpenType metrics
/// and geometric character advance measurements, without relying on hardcoded name lists.
/// </summary>
public static class FontHelper
{
    private static List<string>? _cachedMonospaceFonts;
    private static List<string>? _cachedAllFonts;

    /// <summary>
    /// Standard monospace families used for fast checks and headless environments.
    /// </summary>
    private static readonly HashSet<string> StandardMonospaceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cascadia Mono",
        "Cascadia Code",
        "Consolas",
        "Courier New",
        "Courier",
        "DejaVu Sans Mono",
        "Fira Code",
        "Fira Mono",
        "Hack",
        "Inconsolata",
        "JetBrains Mono",
        "Liberation Mono",
        "Lucida Console",
        "Menlo",
        "Monaco",
        "Monospace",
        "Noto Sans Mono",
        "Oxygen Mono",
        "PT Mono",
        "Roboto Mono",
        "Sarasa Mono",
        "Sarasa Mono SC",
        "Maple Mono",
        "Source Code Pro",
        "Ubuntu Mono"
    };

    /// <summary>
    /// Returns all monospaced fonts available on the current system based on OpenType
    /// fixed-pitch metadata and glyph advance geometry. Results are cached after first retrieval.
    /// </summary>
    public static List<string> GetMonospaceFonts()
    {
        if (_cachedMonospaceFonts != null)
        {
            return _cachedMonospaceFonts;
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var fontManager = FontManager.Current;
            var systemFonts = fontManager.SystemFonts;

            foreach (var family in systemFonts)
            {
                var name = family.Name;
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (IsMonospace(family))
                {
                    result.Add(name);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to enumerate monospace fonts via FontManager: {ex.Message}");
        }

        // Guarantee fallback fonts if enumeration yielded no fonts (e.g. headless environment)
        if (result.Count == 0)
        {
            foreach (var font in StandardMonospaceNames)
            {
                result.Add(font);
            }
        }

        _cachedMonospaceFonts = result.OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList();
        return _cachedMonospaceFonts;
    }

    /// <summary>
    /// Returns all available system font families sorted by name, cached after first retrieval.
    /// </summary>
    public static List<string> GetAllFonts()
    {
        if (_cachedAllFonts != null)
        {
            return _cachedAllFonts;
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var systemFonts = FontManager.Current.SystemFonts;
            foreach (var family in systemFonts)
            {
                if (!string.IsNullOrWhiteSpace(family.Name))
                {
                    result.Add(family.Name);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to enumerate all system fonts via FontManager: {ex.Message}");
        }

        if (result.Count == 0)
        {
            foreach (var f in GetMonospaceFonts())
            {
                result.Add(f);
            }
        }

        _cachedAllFonts = result.OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList();
        return _cachedAllFonts;
    }

    /// <summary>
    /// Determines whether the specified font name or font family string is monospaced.
    /// </summary>
    public static bool IsMonospace(string fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName)) return false;

        var primary = fontName.Split(',')[0].Trim();
        if (fontName.Contains("monospace", StringComparison.OrdinalIgnoreCase))
            return true;

        if (StandardMonospaceNames.Contains(primary))
            return true;

        try
        {
            return IsMonospace(new FontFamily(primary));
        }
        catch
        {
            return primary.Contains("Mono", StringComparison.OrdinalIgnoreCase)
                || primary.Contains("Console", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Evaluates whether a font family is monospaced using:
    /// 1. OpenType / TrueType post.isFixedPitch specification flag
    /// 2. Geometric consistency across representative ASCII and symbol characters
    /// </summary>
    public static bool IsMonospace(FontFamily family)
    {
        if (family == null || string.IsNullOrWhiteSpace(family.Name))
            return false;

        var name = family.Name;

        try
        {
            var typeface = new Typeface(family);

            // 1. Inspect OpenType Metrics.IsFixedPitch from font header.
            // In TrueType/OpenType specifications, fonts like SimSun, FangSong, Arial, Segoe UI
            // have isFixedPitch = false, whereas true terminal fonts declare isFixedPitch = true.
            if (FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphTypeface))
            {
                if (!glyphTypeface.Metrics.IsFixedPitch)
                {
                    return false;
                }

                // 2. Terminal Grid Alignment Check:
                // Terminal emulators require single-width box-drawing symbols ('─' U+2500)
                // to match the fundamental ASCII character advance width (1-cell grid).
                // Legacy CJK printing, decorative, and calligraphy fonts (e.g. NSimSun, YouYuan, LiSu)
                // declare post.isFixedPitch = true for English letters, but encode box-drawing characters
                // as full-width (2 cells) Chinese symbols (advBoxH == advA * 2), which breaks terminal layouts.
                var map = glyphTypeface.CharacterToGlyphMap;
                if (map.TryGetGlyph('A', out var gA) &&
                    map.TryGetGlyph('\u2500', out var gBoxH) && gBoxH != 0)
                {
                    glyphTypeface.TryGetHorizontalGlyphAdvance(gA, out var advA);
                    glyphTypeface.TryGetHorizontalGlyphAdvance(gBoxH, out var advBoxH);
                    if (advA > 0 && advBoxH > 0 && advA != advBoxH)
                    {
                        return false;
                    }
                }
            }

            // 2. Geometric advance verification across diverse glyph widths (narrow, wide, symbols)
            var fI = new FormattedText("i", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 14, Brushes.White);
            var fM = new FormattedText("M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 14, Brushes.White);
            var fW = new FormattedText("W", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 14, Brushes.White);
            var fAt = new FormattedText("@", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 14, Brushes.White);
            var fPeriod = new FormattedText(".", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 14, Brushes.White);

            var wI = fI.WidthIncludingTrailingWhitespace;
            var wM = fM.WidthIncludingTrailingWhitespace;
            var wW = fW.WidthIncludingTrailingWhitespace;
            var wAt = fAt.WidthIncludingTrailingWhitespace;
            var wP = fPeriod.WidthIncludingTrailingWhitespace;

            if (wI <= 0 ||
                Math.Abs(wI - wM) > 0.01 ||
                Math.Abs(wM - wW) > 0.01 ||
                Math.Abs(wI - wAt) > 0.01 ||
                Math.Abs(wI - wP) > 0.01)
            {
                return false;
            }

            return true;
        }
        catch
        {
            // Fallback for headless environments without font backend
            return StandardMonospaceNames.Contains(name) ||
                   name.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Console", StringComparison.OrdinalIgnoreCase);
        }
    }
}
