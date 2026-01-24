using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace JxTerminal.Models;

public class LocalShellSession : ITerminalSession
{
    private Process? _process;
    private bool _isConnected;

    public event Action<string>? DataReceived;
    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<Exception>? ErrorOccurred;

    public bool IsConnected => _isConnected;

    public async Task ConnectAsync()
    {
        Console.WriteLine("DEBUG: LocalShellSession.ConnectAsync started");
        await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-i",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                psi.EnvironmentVariables["TERM"] = "xterm-256color";

                _process = new Process { StartInfo = psi };
                _process.Start();

                _isConnected = true;
                Connected?.Invoke();

                // Read Output
                Task.Run(async () =>
                {
                    var buffer = new char[4096];
                    while (_process != null && !_process.HasExited)
                    {
                        try
                        {
                            int read = await _process.StandardOutput.ReadAsync(buffer, 0, buffer.Length);
                            if (read > 0)
                            {
                                var text = new string(buffer, 0, read);
                                Console.WriteLine($"DEBUG: Shell Out (Raw): {text.Replace("\n", "\\n").Replace("\r", "\\r")}");
                                DataReceived?.Invoke(text);
                            }
                            else await Task.Delay(10);
                        }
                        catch { break; }
                    }
                });

                // Read Error
                Task.Run(async () =>
                {
                    var buffer = new char[4096];
                    while (_process != null && !_process.HasExited)
                    {
                        try
                        {
                            int read = await _process.StandardError.ReadAsync(buffer, 0, buffer.Length);
                            if (read > 0)
                            {
                                var text = new string(buffer, 0, read);
                                Console.WriteLine($"DEBUG: Shell Err (Raw): {text.Replace("\n", "\\n").Replace("\r", "\\r")}");
                                DataReceived?.Invoke(text);
                            }
                            else await Task.Delay(10);
                        }
                        catch { break; }
                    }
                });

                _process.WaitForExitAsync().ContinueWith(t =>
                {
                    _isConnected = false;
                    Disconnected?.Invoke();
                });
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
            }
        });
    }

    public void Send(string data)
    {
        if (_process != null && !_process.HasExited)
        {
            try
            {
                // Basic redirection doesn't handle single char input well like 'ls<tab>'
                // It expects full lines usually for StandardInput write unless application handles it.
                // But we just write stream.
                _process.StandardInput.Write(data);
                // Flush is automatic on newline usually, but let's try manual flush if needed?
                // StreamWriter AutoFlush is true by default? No.
                _process.StandardInput.Flush();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
            }
        }
    }

    public void Resize(int rows, int cols, int width, int height)
    {
        // For local shell without PTY, try to sync size using stty command
        // This works for some interactive shells (like bash/zsh)
        // We send it to stdin, hoping the shell processes it.
        // It consumes visual space but is better than wrong wrap.
        Send($"stty cols {cols} rows {rows}\n");
    }

    public void Dispose()
    {
        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
            }
            _process?.Dispose();
        }
        catch { }
        _isConnected = false;
        Disconnected?.Invoke();
    }
}
