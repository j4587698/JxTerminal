using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JxTerminal.Models;

public class TelnetSession : ITerminalSession
{
    public string Host { get; }
    public int Port { get; }

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;

    public event Action<string>? DataReceived;
    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<Exception>? ErrorOccurred;

    public bool IsConnected => _client?.Connected ?? false;

    public TelnetSession(string host, int port)
    {
        Host = host;
        Port = port;
    }

    public async Task ConnectAsync()
    {
        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(Host, Port);
            _stream = _client.GetStream();
            _cts = new CancellationTokenSource();

            Connected?.Invoke();

            // Start reading loop
            _ = ReadLoop();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex);
        }
    }

    private async Task ReadLoop()
    {
        try
        {
            var buffer = new byte[4096];
            while (_client != null && _client.Connected && _stream != null && !_cts!.IsCancellationRequested)
            {
                int read = await _stream.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                if (read == 0) break; // Disconnected

                // Minimal Telnet Protocol Handling (Stripping IAC commands ideally, but raw for now)
                // TODO: Implement Telnet protocol command filtering if raw text is garbled.
                // For basic usage with many devices, raw is handled by the terminal emulator to some extent, 
                // but real Telnet negotiation (IAC DO/DONT/WILL/WONT) should be handled.
                // For this iteration, we pass raw data and let the terminal emulator handle character display,
                // but we might see garbage.
                
                string data = Encoding.ASCII.GetString(buffer, 0, read);
                DataReceived?.Invoke(data);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // ErrorOccurred?.Invoke(ex); // Maybe just disconnect
        }
        finally
        {
            // Disconnect
            Dispose();
        }
    }

    public void Send(string data)
    {
        if (_stream != null && IsConnected)
        {
            try
            {
                byte[] bytes = Encoding.ASCII.GetBytes(data);
                _stream.Write(bytes, 0, bytes.Length);
                _stream.Flush();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
            }
        }
    }

    public void Resize(int rows, int cols, int width, int height)
    {
        // Telnet RFC 1073 (NAWS) would be needed here.
        // Sending IAC SB NAWS ...
        if (_stream != null && IsConnected) 
        {
             // Not implemented for basic TCP/Telnet yet.
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _stream?.Dispose();
        _client?.Dispose();
        _client = null;
        Disconnected?.Invoke();
    }
}
