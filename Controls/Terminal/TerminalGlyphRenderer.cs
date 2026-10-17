using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace JxTerminal.Controls;

/// <summary>
/// High-performance grid-aligned glyph renderer for terminal cells.
/// Similar to Windows Terminal's AtlasEngine, this binds glyph advances and positions
/// strictly to terminal character grid columns (col * charWidth).
/// Ensures precise cursor, selection, and mouse click alignment regardless of whether
/// the font is monospace, proportional (e.g. Arial), or CJK (e.g. SimSun, YouYuan).
/// </summary>
public static class TerminalGlyphRenderer
{
    public readonly struct CellSpan
    {
        public readonly int Col;
        public readonly int Width;
        public readonly int CharCount;

        public CellSpan(int col, int width, int charCount)
        {
            Col = col;
            Width = width;
            CharCount = charCount;
        }
    }

    /// <summary>
    /// Renders a style-batched run of cells aligned to the terminal grid columns.
    /// </summary>
    public static void DrawGridAlignedRun(
        DrawingContext context,
        string fullText,
        IReadOnlyList<CellSpan> cells,
        Typeface typeface,
        double fontSize,
        IBrush fgBrush,
        double yPos,
        double charWidth,
        double charHeight)
    {
        if (string.IsNullOrEmpty(fullText) || cells.Count == 0 || charWidth <= 0 || charHeight <= 0)
        {
            return;
        }

        int totalChars = fullText.Length;
        Span<int> cellMap = totalChars <= 512 ? stackalloc int[totalChars] : new int[totalChars];
        int curCellIdx = 0;
        int curCellCharRemaining = cells[0].CharCount;

        for (int charIdx = 0; charIdx < totalChars; charIdx++)
        {
            while (curCellCharRemaining <= 0 && curCellIdx + 1 < cells.Count)
            {
                curCellIdx++;
                curCellCharRemaining = cells[curCellIdx].CharCount;
            }
            cellMap[charIdx] = curCellIdx;
            curCellCharRemaining--;
        }

        var layout = new TextLayout(fullText, typeface, fontSize, fgBrush);

        int textOffset = 0;
        foreach (var line in layout.TextLines)
        {
            double textVerticalOffset = (charHeight - line.Height) / 2.0;
            double baselineY = yPos + textVerticalOffset + line.Baseline;

            foreach (var tr in line.TextRuns)
            {
                if (tr is not ShapedTextRun str)
                {
                    continue;
                }

                var origRun = str.GlyphRun;
                if (origRun == null || origRun.GlyphInfos.Count == 0)
                {
                    textOffset += str.Length;
                    continue;
                }

                int glyphCount = origRun.GlyphInfos.Count;
                int firstCluster = origRun.GlyphInfos[0].GlyphCluster;
                int baseOffset = firstCluster < textOffset ? textOffset : 0;

                int firstChar = baseOffset + firstCluster;
                int firstCellIdx = cellMap[Math.Clamp(firstChar, 0, totalChars - 1)];
                double runStartX = cells[firstCellIdx].Col * charWidth;

                var newInfos = new GlyphInfo[glyphCount];

                for (int gi = 0; gi < glyphCount; gi++)
                {
                    var info = origRun.GlyphInfos[gi];

                    // Zero-advance glyphs (such as combining accents) keep zero advance and HarfBuzz positioning
                    if (info.GlyphAdvance == 0)
                    {
                        newInfos[gi] = info;
                        continue;
                    }

                    int charStart = baseOffset + info.GlyphCluster;
                    int cell1 = cellMap[Math.Clamp(charStart, 0, totalChars - 1)];
                    int startCol = cells[cell1].Col;

                    int endCol;
                    if (gi + 1 < glyphCount)
                    {
                        int charNext = baseOffset + origRun.GlyphInfos[gi + 1].GlyphCluster;
                        if (charNext > charStart)
                        {
                            int cell2 = cellMap[Math.Clamp(charNext, 0, totalChars - 1)];
                            endCol = cells[cell2].Col;
                        }
                        else
                        {
                            endCol = startCol + cells[cell1].Width;
                        }
                    }
                    else
                    {
                        int lastChar = baseOffset + origRun.GlyphInfos[glyphCount - 1].GlyphCluster;
                        int cellLast = cellMap[Math.Clamp(lastChar, 0, totalChars - 1)];
                        endCol = cells[cellLast].Col + cells[cellLast].Width;
                    }

                    int spanCols = Math.Max(cells[cell1].Width, endCol - startCol);
                    double targetAdvance = spanCols * charWidth;

                    // Center glyph in its cell if narrower than cell grid column
                    double hOffset = 0;
                    if (targetAdvance > info.GlyphAdvance)
                    {
                        hOffset = (targetAdvance - info.GlyphAdvance) / 2.0;
                    }

                    newInfos[gi] = new GlyphInfo(
                        info.GlyphIndex,
                        info.GlyphCluster,
                        targetAdvance,
                        new Vector(info.GlyphOffset.X + hOffset, info.GlyphOffset.Y));
                }

                using var gridRun = new GlyphRun(
                    origRun.GlyphTypeface,
                    origRun.FontRenderingEmSize,
                    origRun.Characters,
                    newInfos,
                    new Point(runStartX, baselineY),
                    origRun.BiDiLevel);

                context.DrawGlyphRun(fgBrush, gridRun);

                textOffset += str.Length;
            }
        }
    }
}
