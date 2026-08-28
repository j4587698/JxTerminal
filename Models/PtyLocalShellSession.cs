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
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

    public event Action<string>? DataReceived;
    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<Exception>? ErrorOccurred;

    public bool IsConnected => _isConnected;

    public PtyLocalShellSession(string shellKind = "Auto")
    {
        _shellKind = shellKind;
    }

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
                Cols = 80,
                Rows = 24,
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

            // Handle process exit
            _pty.ProcessExited += (sender, e) =>
            {
                if (!_isConnected) return;
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
            if (_isConnected)
            {
                _isConnected = false;
                Disconnected?.Invoke();
            }
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

        if (_isConnected)
        {
            _isConnected = false;
            Disconnected?.Invoke();
        }
    }
}
