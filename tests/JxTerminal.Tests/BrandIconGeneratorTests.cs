using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace JxTerminal.Tests;

[TestClass]
public class BrandIconGeneratorTests
{
    private static string GetProjectAssetsDir()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        var current = new DirectoryInfo(dir);
        while (current != null)
        {
            var assets = Path.Combine(current.FullName, "Assets");
            if (File.Exists(Path.Combine(current.FullName, "JxTerminal.csproj")) && Directory.Exists(assets))
            {
                return assets;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate project Assets directory.");
    }

    [TestMethod]
    public void GenerateAllBrandIcons()
    {
        var assetsDir = GetProjectAssetsDir();

        // 1. Generate Pure Transparent Glyph (for TitleBar, 0 border, 100% clean alpha)
        using var glyphSurface = SKSurface.Create(new SKImageInfo(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul));
        var glyphCanvas = glyphSurface.Canvas;
        glyphCanvas.Clear(SKColors.Transparent);
        DrawCyberPrismaticJxLogo(glyphCanvas, 512, withGlow: true);

        using var glyphImage = glyphSurface.Snapshot();
        using var glyphData = glyphImage.Encode(SKEncodedImageFormat.Png, 100);
        var glyphPath = Path.Combine(assetsDir, "jxterminal-glyph.png");
        File.WriteAllBytes(glyphPath, glyphData.ToArray());

        // 2. Generate Squircle App Icon (Dark titanium frosted tile + JX logo)
        using var appSurface = SKSurface.Create(new SKImageInfo(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul));
        var appCanvas = appSurface.Canvas;
        appCanvas.Clear(SKColors.Transparent);
        DrawSquircleAppTile(appCanvas, 512);

        using var appImage = appSurface.Snapshot();
        using var appData = appImage.Encode(SKEncodedImageFormat.Png, 100);
        var masterPngPath = Path.Combine(assetsDir, "jxterminal.png");
        File.WriteAllBytes(masterPngPath, appData.ToArray());

        // 3. Generate multi-resolution Windows ICO
        var icoPath = Path.Combine(assetsDir, "jxterminal.ico");
        var legacyIcoPath = Path.Combine(assetsDir, "avalonia-logo.ico");
        GenerateWindowsIco(appImage, icoPath);
        File.Copy(icoPath, legacyIcoPath, true);

        Assert.IsTrue(File.Exists(glyphPath));
        Assert.IsTrue(File.Exists(masterPngPath));
        Assert.IsTrue(File.Exists(icoPath));
    }

    /// <summary>
    /// Draws the Cyber Prismatic 'JX' Monogram:
    /// Bold, authoritative 3D geometric ribbon form with depth interlocking.
    /// 'J' incorporates the terminal prompt '>' chevron apex on the left.
    /// 'X' dynamically interlocks on the right.
    /// '_' terminal cursor underlines the design.
    /// </summary>
    private static void DrawCyberPrismaticJxLogo(SKCanvas canvas, float size, bool withGlow)
    {
        float scale = size / 512f;
        canvas.Save();
        canvas.Scale(scale);

        // Core Palette
        var electricCyan = new SKColor(0, 245, 255);    // #00F5FF
        var cyanGlow = new SKColor(6, 182, 212);        // #06B6D4
        var royalBlue = new SKColor(37, 99, 235);       // #2563EB
        var indigo = new SKColor(99, 102, 241);         // #6366F1
        var neonPurple = new SKColor(168, 85, 247);     // #A855F7
        var magenta = new SKColor(236, 72, 153);        // #EC4899

        // Geometry:
        // We use bold 52px wide smooth ribbon paths
        float strokeW = 50f;

        // J Ribbon:
        // Starts at (125, 140) -> slants down-right to (235, 250) (terminal prompt > top)
        // -> down to (235, 310) -> turns smoothly left to (175, 375) -> hooks up to (115, 315)
        using var jPath = new SKPath();
        jPath.MoveTo(125, 140);
        jPath.LineTo(235, 250);
        jPath.LineTo(235, 310);
        jPath.ArcTo(new SKPoint(235, 380), new SKPoint(175, 380), 60);
        jPath.ArcTo(new SKPoint(115, 380), new SKPoint(115, 320), 60);
        jPath.LineTo(115, 305);

        // X Ribbon 1 (Background slash: Top-Right to Bottom-Left)
        // From (395, 140) to (265, 380)
        using var xPathBack = new SKPath();
        xPathBack.MoveTo(395, 140);
        xPathBack.LineTo(265, 380);

        // X Ribbon 2 (Foreground slash: Top-Left to Bottom-Right)
        // From (265, 140) to (395, 380)
        using var xPathFront = new SKPath();
        xPathFront.MoveTo(265, 140);
        xPathFront.LineTo(395, 380);

        // Terminal Prompt Cursor '_'
        var cursorRect = new SKRoundRect(new SKRect(340, 405, 420, 422), 8, 8);

        // Shaders
        using var jShader = SKShader.CreateLinearGradient(
            new SKPoint(115, 140),
            new SKPoint(235, 380),
            new[] { electricCyan, cyanGlow, royalBlue },
            new[] { 0f, 0.5f, 1f },
            SKShaderTileMode.Clamp);

        using var xBackShader = SKShader.CreateLinearGradient(
            new SKPoint(395, 140),
            new SKPoint(265, 380),
            new[] { neonPurple, indigo, royalBlue },
            new[] { 0f, 0.5f, 1f },
            SKShaderTileMode.Clamp);

        using var xFrontShader = SKShader.CreateLinearGradient(
            new SKPoint(265, 140),
            new SKPoint(395, 380),
            new[] { royalBlue, indigo, neonPurple, magenta },
            new[] { 0f, 0.35f, 0.75f, 1f },
            SKShaderTileMode.Clamp);

        using var cursorShader = SKShader.CreateLinearGradient(
            new SKPoint(340, 405),
            new SKPoint(420, 422),
            new[] { neonPurple, magenta },
            null,
            SKShaderTileMode.Clamp);

        // 1. Soft Ambient Neon Bloom (Layer 0)
        if (withGlow)
        {
            using var bloomJ = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 72,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
                Shader = jShader,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 26)
            };
            canvas.DrawPath(jPath, bloomJ);

            using var bloomX1 = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 72,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
                Shader = xBackShader,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 26)
            };
            canvas.DrawPath(xPathBack, bloomX1);

            using var bloomX2 = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 72,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
                Shader = xFrontShader,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 26)
            };
            canvas.DrawPath(xPathFront, bloomX2);

            using var bloomCur = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
                Shader = cursorShader,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 16)
            };
            canvas.DrawRoundRect(cursorRect, bloomCur);
        }

        // 2. Base Solid Ribbons (Layer 1)
        // Draw J first
        using var paintJ = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeW,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
            Shader = jShader
        };
        canvas.DrawPath(jPath, paintJ);

        // Draw X Back Slash
        using var paintXBack = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeW,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
            Shader = xBackShader
        };
        canvas.DrawPath(xPathBack, paintXBack);

        // Ambient Occlusion shadow under X Front Slash crossing point (330, 260)
        using var occlShadow = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 140),
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10)
        };
        canvas.DrawCircle(330, 260, 24, occlShadow);

        // Draw X Front Slash (Overlapping to create 3D depth)
        using var paintXFront = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeW,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
            Shader = xFrontShader
        };
        canvas.DrawPath(xPathFront, paintXFront);

        // Draw Cursor
        using var paintCursor = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            Shader = cursorShader
        };
        canvas.DrawRoundRect(cursorRect, paintCursor);

        // 3. Crisp Prismatic Facet Center Rib (Layer 2, Width 15px)
        using var facetJShader = SKShader.CreateLinearGradient(
            new SKPoint(115, 140),
            new SKPoint(235, 380),
            new[] { SKColors.White, new SKColor(186, 230, 253, 240), new SKColor(147, 197, 253, 190) },
            new[] { 0f, 0.45f, 1f },
            SKShaderTileMode.Clamp);

        using var facetJ = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 15,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
            Shader = facetJShader
        };
        canvas.DrawPath(jPath, facetJ);

        using var facetXBackShader = SKShader.CreateLinearGradient(
            new SKPoint(395, 140),
            new SKPoint(265, 380),
            new[] { new SKColor(243, 232, 255, 230), new SKColor(199, 210, 254, 200) },
            null,
            SKShaderTileMode.Clamp);

        using var facetXBack = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 15,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
            Shader = facetXBackShader
        };
        canvas.DrawPath(xPathBack, facetXBack);

        using var facetXFrontShader = SKShader.CreateLinearGradient(
            new SKPoint(265, 140),
            new SKPoint(395, 380),
            new[] { new SKColor(199, 210, 254, 230), new SKColor(243, 232, 255, 240), new SKColor(252, 231, 243, 230) },
            new[] { 0f, 0.5f, 1f },
            SKShaderTileMode.Clamp);

        using var facetXFront = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 15,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
            Shader = facetXFrontShader
        };
        canvas.DrawPath(xPathFront, facetXFront);

        // 4. Brilliant Quantum Intersection Sparks
        // A. Prompt apex at (235, 250)
        using var sparkBloom = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6)
        };
        canvas.DrawCircle(235, 250, 9, sparkBloom);
        using var sparkCore = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };
        canvas.DrawCircle(235, 250, 4.5f, sparkCore);

        // B. X intersection at (330, 260)
        canvas.DrawCircle(330, 260, 11, sparkBloom);
        canvas.DrawCircle(330, 260, 5.5f, sparkCore);

        canvas.Restore();
    }

    /// <summary>
    /// Draws the modern Squircle App Tile with deep space dark obsidian gradient,
    /// seamless anti-aliased squircle mask, and the centered JX logo.
    /// </summary>
    private static void DrawSquircleAppTile(SKCanvas canvas, float size)
    {
        float padding = 20f;
        float tileSize = size - padding * 2;
        float cornerRadius = 112f;

        var squircleRect = new SKRoundRect(new SKRect(padding, padding, padding + tileSize, padding + tileSize), cornerRadius, cornerRadius);

        // 1. Soft Ambient Drop Shadow under tile
        using var shadowPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 110),
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 18)
        };
        var shadowRect = new SKRoundRect(new SKRect(padding, padding + 10, padding + tileSize, padding + tileSize + 10), cornerRadius, cornerRadius);
        canvas.DrawRoundRect(shadowRect, shadowPaint);

        // 2. Obsidian Titanium Tile Surface (#090D16 -> #131A29)
        using var tileShader = SKShader.CreateLinearGradient(
            new SKPoint(size / 2, padding),
            new SKPoint(size / 2, padding + tileSize),
            new[] { new SKColor(18, 25, 40), new SKColor(8, 12, 19) },
            null,
            SKShaderTileMode.Clamp);

        using var tilePaint = new SKPaint
        {
            Shader = tileShader,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRoundRect(squircleRect, tilePaint);

        // 3. Internal Subtle Ambient Neon Glow
        using var radialShader = SKShader.CreateRadialGradient(
            new SKPoint(256, 256),
            200f,
            new[] { new SKColor(37, 99, 235, 50), new SKColor(139, 92, 246, 25), SKColors.Transparent },
            new[] { 0f, 0.55f, 1f },
            SKShaderTileMode.Clamp);

        using var radialPaint = new SKPaint
        {
            Shader = radialShader,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRoundRect(squircleRect, radialPaint);

        // 4. Subtle Inner Rim Stroke (Clean Cyan-Blue shimmer, no white fringe)
        using var rimShader = SKShader.CreateLinearGradient(
            new SKPoint(size / 2, padding),
            new SKPoint(size / 2, padding + tileSize),
            new[] { new SKColor(56, 189, 248, 140), new SKColor(30, 58, 138, 50), new SKColor(15, 23, 42, 80) },
            new[] { 0f, 0.35f, 1f },
            SKShaderTileMode.Clamp);

        using var rimPaint = new SKPaint
        {
            Shader = rimShader,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f
        };
        canvas.DrawRoundRect(squircleRect, rimPaint);

        // 5. Centered JX Monogram
        canvas.Save();
        canvas.Translate(size * 0.045f, size * 0.02f);
        canvas.Scale(0.91f);
        DrawCyberPrismaticJxLogo(canvas, size, withGlow: true);
        canvas.Restore();
    }

    /// <summary>
    /// Generates multi-resolution Windows ICO with PNG compression.
    /// </summary>
    private static void GenerateWindowsIco(SKImage source, string icoPath)
    {
        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        var pngStreams = new List<byte[]>();

        foreach (var s in sizes)
        {
            using var resizedBmp = new SKBitmap(s, s, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(resizedBmp);
            canvas.Clear(SKColors.Transparent);

            using var paint = new SKPaint
            {
                IsAntialias = true
            };
            canvas.DrawImage(source, new SKRect(0, 0, s, s), paint);

            using var image = SKImage.FromBitmap(resizedBmp);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            pngStreams.Add(data.ToArray());
        }

        using var fs = File.Create(icoPath);
        using var bw = new BinaryWriter(fs);

        bw.Write((ushort)0);
        bw.Write((ushort)1);
        bw.Write((ushort)sizes.Length);

        int offset = 6 + (sizes.Length * 16);

        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            var data = pngStreams[i];
            byte dim = s >= 256 ? (byte)0 : (byte)s;

            bw.Write(dim);
            bw.Write(dim);
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)1);
            bw.Write((ushort)32);
            bw.Write((uint)data.Length);
            bw.Write((uint)offset);

            offset += data.Length;
        }

        for (int i = 0; i < sizes.Length; i++)
        {
            bw.Write(pngStreams[i]);
        }

        bw.Flush();
    }
}
