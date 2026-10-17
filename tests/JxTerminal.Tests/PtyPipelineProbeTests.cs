using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JxTerminal.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
public class PtyPipelineProbeTests
{
    [TestMethod]
    public async Task LocalShell_EmitsPromptOutput()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("PTY probe only runs on Windows");
            return;
        }

        var session = new PtyLocalShellSession("Cmd");
        var received = new StringBuilder();
        session.DataReceived += s => { lock (received) received.Append(s); };

        await session.ConnectAsync();
        Assert.IsTrue(session.IsConnected, "PTY session should connect");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            lock (received)
            {
                if (received.Length > 0) break;
            }
            await Task.Delay(200);
        }

        string text;
        lock (received) text = received.ToString();

        session.Dispose();
        Assert.IsTrue(text.Length > 0, "PTY produced no output within 10s");
    }
}
