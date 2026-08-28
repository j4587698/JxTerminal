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
        // Same strategy as DataStore: reflection serializer, because the STJ source-gen
        // context cannot see CommunityToolkit.Mvvm generated properties.
        var json = JsonSerializer.Serialize(settings, Indented);
        return JsonSerializer.Deserialize<TerminalSettings>(json, Indented)!;
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
}
