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
    public void SplitAcrossChunks_DoesNotThrowOrCorrupt()
    {
        var t = new AnsiModeTracker();

        // Sequence split across two chunks: the parser must bail out safely.
        t.Process("\x1b[?100");
        t.Process("6h");

        // 1006 was never delivered as a complete sequence.
        Assert.IsFalse(t.SgrMouse);
    }

    [TestMethod]
    public void DeviceAttributes_WithQuestionMarkParams_DoNotTouchModes()
    {
        // Primary device attributes: ESC [ ? 6 c — ends with 'c', not h/l, so nothing changes.
        var t = new AnsiModeTracker();
        t.Process("\x1b[?6c");
        Assert.IsFalse(t.MouseTracking);
    }
}
