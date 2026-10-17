using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace JxTerminal.Services.CommandLine;

/// <summary>
/// Provides single-instance IPC communication via Named Pipes so that secondary command-line
/// launches (e.g. from bastion hosts or terminal shortcuts) can open as new tabs in the existing window.
/// </summary>
public static class SingleInstanceService
{
    private static CancellationTokenSource? _serverCts;

    /// <summary>
    /// Unique pipe name scoped to current user session.
    /// </summary>
    public static string PipeName { get; } = $"JxTerminal_IPC_{Environment.UserName.Replace('\\', '_')}";

    /// <summary>
    /// Tries to send command-line arguments to an existing running JxTerminal instance.
    /// Returns true if an existing instance accepted the arguments; false if no instance was running.
    /// </summary>
    public static async Task<bool> TrySendArgsToExistingInstanceAsync(string[] args, int timeoutMs = 800)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            using var cts = new CancellationTokenSource(timeoutMs);

            await client.ConnectAsync(cts.Token).ConfigureAwait(false);

            using var writer = new BinaryWriter(client, Encoding.UTF8, leaveOpen: true);
            writer.Write(args.Length);
            foreach (var arg in args)
            {
                writer.Write(arg ?? "");
            }
            writer.Flush();

            return true;
        }
        catch
        {
            // Server not running or timed out
            return false;
        }
    }

    /// <summary>
    /// Starts background Named Pipe server listening for arguments from subsequent launches.
    /// </summary>
    public static void StartServer(Action<string[]> onArgumentsReceived)
    {
        StopServer();

        _serverCts = new CancellationTokenSource();
        var token = _serverCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                    using var reader = new BinaryReader(server, Encoding.UTF8, leaveOpen: true);
                    int count = reader.ReadInt32();
                    if (count is >= 0 and <= 1000)
                    {
                        var args = new string[count];
                        for (int i = 0; i < count; i++)
                        {
                            args[i] = reader.ReadString();
                        }

                        onArgumentsReceived(args);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"SingleInstance server connection error: {ex.Message}");
                    // Small delay to prevent tight loop on persistent pipe errors
                    try { await Task.Delay(250, token).ConfigureAwait(false); } catch { break; }
                }
            }
        }, token);
    }

    /// <summary>
    /// Stops the pipe server if running.
    /// </summary>
    public static void StopServer()
    {
        var old = Interlocked.Exchange(ref _serverCts, null);
        if (old != null)
        {
            try { old.Cancel(); } catch { }
            old.Dispose();
        }
    }

    /// <summary>
    /// Brings the specified Avalonia window to the foreground and restores if minimized.
    /// </summary>
    public static void BringWindowToFront(Window window)
    {
        if (window == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (window.WindowState == WindowState.Minimized)
                {
                    window.WindowState = WindowState.Normal;
                }

                window.Show();
                window.Activate();

                if (OperatingSystem.IsWindows())
                {
                    var handle = window.TryGetPlatformHandle()?.Handle;
                    if (handle.HasValue && handle.Value != IntPtr.Zero)
                    {
                        ShowWindow(handle.Value, 9); // SW_RESTORE
                        SetForegroundWindow(handle.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Failed to bring window to front: {ex.Message}");
            }
        });
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
