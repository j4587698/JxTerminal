using System.Text.Json;
using JxTerminal.Models;
using JxTerminal.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
public class TerminalSettingsSerializationTests
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static TerminalSettings RoundTrip(TerminalSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, AppJsonContext.Default.TerminalSettings);
        return JsonSerializer.Deserialize(json, AppJsonContext.Default.TerminalSettings)!;
    }

    [TestMethod]
    public void RoundTrip_PreservesSessionOptions()
    {
        var settings = new TerminalSettings
        {
            FontSize = 17,
            FontFamily = "JetBrains Mono",
            ScrollbackLines = 5000,
            RemoteEncoding = "GBK",
            LocalShellKind = "Cmd",
            CursorStyle = "Bar",
            CopyOnSelect = true,
            ColorSchemeName = "Dracula"
        };

        var restored = RoundTrip(settings);

        Assert.AreEqual(17, restored.FontSize);
        Assert.AreEqual("JetBrains Mono", restored.FontFamily);
        Assert.AreEqual(5000, restored.ScrollbackLines);
        Assert.AreEqual("GBK", restored.RemoteEncoding);
        Assert.AreEqual("Cmd", restored.LocalShellKind);
        Assert.AreEqual("Bar", restored.CursorStyle);
        Assert.IsTrue(restored.CopyOnSelect);
        Assert.AreEqual("Dracula", restored.ColorSchemeName);
    }

    [TestMethod]
    public void Defaults_SurviveRoundTrip()
    {
        var restored = RoundTrip(new TerminalSettings());

        Assert.AreEqual(14, restored.FontSize);
        Assert.AreEqual("UTF-8", restored.RemoteEncoding);
        Assert.AreEqual("Block", restored.CursorStyle);
        Assert.IsFalse(restored.CopyOnSelect);
    }

    [TestMethod]
    public void RoundTrip_PreservesAiSettings_AndNeverExposesPlaintextKey()
    {
        var settings = new TerminalSettings
        {
            AiEndpoint = "https://api.deepseek.com/v1",
            AiModel = "deepseek-chat",
            AiAutoApproveCommands = false,
            AiApiKey = "sk-secret-123",
            AiMaxToolRounds = 35
        };

        var json = JsonSerializer.Serialize(settings, AppJsonContext.Default.TerminalSettings);
        Assert.IsFalse(json.Contains("sk-secret-123"), "API key must not be serialized in plain text");

        var restored = JsonSerializer.Deserialize(json, AppJsonContext.Default.TerminalSettings)!;
        Assert.AreEqual("https://api.deepseek.com/v1", restored.AiEndpoint);
        Assert.AreEqual("deepseek-chat", restored.AiModel);
        Assert.IsFalse(restored.AiAutoApproveCommands);
        Assert.AreEqual("sk-secret-123", restored.AiApiKey);
        Assert.AreEqual(35, restored.AiMaxToolRounds);
        Assert.IsTrue(restored.IsAiConfigured);
    }

    [TestMethod]
    public void AiSettings_Defaults_AreUnconfigured()
    {
        var settings = new TerminalSettings();
        Assert.IsFalse(settings.IsAiConfigured);
        Assert.IsTrue(settings.AiAutoApproveCommands);
        Assert.AreEqual(25, settings.AiMaxToolRounds);
    }

    [TestMethod]
    public void CustomColorSchemes_SurviveRoundTrip_EvenWhenPresetActive()
    {
        var settings = new TerminalSettings
        {
            ColorSchemeName = "Dracula"
        };
        var custom = new TerminalColorScheme
        {
            Name = "My Custom Scheme",
            IsBuiltIn = false,
            Background = Avalonia.Media.Colors.DarkSlateBlue,
            Foreground = Avalonia.Media.Colors.MintCream
        };
        settings.CustomColorSchemes.Add(custom);

        var restored = RoundTrip(settings);
        restored.ResolveColorScheme();

        Assert.AreEqual("Dracula", restored.ColorSchemeName);
        Assert.IsTrue(restored.ColorScheme.IsBuiltIn);
        Assert.AreEqual(1, restored.CustomColorSchemes.Count);
        Assert.AreEqual("My Custom Scheme", restored.CustomColorSchemes[0].Name);
        Assert.AreEqual(Avalonia.Media.Colors.DarkSlateBlue, restored.CustomColorSchemes[0].Background);
        Assert.AreEqual(Avalonia.Media.Colors.MintCream, restored.CustomColorSchemes[0].Foreground);
    }

    [TestMethod]
    public void CustomColorScheme_Active_SurvivesRoundTripAndResolves()
    {
        var settings = new TerminalSettings();
        var custom = new TerminalColorScheme
        {
            Name = "Retro Amber",
            IsBuiltIn = false,
            Background = Avalonia.Media.Color.FromRgb(10, 10, 10),
            Foreground = Avalonia.Media.Color.FromRgb(255, 176, 0)
        };
        settings.ApplyScheme(custom);

        Assert.AreEqual("Retro Amber", settings.ColorSchemeName);
        Assert.IsFalse(settings.ColorScheme.IsBuiltIn);

        var restored = RoundTrip(settings);
        restored.ResolveColorScheme();

        Assert.AreEqual("Retro Amber", restored.ColorSchemeName);
        Assert.IsFalse(restored.ColorScheme.IsBuiltIn);
        Assert.AreEqual(Avalonia.Media.Color.FromRgb(10, 10, 10), restored.ColorScheme.Background);
        Assert.AreEqual(Avalonia.Media.Color.FromRgb(255, 176, 0), restored.ColorScheme.Foreground);
        Assert.AreEqual(1, restored.CustomColorSchemes.Count);
    }

    [TestMethod]
    public void LegacyCustomColorScheme_ImportsIntoCustomColorSchemes()
    {
        var legacyJson = """
        {
          "ColorSchemeName": "Legacy Custom",
          "CustomColorScheme": {
            "Name": "Legacy Custom",
            "IsBuiltIn": false,
            "Foreground": "#FFFFFFFF",
            "Background": "#FF000000"
          }
        }
        """;

        var restored = JsonSerializer.Deserialize(legacyJson, AppJsonContext.Default.TerminalSettings)!;
        restored.ResolveColorScheme();

        Assert.AreEqual("Legacy Custom", restored.ColorSchemeName);
        Assert.IsFalse(restored.ColorScheme.IsBuiltIn);
        Assert.AreEqual(1, restored.CustomColorSchemes.Count);
        Assert.AreEqual("Legacy Custom", restored.CustomColorSchemes[0].Name);
    }
}
