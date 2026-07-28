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
        Parity parity = Parity.None, StopBits stopBits = StopBits.One, Handshake handshake = Handshake.None)
    {
        PortName = portName;
        BaudRate = baudRate;
        DataBits = dataBits;
        Parity = parity;
        StopBits = stopBits;
        Handshake = handshake;
    }

    public async Task ConnectAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                _serialPort = new SerialPort(PortName, BaudRate, Parity, DataBits, StopBits)
                {
                    Encoding = Encoding.UTF8, // Maybe configurable?
                    Handshake = Handshake,
                    DtrEnable = Handshake != Handshake.RequestToSend && Handshake != Handshake.RequestToSendXOnXOff,
                    RtsEnable = Handshake != Handshake.RequestToSend && Handshake != Handshake.RequestToSendXOnXOff
                };
                
                _serialPort.Open();
                _cts = new CancellationTokenSource();

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
            var stream = _serialPort.BaseStream;

            while (_serialPort.IsOpen && !_cts!.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                if (read > 0)
                {
                    string data = Encoding.UTF8.GetString(buffer, 0, read);
                    DataReceived?.Invoke(data);
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
        if (_serialPort != null && _serialPort.IsOpen)
        {
            try { _serialPort.Close(); } catch { }
        }
        _serialPort?.Dispose();
        Disconnected?.Invoke();
    }
}
