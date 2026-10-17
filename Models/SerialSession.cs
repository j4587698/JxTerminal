using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JxTerminal.Models;

public class SerialSession : ITerminalSession
{
    public string PortName { get; }
    public int BaudRate { get; }
    public int DataBits { get; }
    public Parity Parity { get; }
    public StopBits StopBits { get; }
    public Handshake Handshake { get; }

    private SerialPort? _serialPort;
    private CancellationTokenSource? _cts;

    public event Action<string>? DataReceived;
    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<Exception>? ErrorOccurred;

    public bool IsConnected => _serialPort?.IsOpen ?? false;

    public SerialSession(string portName, int baudRate, int dataBits = 8,
        Parity parity = Parity.None, StopBits stopBits = StopBits.One, Handshake handshake = Handshake.None,
        string encodingName = "UTF-8")
    {
        PortName = portName;
        BaudRate = baudRate;
        DataBits = dataBits;
        Parity = parity;
        StopBits = stopBits;
        Handshake = handshake;
        try { _encoding = Encoding.GetEncoding(string.IsNullOrWhiteSpace(encodingName) ? "UTF-8" : encodingName); }
        catch (ArgumentException) { _encoding = Encoding.UTF8; }
        _decoder = _encoding.GetDecoder();
    }

    private readonly Encoding _encoding;
    // Stateful decoder: honors the configured encoding and survives multi-byte
    // characters split across reads.
    private readonly Decoder _decoder;
    // Guards against raising Disconnected more than once per connection lifetime.
    private int _disconnectedInvoked;

    public async Task ConnectAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                _serialPort = new SerialPort(PortName, BaudRate, Parity, DataBits, StopBits)
                {
                    Encoding = _encoding,
                    Handshake = Handshake,
                    DtrEnable = Handshake != Handshake.RequestToSend && Handshake != Handshake.RequestToSendXOnXOff,
                    RtsEnable = Handshake != Handshake.RequestToSend && Handshake != Handshake.RequestToSendXOnXOff
                };
                
                _serialPort.Open();
                _cts = new CancellationTokenSource();
                Interlocked.Exchange(ref _disconnectedInvoked, 0);

                Connected?.Invoke();
                
                // Start read loop
                _ = ReadLoop();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
            }
        });
    }

    private async Task ReadLoop()
    {
        try
        {
            // System.IO.Ports doesn't have Async Read properly for streams in some versions/implementations 
            // but BaseStream.ReadAsync should work.
            if (_serialPort == null) return;

            var buffer = new byte[4096];
            var chars = new char[_encoding.GetMaxCharCount(buffer.Length)];
            var stream = _serialPort.BaseStream;

            while (_serialPort.IsOpen && !_cts!.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                if (read > 0)
                {
                    // Decode with the session's configured encoding (not hardcoded UTF-8);
                    // flush:false keeps a trailing partial multi-byte sequence for the
                    // next read instead of emitting replacement characters.
                    int count = _decoder.GetChars(buffer, 0, read, chars, 0, false);
                    if (count > 0)
                        DataReceived?.Invoke(new string(chars, 0, count));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (IsConnected) ErrorOccurred?.Invoke(ex);
        }
        finally
        {
            Dispose();
        }
    }

    public void Send(string data)
    {
        if (_serialPort != null && IsConnected)
        {
            try
            {
                _serialPort.Write(data);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
            }
        }
    }

    public void Resize(int rows, int cols, int width, int height)
    {
        // Serial ports don't really support resizing window via protocol
    }

    public void Dispose()
    {
        _cts?.Cancel();
        var port = Interlocked.Exchange(ref _serialPort, null);
        if (port != null)
        {
            try { if (port.IsOpen) port.Close(); } catch { }
            port.Dispose();
        }
        // The CTS outlives the loop teardown; it is replaced on reconnect.
        if (Interlocked.Exchange(ref _disconnectedInvoked, 1) == 0)
            Disconnected?.Invoke();
    }
}
