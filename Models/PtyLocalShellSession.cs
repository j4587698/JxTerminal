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
    private readonly string _shellKind;
    private IPtyConnection? _pty;
    private bool _isConnected;
    private CancellationTokenSource? _readCts;
    // Stateful so multi-byte characters split across reads survive.
    private readonly Decoder _decoder;
    private readonly Encoding _encoding;
    // Guards against raising Disconnected more than once per connection lifetime.
    private int _disconnectedInvoked;

    // Window dimensions (persists across connect lifecycle so SpawnAsync uses the actual size)
    private int _cols = 80;
    private int _rows = 24;
    private int _width = 800;
    private int _height = 600;

    public event Action<string>? DataReceived;
    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<Exception>? ErrorOccurred;

    public bool IsConnected => _isConnected;

    private readonly object _writeLock = new();

    public PtyLocalShellSession(string shellKind = "Auto")
    {
        _shellKind = shellKind;
        _encoding = ResolveLocalEncoding();
        _decoder = _encoding.GetDecoder();
    }

    /// <summary>
    /// Modern ConPTY (Windows 10/11) and Unix PTYs emit and consume UTF-8 across
    /// the pseudo-console VT pipe.
    /// </summary>
    private static Encoding ResolveLocalEncoding() => new UTF8Encoding(false);

    /// <summary>
    /// Resolves the requested shell kind to a platform executable.
    /// Accepts preset names (Auto/PowerShell/Cmd/Wsl/Bash) or an explicit path.
    /// Falls back to the platform default when the request cannot be honoured.
    /// </summary>
    internal static string ResolveShell(string shellKind)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var kind = (shellKind ?? "Auto").Trim();

        // Explicit path from a saved connection: use it verbatim when it exists.
        if (kind.Contains('/') || kind.Contains('\\') || kind.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return File.Exists(kind) ? kind : DefaultShell(isWindows);
        }

        switch (kind)
        {
            case "PowerShell" when isWindows:
            {
                var pwsh = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "PowerShell", "7", "pwsh.exe");
                return File.Exists(pwsh)
                    ? pwsh
                    : Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            }
            case "Cmd" when isWindows:
                return Path.Combine(Environment.SystemDirectory, "cmd.exe");
            case "Wsl" when isWindows && File.Exists(Path.Combine(Environment.SystemDirectory, "wsl.exe")):
                return Path.Combine(Environment.SystemDirectory, "wsl.exe");
            case "Bash" when !isWindows:
                return "/bin/bash";
        }

        return DefaultShell(isWindows);
    }

    private static string DefaultShell(bool isWindows)
        => isWindows
            ? Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")
            : Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } sh ? sh : "/bin/bash";

    public async Task ConnectAsync()
    {
        try
        {
            var options = new PtyOptions
            {
                Name = $"JxTerminal-{Guid.NewGuid()}",
                Cols = _cols > 0 ? _cols : 80,
                Rows = _rows > 0 ? _rows : 24,
                Cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                App = ResolveShell(_shellKind),
                Environment = new Dictionary<string, string>
                {
                    { "TERM", "xterm-256color" },
                    { "COLORTERM", "truecolor" }
                }
            };

            _pty = await PtyProvider.SpawnAsync(options, CancellationToken.None);
            _isConnected = true;
            Interlocked.Exchange(ref _disconnectedInvoked, 0);

            try { _pty.Resize(_cols > 0 ? _cols : 80, _rows > 0 ? _rows : 24); } catch { }

            // Handle process exit
            _pty.ProcessExited += (sender, e) =>
            {
                FireDisconnectedOnce();
            };

            Connected?.Invoke();

            // Start reading output
            _readCts = new CancellationTokenSource();
            _ = ReadOutputAsync(_readCts);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex);
        }
    }

    private async Task ReadOutputAsync(CancellationTokenSource cts)
    {
        var cancellationToken = cts.Token;
        var buffer = new byte[4096];
        try
        {
            while (!cancellationToken.IsCancellationRequested && _pty != null && _isConnected)
            {
                int bytesRead = await _pty.ReaderStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead > 0)
                {
                    // Stateful decode keeps UTF-8 sequences that span read boundaries intact.
                    var chars = new char[_decoder.GetCharCount(buffer, 0, bytesRead, false)];
                    int count = _decoder.GetChars(buffer, 0, bytesRead, chars, 0, false);
                    if (count > 0) DataReceived?.Invoke(new string(chars, 0, count));
                }
                else if (bytesRead == 0)
                {
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
            FireDisconnectedOnce();
            // The loop has fully unwound, so no token registration can still be
            // in flight; safe to release the CTS here (Dispose only cancels).
            cts.Dispose();
        }
    }

    private void FireDisconnectedOnce()
    {
        if (Interlocked.Exchange(ref _disconnectedInvoked, 1) == 0)
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
                byte[] bytes = _encoding.GetBytes(data);
                lock (_writeLock)
                {
                    _pty.WriterStream.Write(bytes, 0, bytes.Length);
                    _pty.WriterStream.Flush();
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
            }
        }
    }

    public void Resize(int rows, int cols, int width, int height)
    {
        if (cols <= 0 || rows <= 0) return;
        _cols = cols;
        _rows = rows;
        if (width > 0) _width = width;
        if (height > 0) _height = height;

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
        // Only cancel here: the token may still be registered by an in-flight ReadAsync;
        // the CTS itself is disposed by ReadOutputAsync once its loop has unwound.
        try { _readCts?.Cancel(); } catch (ObjectDisposedException) { }

        try
        {
            _pty?.Kill();
            _pty?.Dispose();
        }
        catch { }

        FireDisconnectedOnce();
    }
}
