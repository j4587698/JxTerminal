using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using JxTerminal.Controls;
using JxTerminal.Models;
using JxTerminal.Services.Terminal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
public class FontHelperTests
{
    [TestMethod]
    public void IsMonospace_ReturnsTrue_ForKnownMonospaceFonts()
    {
        Assert.IsTrue(FontHelper.IsMonospace("Consolas"));
        Assert.IsTrue(FontHelper.IsMonospace("Cascadia Code"));
        Assert.IsTrue(FontHelper.IsMonospace("Cascadia Mono"));
        Assert.IsTrue(FontHelper.IsMonospace("JetBrains Mono"));
        Assert.IsTrue(FontHelper.IsMonospace("Courier New"));
        Assert.IsTrue(FontHelper.IsMonospace("Fira Code"));
        Assert.IsTrue(FontHelper.IsMonospace("Lucida Console"));
        Assert.IsTrue(FontHelper.IsMonospace("Sarasa Mono SC"));
        Assert.IsTrue(FontHelper.IsMonospace("Maple Mono"));
    }

    [TestMethod]
    public void IsMonospace_ReturnsTrue_ForFallbackStringsContainingMonospace()
    {
        Assert.IsTrue(FontHelper.IsMonospace("DejaVu Sans Mono, Liberation Mono, Consolas, monospace"));
        Assert.IsTrue(FontHelper.IsMonospace("CustomFont, monospace"));
    }

    [TestMethod]
    public void IsMonospace_ReturnsFalse_ForProportionalFonts()
    {
        Assert.IsFalse(FontHelper.IsMonospace("Arial"));
        Assert.IsFalse(FontHelper.IsMonospace("Times New Roman"));
        Assert.IsFalse(FontHelper.IsMonospace("Segoe UI"));
        Assert.IsFalse(FontHelper.IsMonospace("Calibri"));
        Assert.IsFalse(FontHelper.IsMonospace("Comic Sans MS"));
        Assert.IsFalse(FontHelper.IsMonospace("Georgia"));
        Assert.IsFalse(FontHelper.IsMonospace("Verdana"));
        Assert.IsFalse(FontHelper.IsMonospace("宋体"));
        Assert.IsFalse(FontHelper.IsMonospace("仿宋"));
        Assert.IsFalse(FontHelper.IsMonospace("楷体"));
        Assert.IsFalse(FontHelper.IsMonospace("黑体"));
        Assert.IsFalse(FontHelper.IsMonospace("微软雅黑"));
        Assert.IsFalse(FontHelper.IsMonospace("新宋体"));
        Assert.IsFalse(FontHelper.IsMonospace("幼圆"));
        Assert.IsFalse(FontHelper.IsMonospace("隶书"));
        Assert.IsFalse(FontHelper.IsMonospace("NSimSun"));
        Assert.IsFalse(FontHelper.IsMonospace("YouYuan"));
        Assert.IsFalse(FontHelper.IsMonospace("LiSu"));
    }

    [TestMethod]
    public void IsMonospace_ReturnsFalse_ForEmptyOrNull()
    {
        Assert.IsFalse(FontHelper.IsMonospace(""));
        Assert.IsFalse(FontHelper.IsMonospace("   "));
        Assert.IsFalse(FontHelper.IsMonospace((string)null!));
    }

    [TestMethod]
    public void GetMonospaceFonts_ReturnsCleanList_ExcludingCjkDocumentFonts()
    {
        var fonts = FontHelper.GetMonospaceFonts();
        Assert.IsNotNull(fonts);
        Assert.IsTrue(fonts.Count > 0, "Monospace font list must not be empty");
        Assert.IsTrue(fonts.Contains("Consolas") || fonts.Contains("Cascadia Mono") || fonts.Contains("Courier New"));

        // Must not contain any CJK document, decorative, calligraphy fonts
        Assert.IsFalse(fonts.Contains("宋体"));
        Assert.IsFalse(fonts.Contains("仿宋"));
        Assert.IsFalse(fonts.Contains("楷体"));
        Assert.IsFalse(fonts.Contains("黑体"));
        Assert.IsFalse(fonts.Contains("微软雅黑"));
        Assert.IsFalse(fonts.Contains("新宋体"));
        Assert.IsFalse(fonts.Contains("幼圆"));
        Assert.IsFalse(fonts.Contains("隶书"));
        Assert.IsFalse(fonts.Contains("Arial"));
    }


    [TestMethod]
    public void TerminalSettings_FontFamily_FallbackToDefaultWhenEmpty()
    {
        var settings = new TerminalSettings();
        settings.FontFamily = "";
        Assert.AreEqual(TerminalSettings.DefaultFontFamily, settings.FontFamily);

        settings.FontFamily = "   ";
        Assert.AreEqual(TerminalSettings.DefaultFontFamily, settings.FontFamily);
    }

    [TestMethod]
    public void TerminalGlyphRenderer_DrawsGridAlignedRun_ForProportionalAndCjkFonts()
    {
        try { AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting(); } catch { }

        double cellWidth = 10.0;
        double cellHeight = 20.0;
        var typeface = new Typeface("Arial");
        var cells = new List<TerminalGlyphRenderer.CellSpan>
        {
            new(0, 1, 1), // 'h'
            new(1, 1, 1), // 'e'
            new(2, 1, 1), // 'l'
            new(3, 1, 1), // 'l'
            new(4, 1, 1), // 'o'
            new(5, 1, 1), // ' '
            new(6, 2, 1), // '世' (2 columns)
            new(8, 2, 1)  // '界' (2 columns)
        };
        string fullText = "hello 世界";

        var drawingGroup = new DrawingGroup();
        using (var context = drawingGroup.Open())
        {
            TerminalGlyphRenderer.DrawGridAlignedRun(
                context,
                fullText,
                cells,
                typeface,
                14,
                Brushes.White,
                0,
                cellWidth,
                cellHeight);
        }

        Assert.IsTrue(drawingGroup.Children.Count >= 1, "Should produce at least one GlyphRunDrawing");
        
        // Sum total advance of all glyph runs
        double totalAdvance = 0;
        foreach (var child in drawingGroup.Children)
        {
            if (child is GlyphRunDrawing grd && grd.GlyphRun != null)
            {
                totalAdvance += grd.GlyphRun.GlyphInfos.Sum(g => g.GlyphAdvance);
            }
        }

        // Expected total width: 10 columns * 10px = 100px
        Assert.AreEqual(100.0, totalAdvance, 0.5, "Total advance across all glyphs must strictly match total cell columns * cellWidth");
    }

    [TestMethod]
    public void TestPowerShellBanner()
    {
        try { AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting(); } catch { }

        string text = "版权所有 (C) Microsoft Corporation. 保留所有权利。";
        var tf = new Typeface("YouYuan");

        var cells = new List<TerminalGlyphRenderer.CellSpan>();
        int col = 0;
        foreach (var ch in text)
        {
            int w = ch >= 0x4e00 && ch <= 0x9fff || ch == '（' || ch == '）' || ch == '。' ? 2 : 1;
            cells.Add(new TerminalGlyphRenderer.CellSpan(col, w, 1));
            col += w;
        }

        double cellWidth = 7.0;
        double cellHeight = 19.0;
        var drawingGroup = new DrawingGroup();
        using (var context = drawingGroup.Open())
        {
            TerminalGlyphRenderer.DrawGridAlignedRun(
                context,
                text,
                cells,
                tf,
                14,
                Brushes.White,
                0,
                cellWidth,
                cellHeight);
        }

        Assert.IsTrue(drawingGroup.Children.Count >= 1);
        double totalAdvance = 0;
        foreach (var child in drawingGroup.Children)
        {
            if (child is GlyphRunDrawing grd && grd.GlyphRun != null)
            {
                totalAdvance += grd.GlyphRun.GlyphInfos.Sum(g => g.GlyphAdvance);
            }
        }

        double expectedWidth = col * cellWidth;
        Assert.AreEqual(expectedWidth, totalAdvance, 0.5);
    }

    [TestMethod]
    public void TerminalGlyphRenderer_EmptyOrNullText_DoesNotThrow()
    {
        var drawingGroup = new DrawingGroup();
        using (var context = drawingGroup.Open())
        {
            TerminalGlyphRenderer.DrawGridAlignedRun(
                context,
                "",
                Array.Empty<TerminalGlyphRenderer.CellSpan>(),
                new Typeface("Consolas"),
                14,
                Brushes.White,
                0,
                10,
                20);
        }

        Assert.AreEqual(0, drawingGroup.Children.Count);
    }

    [TestMethod]
    public void GetAllFonts_ReturnsSystemFonts_ContainingMonospaceAndProportional()
    {
        var allFonts = FontHelper.GetAllFonts();
        Assert.IsNotNull(allFonts);
        Assert.IsTrue(allFonts.Count > 0);

        var monoFonts = FontHelper.GetMonospaceFonts();
        Assert.IsTrue(allFonts.Count >= monoFonts.Count, "All fonts list must be at least as large as monospace fonts list");
    }

    [TestMethod]
    public void FontPickerViewModel_DefaultsToShowMonospace_AndTogglesToAllFonts()
    {
        var mono = new List<string> { "Cascadia Mono", "Consolas" };
        var all = new List<string> { "Arial", "Cascadia Mono", "Consolas", "SimSun", "YouYuan" };

        var vm = new JxTerminal.ViewModels.FontPickerViewModel(mono, all, "Consolas");

        Assert.IsFalse(vm.ShowAllFonts, "ShowAllFonts must be false by default for monospace current font");
        Assert.AreEqual(2, vm.FilteredFonts.Count);
        Assert.IsTrue(vm.FilteredFonts.Contains("Consolas"));
        Assert.IsFalse(vm.FilteredFonts.Contains("SimSun"));

        // Toggle to show all fonts
        vm.ShowAllFonts = true;
        Assert.AreEqual(5, vm.FilteredFonts.Count);
        Assert.IsTrue(vm.FilteredFonts.Contains("SimSun"));
        Assert.IsTrue(vm.FilteredFonts.Contains("YouYuan"));

        // Search in all fonts
        vm.SearchText = "Yuan";
        Assert.AreEqual(1, vm.FilteredFonts.Count);
        Assert.AreEqual("YouYuan", vm.FilteredFonts[0]);
    }

    [TestMethod]
    public void FontPickerViewModel_NonMonospaceCurrentFont_AutomaticallyEnablesShowAllFonts()
    {
        var mono = new List<string> { "Cascadia Mono", "Consolas" };
        var all = new List<string> { "Arial", "Cascadia Mono", "Consolas", "SimSun", "YouYuan" };

        var vm = new JxTerminal.ViewModels.FontPickerViewModel(mono, all, "YouYuan");

        Assert.IsTrue(vm.ShowAllFonts, "ShowAllFonts must auto-enable when current font is non-monospace");
        Assert.AreEqual(5, vm.FilteredFonts.Count);
        Assert.AreEqual("YouYuan", vm.SelectedFont);
    }
}
