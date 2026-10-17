using System;
using System.Reflection;
using System.Threading.Tasks;
using JxTerminal.Models;
using JxTerminal.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
[DoNotParallelize]
public class TerminalWindowResizeTests
{
    private class FakeTerminalSession : ITerminalSession
    {
#pragma warning disable CS0067
        public event Action<string>? DataReceived;
        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<Exception>? ErrorOccurred;
#pragma warning restore CS0067

        public bool IsConnected { get; set; }
        public int LastRows { get; private set; }
        public int LastCols { get; private set; }
        public int LastWidth { get; private set; }
        public int LastHeight { get; private set; }
        public int ResizeCallCount { get; private set; }

        public Task ConnectAsync()
        {
            // Do not fire Connected synchronously to simulate real async network handshake
            return Task.CompletedTask;
        }

        public void Send(string data) { }

        public void Resize(int rows, int cols, int width, int height)
        {
            LastRows = rows;
            LastCols = cols;
            LastWidth = width;
            LastHeight = height;
            ResizeCallCount++;
        }

        public void FireConnected()
        {
            IsConnected = true;
            Connected?.Invoke();
        }

        public void Dispose() { }
    }

    [TestMethod]
    public void TerminalViewModel_Resize_ForwardsToSession_EvenWhenDisconnected()
    {
        var session = new FakeTerminalSession { IsConnected = false };
        var settings = new TerminalSettings();
        using var vm = new TerminalViewModel(session, settings);

        // Simulate control layout pass before connection finishes
        vm.Resize(35, 120, 1024, 768);

        Assert.AreEqual(1, session.ResizeCallCount);
        Assert.AreEqual(35, session.LastRows);
        Assert.AreEqual(120, session.LastCols);
        Assert.AreEqual(1024, session.LastWidth);
        Assert.AreEqual(768, session.LastHeight);
    }

    [TestMethod]
    public void TerminalViewModel_OnConnected_SynchronizesActualTerminalDimensions()
    {
        var session = new FakeTerminalSession { IsConnected = false };
        var settings = new TerminalSettings();
        using var vm = new TerminalViewModel(session, settings);

        // Initial buffer resize from UI control
        vm.Terminal.Resize(150, 45);

        // When connection succeeds, OnConnected must sync actual terminal rows/cols to session
        session.FireConnected();
        try { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); } catch { }

        Assert.AreEqual(45, session.LastRows);
        Assert.AreEqual(150, session.LastCols);
    }

    [TestMethod]
    public void SshSession_Resize_CachesDimensionsBeforeConnect()
    {
        var ssh = new SshSession("127.0.0.1", 22, "root", "password");

        ssh.Resize(40, 132, 1200, 800);

        var colsField = typeof(SshSession).GetField("_cols", BindingFlags.NonPublic | BindingFlags.Instance);
        var rowsField = typeof(SshSession).GetField("_rows", BindingFlags.NonPublic | BindingFlags.Instance);
        var widthField = typeof(SshSession).GetField("_width", BindingFlags.NonPublic | BindingFlags.Instance);
        var heightField = typeof(SshSession).GetField("_height", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.IsNotNull(colsField);
        Assert.IsNotNull(rowsField);
        Assert.AreEqual(132, (int)colsField.GetValue(ssh)!);
        Assert.AreEqual(40, (int)rowsField.GetValue(ssh)!);
        Assert.AreEqual(1200, (int)widthField!.GetValue(ssh)!);
        Assert.AreEqual(800, (int)heightField!.GetValue(ssh)!);
    }

    [TestMethod]
    public void PtyLocalShellSession_Resize_CachesDimensionsBeforeConnect()
    {
        var pty = new PtyLocalShellSession("Cmd");

        pty.Resize(50, 160, 1400, 900);

        var colsField = typeof(PtyLocalShellSession).GetField("_cols", BindingFlags.NonPublic | BindingFlags.Instance);
        var rowsField = typeof(PtyLocalShellSession).GetField("_rows", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.IsNotNull(colsField);
        Assert.IsNotNull(rowsField);
        Assert.AreEqual(160, (int)colsField.GetValue(pty)!);
        Assert.AreEqual(50, (int)rowsField.GetValue(pty)!);
    }
}
