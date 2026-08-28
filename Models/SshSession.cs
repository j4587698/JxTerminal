using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Renci.SshNet;

namespace JxTerminal.Models;

public class SshSession : ITerminalSession
{
    private enum ConnectOutcome { Connected, NeedsKeyDecision, Failed }

    public string Host { get; }
    public int Port { get; }
    public string Username { get; }
    private readonly string _password;
    private readonly SshAuthenticationType _authType;
    private readonly string _keyFilePath;
    private readonly string _passphrase;
    private readonly Encoding _encoding;

    private SshClient? _sshClient;
    private SftpClient? _sftpClient;
    private ShellStream? _shellStream;

    // Stateful codec so multi-byte characters split across reads survive.
    private readonly Decoder _decoder;
    private readonly Encoder _encoder = Encoding.UTF8.GetEncoder();

    public event Action<string>? DataReceived;
    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<Exception>? ErrorOccurred;

    public bool IsConnected => _sshClient?.IsConnected ?? false;
    public ShellStream? ShellStream => _shellStream;
    public SftpClient? SftpClient => _sftpClient;

    /// <summary>
    /// Asked when the server presents a host key that is unknown or different from the trusted one.
    /// Arguments: host display name, received SHA256 fingerprint, previously trusted fingerprint
    /// (null when the host has never been seen). Resolves true to accept and remember the key.
    /// The callback runs on the connect pipeline (never inside the SSH library callback), so it may
    /// await UI interaction freely.
    /// </summary>
    public Func<string, string, string?, Task<bool>>? HostKeyVerifier { get; set; }

    /// <summary>
    /// Set when the connection was aborted because the host key was rejected.
    /// </summary>
    public bool HostKeyRejected { get; private set; }

    /// <summary>Key seen during a connect attempt that still awaits a user decision.</summary>
    private (string Received, string? Known)? _pendingKey;

    public SshSession(string host, int port, string username, string password,
        SshAuthenticationType authType = SshAuthenticationType.Password,
        string keyFilePath = "", string passphrase = "", string encodingName = "UTF-8")
    {
        Host = host;
        Port = port;
        Username = username;
        _password = password;
        _authType = authType;
        _keyFilePath = keyFilePath;
        _passphrase = passphrase;
        try { _encoding = Encoding.GetEncoding(string.IsNullOrWhiteSpace(encodingName) ? "UTF-8" : encodingName); }
        catch (ArgumentException) { _encoding = Encoding.UTF8; }
        _decoder = _encoding.GetDecoder();
        _encoder = _encoding.GetEncoder();
    }

    /// <summary>
    /// Connects to the server. When the host key is unknown or changed, the decision is deferred
    /// to <see cref="HostKeyVerifier"/> on the async pipeline instead of blocking inside the SSH
    /// library callback; an accepted key is trusted and the connection retried once.
    /// </summary>
    public async Task ConnectAsync()
    {
        var outcome = await ConnectCoreAsync();

        if (outcome == ConnectOutcome.NeedsKeyDecision)
        {
            var pending = _pendingKey;
            _pendingKey = null;

            bool accepted = false;
            if (pending.HasValue && HostKeyVerifier != null)
            {
                try
                {
                    accepted = await HostKeyVerifier($"{Host}:{Port}", pending.Value.Received, pending.Value.Known);
                }
                catch
                {
                    accepted = false;
                }
            }

            if (!accepted)
            {
                HostKeyRejected = true;
                ErrorOccurred?.Invoke(new System.Security.SecurityException("HostKeyRejected"));
                return;
            }

            Services.KnownHostsService.Trust(Host, Port, pending!.Value.Received);
            outcome = await ConnectCoreAsync();
        }
    }

    private async Task<ConnectOutcome> ConnectCoreAsync()
    {
        try
        {
            return await Task.Run(() =>
            {
                var methods = new List<AuthenticationMethod>();

                if (_authType == SshAuthenticationType.Key && !string.IsNullOrWhiteSpace(_keyFilePath) && File.Exists(_keyFilePath))
                {
                    var keyFile = string.IsNullOrEmpty(_passphrase)
                        ? new PrivateKeyFile(_keyFilePath)
                        : new PrivateKeyFile(_keyFilePath, _passphrase);

                    methods.Add(new PrivateKeyAuthenticationMethod(Username, keyFile));
                }
                else
                {
                    // Password or Ask (Ask should have provided password here)
                    methods.Add(new PasswordAuthenticationMethod(Username, _password));
                    methods.Add(new KeyboardInteractiveAuthenticationMethod(Username)); // Fallback
                }

                var connectionInfo = new ConnectionInfo(Host, Port, Username, methods.ToArray());
                connectionInfo.Timeout = TimeSpan.FromSeconds(15);

                // Handle keyboard interactive if needed
                foreach (var method in connectionInfo.AuthenticationMethods)
                {
                    if (method is KeyboardInteractiveAuthenticationMethod kiam)
                    {
                        kiam.AuthenticationPrompt += (sender, e) =>
                        {
                            foreach (var prompt in e.Prompts)
                            {
                                var request = prompt.Request.ToLowerInvariant();
                                if (request.Contains("password") || request.Contains("passwd") || prompt.Request.Contains("密码"))
                                {
                                    prompt.Response = _password;
                                }
                            }
                        };
                    }
                }

                _pendingKey = null;
                _sshClient = new SshClient(connectionInfo)
                {
                    KeepAliveInterval = TimeSpan.FromSeconds(30) // survive NAT/firewall idle timeouts
                };
                _sshClient.HostKeyReceived += OnHostKeyReceived;
                _sshClient.Connect();

                // The host key callback ran; if it deferred a decision we abort this attempt here
                // (cleanly disposing what was built) so the outer pipeline can ask the user.
                if (_pendingKey != null)
                {
                    CleanupClient();
                    return ConnectOutcome.NeedsKeyDecision;
                }

                // Create shell stream
                _shellStream = _sshClient.CreateShellStream("xterm-256color", 80, 24, 800, 600, 1024);
                _shellStream.DataReceived += (s, e) => DataReceived?.Invoke(Decode(e.Data));

                _sftpClient = new SftpClient(connectionInfo);
                _sftpClient.HostKeyReceived += OnHostKeyReceived;
                _sftpClient.Connect();

                Connected?.Invoke();
                return ConnectOutcome.Connected;
            });
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex);
            return ConnectOutcome.Failed;
        }
    }

    private void OnHostKeyReceived(object? sender, Renci.SshNet.Common.HostKeyEventArgs e)
    {
        var received = e.FingerPrintSHA256;
        var known = Services.KnownHostsService.GetFingerprint(Host, Port);

        if (known != null && string.Equals(known, received, StringComparison.Ordinal))
        {
            e.CanTrust = true;
            return;
        }

        // Never block inside the library callback: record the fingerprint, refuse this
        // attempt, and let the async pipeline ask the user before retrying.
        _pendingKey ??= (received, known);
        e.CanTrust = false;
    }

    private void CleanupClient()
    {
        _shellStream?.Dispose();
        _shellStream = null;
        _sftpClient?.Disconnect();
        _sftpClient?.Dispose();
        _sftpClient = null;
        _sshClient?.Disconnect();
        _sshClient?.Dispose();
        _sshClient = null;
    }

    private string Decode(byte[] data)
    {
        var chars = new char[_decoder.GetCharCount(data, 0, data.Length, false)];
        int count = _decoder.GetChars(data, 0, data.Length, chars, 0, false);
        return count == 0 ? string.Empty : new string(chars, 0, count);
    }

    private byte[] Encode(string data)
    {
        var chars = data.ToCharArray();
        var buffer = new byte[_encoder.GetByteCount(chars, 0, chars.Length, true)];
        _encoder.GetBytes(chars, 0, chars.Length, buffer, 0, true);
        return buffer;
    }

    public async Task<string> ExecuteCommandAsync(string command)
    {
        return await Task.Run(() =>
        {
            if (_sshClient != null && _sshClient.IsConnected)
            {
                try
                {
                    var cmd = _sshClient.CreateCommand(command);
                    cmd.CommandTimeout = TimeSpan.FromSeconds(20);
                    return cmd.Execute();
                }
                catch (Exception)
                {
                    return string.Empty;
                }
            }
            return string.Empty;
        });
    }

    public void Send(string data)
    {
        if (_shellStream != null && IsConnected)
        {
            var bytes = Encode(data);
            _shellStream.Write(bytes, 0, bytes.Length);
            _shellStream.Flush();
        }
    }

    public void Resize(int rows, int cols, int width, int height)
    {
        if (_shellStream != null && IsConnected)
        {
            try { _shellStream.ChangeWindowSize((uint)cols, (uint)rows, (uint)width, (uint)height); } catch { }
        }
    }

    public void Dispose()
    {
        try
        {
            CleanupClient();
        }
        catch { /* ignore */ }
        Disconnected?.Invoke();
    }
}
