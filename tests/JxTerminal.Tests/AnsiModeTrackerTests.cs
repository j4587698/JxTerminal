using JxTerminal.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
public class AnsiModeTrackerTests
{
    [TestMethod]
    public void MouseMode_TurnsOnAndOff()
    {
        var t = new AnsiModeTracker();
        Assert.IsFalse(t.MouseTracking);

        t.Process("\x1b[?1000h");
        Assert.IsTrue(t.MouseNormal);
        Assert.IsTrue(t.MouseTracking);

        t.Process("\x1b[?1000l");
        Assert.IsFalse(t.MouseNormal);
        Assert.IsFalse(t.MouseTracking);
    }

    [TestMethod]
    public void CompoundSequence_AppliesAllParams()
    {
        var t = new AnsiModeTracker();
        t.Process("\x1b[?1000;1006h");

        Assert.IsTrue(t.MouseNormal);
        Assert.IsTrue(t.SgrMouse);

        t.Process("\x1b[?1002h");
        Assert.IsTrue(t.MouseButtonEvent);
    }

    [TestMethod]
    public void BracketedPaste_TracksState()
    {
        var t = new AnsiModeTracker();
        t.Process("\x1b[?2004h");
        Assert.IsTrue(t.BracketedPaste);

        t.Process("\x1b[?2004l");
        Assert.IsFalse(t.BracketedPaste);
    }

    [TestMethod]
    public void ApplicationCursorKeys_Tracked()
    {
        var t = new AnsiModeTracker();
        t.Process("\x1b[?1h\x1b[?1006h");
        Assert.IsTrue(t.ApplicationCursorKeys);
        Assert.IsTrue(t.SgrMouse);

        t.Process("\x1b[?1l");
        Assert.IsFalse(t.ApplicationCursorKeys);
    }

    [TestMethod]
    public void NonPrivateCsi_Ignored()
    {
        var t = new AnsiModeTracker();
        t.Process("\x1b[2J\x1b[H\x1b[31mtext");
        Assert.IsFalse(t.MouseTracking);
        Assert.IsFalse(t.BracketedPaste);
        Assert.IsFalse(t.ApplicationCursorKeys);
    }

    [TestMethod]
    public void SplitAcrossChunks_CompletedByNextChunk()
    {
        var t = new AnsiModeTracker();

        // Sequence split across two chunks: the parser buffers the incomplete tail
        // and applies the mode once the remainder arrives.
        t.Process("\x1b[?100");
        Assert.IsFalse(t.SgrMouse); // not applied yet
        t.Process("6h");

        Assert.IsTrue(t.SgrMouse);
    }

    [TestMethod]
    public void EscAtChunkEnd_WaitsForNextChunk()
    {
        var t = new AnsiModeTracker();

        t.Process("text\x1b");
        t.Process("[?2004h");
        Assert.IsTrue(t.BracketedPaste);
    }

    [TestMethod]
    public void DeviceAttributes_WithQuestionMarkParams_DoNotTouchModes()
    {
        // Primary device attributes: ESC [ ? 6 c — ends with 'c', not h/l, so nothing changes.
        var t = new AnsiModeTracker();
        t.Process("\x1b[?6c");
        Assert.IsFalse(t.MouseTracking);
    }

    [TestMethod]
    public void Osc7_ExtractsCurrentDirectory_WithBel()
    {
        var t = new AnsiModeTracker();
        string? notifiedPath = null;
        t.CurrentDirectoryChanged += p => notifiedPath = p;

        t.Process("\x1b]7;file://my-debian-host/home/user/workspace\x07");

        Assert.AreEqual("/home/user/workspace", t.CurrentDirectory);
        Assert.AreEqual("/home/user/workspace", notifiedPath);
    }

    [TestMethod]
    public void Osc7_ExtractsCurrentDirectory_WithStAndUrlEncoding()
    {
        var t = new AnsiModeTracker();
        string? notifiedPath = null;
        t.CurrentDirectoryChanged += p => notifiedPath = p;

        t.Process("\x1b]7;file:///var/log/my%20app%20dir\x1b\\");

        Assert.AreEqual("/var/log/my app dir", t.CurrentDirectory);
        Assert.AreEqual("/var/log/my app dir", notifiedPath);
    }

    [TestMethod]
    public void Osc1337_CurrentDir_ExtractsDirectory()
    {
        var t = new AnsiModeTracker();
        string? notifiedPath = null;
        t.CurrentDirectoryChanged += p => notifiedPath = p;

        t.Process("\x1b]1337;CurrentDir=/usr/local/bin\x07");

        Assert.AreEqual("/usr/local/bin", t.CurrentDirectory);
        Assert.AreEqual("/usr/local/bin", notifiedPath);
    }

    [TestMethod]
    public void Osc9_9_ExtractsDirectory()
    {
        var t = new AnsiModeTracker();
        string? notifiedPath = null;
        t.CurrentDirectoryChanged += p => notifiedPath = p;

        t.Process("\x1b]9;9;\"/etc/nginx\"\x07");

        Assert.AreEqual("/etc/nginx", t.CurrentDirectory);
        Assert.AreEqual("/etc/nginx", notifiedPath);
    }

    [TestMethod]
    public void Osc7_SplitAcrossChunks_CompletesAndNotifies()
    {
        var t = new AnsiModeTracker();
        string? notifiedPath = null;
        t.CurrentDirectoryChanged += p => notifiedPath = p;

        t.Process("\x1b]7;file://host/var/l");
        Assert.AreEqual("", t.CurrentDirectory);
        Assert.IsNull(notifiedPath);

        t.Process("og\x07");
        Assert.AreEqual("/var/log", t.CurrentDirectory);
        Assert.AreEqual("/var/log", notifiedPath);
    }
}
