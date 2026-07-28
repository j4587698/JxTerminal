using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JxTerminal.Models;

/// <summary>
/// Telnet client with RFC 854 option negotiation, RFC 1091 terminal type and RFC 1073 window
/// size (NAWS) support. Control sequences are stripped from the stream before the data reaches
/// the terminal emulator.
/// </summary>
public class TelnetSession : ITerminalSession
{
    // RFC 854 commands
    private const byte IAC = 255;
    private const byte DONT = 254;
    private const byte DO = 253;
    private const byte WONT = 252;
    private const byte WILL = 251;
    private const byte SB = 250;
    private const byte SE = 240;

    // Options
    private const byte OptEcho = 1;
    private const byte OptSuppressGoAhead = 3;
    private const byte OptTerminalType = 24;
    private const byte OptNaws = 31;

    private const byte TerminalTypeIs = 0;
    private const byte TerminalTypeSend = 1;

    private const string TerminalTypeName = "xterm-256color";

    private enum ParseState
    {
        Data,
        Iac,
        Option,
        Subnegotiation,
        SubnegotiationIac
    }

    public string Host { get; }
    public int Port { get; }

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;

    private ParseState _state = ParseState.Data;
    private byte _pendingCommand;
    private readonly List<byte> _subnegotiation = new();
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private bool _nawsEnabled;
    private int _cols = 80;
    private int _rows = 24;

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

            // Announce the options we are willing to support up front; servers that do not care
            // will simply refuse them.
            SendCommand(WILL, OptTerminalType);
            SendCommand(WILL, OptNaws);
            SendCommand(DO, OptSuppressGoAhead);
            SendCommand(DO, OptEcho);

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
            var payload = new List<byte>(4096);

            while (_client != null && _client.Connected && _stream != null && !_cts!.IsCancellationRequested)
            {
                int read = await _stream.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                if (read == 0) break; // Disconnected

                payload.Clear();
                ProcessIncoming(buffer, read, payload);

                if (payload.Count > 0)
                {
                    var bytes = payload.ToArray();
                    // Use a stateful decoder so multi-byte characters split across reads survive.
                    var chars = new char[_decoder.GetCharCount(bytes, 0, bytes.Length, false)];
                    int count = _decoder.GetChars(bytes, 0, bytes.Length, chars, 0, false);
                    if (count > 0) DataReceived?.Invoke(new string(chars, 0, count));
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

    /// <summary>
    /// Runs the Telnet state machine, appending user-visible bytes to <paramref name="payload"/>
    /// and answering protocol commands inline.
    /// </summary>
    private void ProcessIncoming(byte[] buffer, int length, List<byte> payload)
    {
        for (int i = 0; i < length; i++)
        {
            byte b = buffer[i];

            switch (_state)
            {
                case ParseState.Data:
                    if (b == IAC) _state = ParseState.Iac;
                    else payload.Add(b);
                    break;

                case ParseState.Iac:
                    if (b == IAC)
                    {
                        // Escaped 0xFF means a literal byte.
                        payload.Add(IAC);
                        _state = ParseState.Data;
                    }
                    else if (b is DO or DONT or WILL or WONT)
                    {
                        _pendingCommand = b;
                        _state = ParseState.Option;
                    }
                    else if (b == SB)
                    {
                        _subnegotiation.Clear();
                        _state = ParseState.Subnegotiation;
                    }
                    else
                    {
                        // Single byte commands (NOP, GA, ...) need no reply.
                        _state = ParseState.Data;
                    }
                    break;

                case ParseState.Option:
                    HandleNegotiation(_pendingCommand, b);
                    _state = ParseState.Data;
                    break;

                case ParseState.Subnegotiation:
                    if (b == IAC) _state = ParseState.SubnegotiationIac;
                    else _subnegotiation.Add(b);
                    break;

                case ParseState.SubnegotiationIac:
                    if (b == SE)
                    {
                        HandleSubnegotiation();
                        _state = ParseState.Data;
                    }
                    else if (b == IAC)
                    {
                        _subnegotiation.Add(IAC);
                        _state = ParseState.Subnegotiation;
                    }
                    else
                    {
                        _state = ParseState.Subnegotiation;
                    }
                    break;
            }
        }
    }

    private void HandleNegotiation(byte command, byte option)
    {
        switch (command)
        {
            case DO:
                if (option is OptTerminalType or OptNaws)
                {
                    SendCommand(WILL, option);
                    if (option == OptNaws)
                    {
                        _nawsEnabled = true;
                        SendWindowSize();
                    }
                }
                else if (option == OptSuppressGoAhead)
                {
                    SendCommand(WILL, option);
                }
                else
                {
                    SendCommand(WONT, option);
                }
                break;

            case DONT:
                if (option == OptNaws) _nawsEnabled = false;
                SendCommand(WONT, option);
                break;

            case WILL:
                // Let the server drive echo and go-ahead suppression; refuse anything else so the
                // stream stays in a state the emulator understands.
                if (option is OptEcho or OptSuppressGoAhead) SendCommand(DO, option);
                else SendCommand(DONT, option);
                break;

            case WONT:
                SendCommand(DONT, option);
                break;
        }
    }

    private void HandleSubnegotiation()
    {
        if (_subnegotiation.Count >= 2 &&
            _subnegotiation[0] == OptTerminalType &&
            _subnegotiation[1] == TerminalTypeSend)
        {
            var name = Encoding.ASCII.GetBytes(TerminalTypeName);
            var reply = new byte[name.Length + 6];
            reply[0] = IAC;
            reply[1] = SB;
            reply[2] = OptTerminalType;
            reply[3] = TerminalTypeIs;
            Array.Copy(name, 0, reply, 4, name.Length);
            reply[^2] = IAC;
            reply[^1] = SE;
            WriteRaw(reply);
        }
    }

    private void SendCommand(byte command, byte option) => WriteRaw(new[] { IAC, command, option });

    private void SendWindowSize()
    {
        if (!_nawsEnabled) return;

        // NAWS payload is width/height as 16-bit big endian, with 0xFF escaped.
        var values = new List<byte>();
        void AddEscaped(byte value)
        {
            values.Add(value);
            if (value == IAC) values.Add(IAC);
        }

        AddEscaped((byte)(_cols >> 8));
        AddEscaped((byte)(_cols & 0xFF));
        AddEscaped((byte)(_rows >> 8));
        AddEscaped((byte)(_rows & 0xFF));

        var packet = new List<byte>(values.Count + 5) { IAC, SB, OptNaws };
        packet.AddRange(values);
        packet.Add(IAC);
        packet.Add(SE);
        WriteRaw(packet.ToArray());
    }

    private void WriteRaw(byte[] bytes)
    {
        if (_stream == null || !IsConnected) return;

        _writeLock.Wait();
        try
        {
            _stream.Write(bytes, 0, bytes.Length);
            _stream.Flush();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Send(string data)
    {
        if (_stream == null || !IsConnected) return;

        var raw = Encoding.UTF8.GetBytes(data);

        // Escape IAC so user input containing 0xFF is not read as a command.
        var escaped = new List<byte>(raw.Length);
        foreach (var b in raw)
        {
            escaped.Add(b);
            if (b == IAC) escaped.Add(IAC);
        }

        WriteRaw(escaped.ToArray());
    }

    public void Resize(int rows, int cols, int width, int height)
    {
        if (cols <= 0 || rows <= 0) return;
        if (cols == _cols && rows == _rows) return;

        _cols = cols;
        _rows = rows;
        SendWindowSize();
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
