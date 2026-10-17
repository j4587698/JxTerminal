using System.Linq;
using JxTerminal.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
public class SavedConnectionTests
{
    [TestMethod]
    public void DisplayTarget_Ssh_IncludesUserAndHost()
    {
        var c = new SavedConnection { Host = "10.0.0.5", Port = 22, Username = "root" };
        Assert.AreEqual("root@10.0.0.5:22", c.DisplayTarget);
    }

    [TestMethod]
    public void DisplayTarget_Serial_UsesClassicNotation()
    {
        var c = new SavedConnection
        {
            Type = ConnectionType.Serial,
            SerialPortName = "COM3",
            BaudRate = 9600
        };
        Assert.AreEqual("COM3 @ 9600 8N1", c.DisplayTarget);
    }

    [TestMethod]
    public void Clone_CopiesAllFields()
    {
        var c = new SavedConnection
        {
            Name = "web1",
            Host = "web1.local",
            Username = "deploy",
            AuthenticationType = SshAuthenticationType.Key,
            KeyFilePath = "/home/me/id_ed25519"
        };

        var clone = c.Clone();

        Assert.AreEqual(c.Name, clone.Name);
        Assert.AreEqual(c.Host, clone.Host);
        Assert.AreEqual(c.Username, clone.Username);
        Assert.AreEqual(c.AuthenticationType, clone.AuthenticationType);
        Assert.AreEqual(c.KeyFilePath, clone.KeyFilePath);
        Assert.AreNotEqual(c.Id, clone.Id);

        var dup = c.Duplicate();
        Assert.AreEqual(c.Name + " (Copy)", dup.Name);
        Assert.AreNotEqual(c.Id, dup.Id);
    }

    [TestMethod]
    public void PingProperties_ComputeCorrectLevelsAndStatus()
    {
        var conn = new SavedConnection
        {
            Type = ConnectionType.Ssh,
            Host = "1.1.1.1",
            Port = 22
        };

        Assert.IsTrue(conn.CanPing);
        Assert.IsFalse(conn.HasPing);

        conn.PingStatus = PingStatus.Pinging;
        Assert.IsTrue(conn.HasPing);
        Assert.IsTrue(conn.IsPinging);
        Assert.AreEqual(PingLevel.Pinging, conn.PingLevel);
        Assert.AreEqual("...", conn.PingDisplay);

        conn.PingStatus = PingStatus.Success;
        conn.PingMs = 35;
        Assert.AreEqual(PingLevel.Fast, conn.PingLevel);
        Assert.AreEqual("35 ms", conn.PingDisplay);

        conn.PingMs = 150;
        Assert.AreEqual(PingLevel.Medium, conn.PingLevel);

        conn.PingMs = 320;
        Assert.AreEqual(PingLevel.Slow, conn.PingLevel);

        conn.PingStatus = PingStatus.Timeout;
        conn.PingMs = null;
        Assert.AreEqual(PingLevel.Timeout, conn.PingLevel);
    }

    [TestMethod]
    public void SerialAndLocal_CannotPing()
    {
        var local = new SavedConnection { Type = ConnectionType.Local };
        Assert.IsFalse(local.CanPing);

        var serial = new SavedConnection { Type = ConnectionType.Serial, SerialPortName = "COM1" };
        Assert.IsFalse(serial.CanPing);
    }
}

[TestClass]
public class TerminalSelectionCoordinateTests
{
    private static double ToRelRow(double absRow, int lineCount) => lineCount - 1 - absRow;
    private static double ToAbsRow(double relRow, int lineCount) => lineCount - 1 - relRow;

    private static (int startRow, int endRow, int startCol, int endCol) NormalizeSelection(
        int p1Col, double p1RelY, int p2Col, double p2RelY, int lineCount)
    {
        int r1 = (int)ToAbsRow(p1RelY, lineCount);
        int r2 = (int)ToAbsRow(p2RelY, lineCount);
        int c1 = p1Col;
        int c2 = p2Col;

        if (r1 > r2 || (r1 == r2 && c1 > c2))
        {
            (r1, r2) = (r2, r1);
            (c1, c2) = (c2, c1);
        }

        return (r1, r2, c1, c2);
    }

    [TestMethod]
    public void DragDown_NormalizesToTopToBottom()
    {
        const int lineCount = 100;
        // User clicks at row 5, col 10, and drags down to row 8, col 4
        double p1RelY = ToRelRow(5, lineCount);
        double p2RelY = ToRelRow(8, lineCount);

        var (startRow, endRow, startCol, endCol) = NormalizeSelection(10, p1RelY, 4, p2RelY, lineCount);

        Assert.AreEqual(5, startRow);
        Assert.AreEqual(8, endRow);
        Assert.AreEqual(10, startCol);
        Assert.AreEqual(4, endCol);
        Assert.IsTrue(startRow <= endRow);
    }

    [TestMethod]
    public void DragUp_NormalizesToTopToBottom()
    {
        const int lineCount = 100;
        // User clicks at row 8, col 4, and drags up to row 5, col 10
        double p1RelY = ToRelRow(8, lineCount);
        double p2RelY = ToRelRow(5, lineCount);

        var (startRow, endRow, startCol, endCol) = NormalizeSelection(4, p1RelY, 10, p2RelY, lineCount);

        Assert.AreEqual(5, startRow);
        Assert.AreEqual(8, endRow);
        Assert.AreEqual(10, startCol);
        Assert.AreEqual(4, endCol);
        Assert.IsTrue(startRow <= endRow);
    }

    [TestMethod]
    public void SingleLine_DragLeftToRight_Preserved()
    {
        const int lineCount = 100;
        double relY = ToRelRow(3, lineCount);

        var (startRow, endRow, startCol, endCol) = NormalizeSelection(2, relY, 15, relY, lineCount);

        Assert.AreEqual(3, startRow);
        Assert.AreEqual(3, endRow);
        Assert.AreEqual(2, startCol);
        Assert.AreEqual(15, endCol);
    }

    [TestMethod]
    public void SingleLine_DragRightToLeft_Inverted()
    {
        const int lineCount = 100;
        double relY = ToRelRow(3, lineCount);

        var (startRow, endRow, startCol, endCol) = NormalizeSelection(15, relY, 2, relY, lineCount);

        Assert.AreEqual(3, startRow);
        Assert.AreEqual(3, endRow);
        Assert.AreEqual(2, startCol);
        Assert.AreEqual(15, endCol);
    }
}


