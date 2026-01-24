using System;
using System.Threading.Tasks;

namespace JxTerminal.Models;

public interface ITerminalSession : IDisposable
{
    event Action<string>? DataReceived;
    event Action? Connected;
    event Action? Disconnected;
    event Action<Exception>? ErrorOccurred;

    bool IsConnected { get; }

    Task ConnectAsync();
    void Send(string data);
    void Resize(int rows, int cols, int width, int height);
}
