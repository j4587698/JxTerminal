using System;
using System.IO;
using System.Runtime.InteropServices;
using JxTerminal.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

// Shell resolution is platform-specific; assertions run only where they make sense.
[TestClass]
public class PtyLocalShellSessionTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    [TestMethod]
    public void Auto_Windows_ReturnsPowerShell()
    {
        if (!IsWindows) return;

        var shell = PtyLocalShellSession.ResolveShell("Auto");

        Assert.IsTrue(File.Exists(shell), $"resolved shell should exist: {shell}");
        Assert.IsTrue(shell.Contains("powershell", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Cmd_ExplicitRequest_ReturnsCmd()
    {
        if (!IsWindows) return;

        var shell = PtyLocalShellSession.ResolveShell("Cmd");

        Assert.IsTrue(shell.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Wsl_Request_ReturnsWslWhenPresent()
    {
        if (!IsWindows) return;
        var wsl = Path.Combine(Environment.SystemDirectory, "wsl.exe");
        if (!File.Exists(wsl)) return; // wsl optional on runners

        Assert.AreEqual(wsl, PtyLocalShellSession.ResolveShell("Wsl"));
    }

    [TestMethod]
    public void ExplicitPath_PassedThroughWhenExists()
    {
        if (!IsWindows) return;

        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        Assert.AreEqual(cmd, PtyLocalShellSession.ResolveShell(cmd));
    }

    [TestMethod]
    public void UnknownPreset_FallsBackToDefault()
    {
        var shell = PtyLocalShellSession.ResolveShell("NoSuchShell");
        Assert.IsTrue(File.Exists(shell));
    }
}
