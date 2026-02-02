using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Porta.Pty;

namespace JxTerminal.Models;

/// <summary>
/// Local shell session using PTY for proper terminal emulation.
/// </summary>
public class PtyLocalShellSession : ITerminalSession
{
    private IPtyConnection? _pty;
    private bool _isConnected;
    private CancellationTokenSource? _readCts;

    public event Action<string>? DataReceived;
    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<Exception>? ErrorOccurred;

    public bool IsConnected => _isConnected;

    public async Task ConnectAsync()
    {
        try
        {
            // Determine shell based on OS
            string shell = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(Environment.SystemDirectory, "cmd.exe")
                : "/bin/bash";

            var options = new PtyOptions
            {
                Name = $"JxTerminal-{Guid.NewGuid()}",
                Cols = 80,
                Rows = 24,
                Cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                App = shell,
                Environment = new Dictionary<string, string>
                {
                    { "TERM", "xterm-256color" },
                    { "COLORTERM", "truecolor" }
                }
            };

            _pty = await PtyProvider.SpawnAsync(options, CancellationToken.None);
            _isConnected = true;

            // Handle process exit
            _pty.ProcessExited += (sender, e) =>
            {
                _isConnected = false;
                Disconnected?.Invoke();
            };

            Connected?.Invoke();

            // Start reading output
            _readCts = new CancellationTokenSource();
            _ = ReadOutputAsync(_readCts.Token);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex);
        }
    }

    private async Task ReadOutputAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        try
        {
            while (!cancellationToken.IsCancellationRequested && _pty != null && _isConnected)
            {
                int bytesRead = await _pty.ReaderStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead > 0)
                {
                    string text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    DataReceived?.Invoke(text);
                }
                else if (bytesRead == 0)
                {
                    // End of stream
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            if (_isConnected)
            {
                ErrorOccurred?.Invoke(ex);
            }
        }
        finally
        {
            _isConnected = false;
            Disconnected?.Invoke();
        }
    }

    public void Send(string data)
    {
        if (_pty != null && _isConnected)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(data);
                _pty.WriterStream.Write(bytes, 0, bytes.Length);
                _pty.WriterStream.Flush();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
            }
        }
    }

    public void Resize(int rows, int cols, int width, int height)
    {
        if (_pty != null && _isConnected)
        {
            try
            {
                _pty.Resize(cols, rows);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
            }
        }
    }

    public void Dispose()
    {
        _readCts?.Cancel();
        _readCts?.Dispose();

        try
        {
            _pty?.Kill();
            _pty?.Dispose();
        }
        catch { }

        _isConnected = false;
        Disconnected?.Invoke();
    }
}
