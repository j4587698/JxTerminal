using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Renci.SshNet;

namespace JxTerminal.Models;

public class SshSession : ITerminalSession
{
    // ...
    public string Host { get; }
    public int Port { get; }
    public string Username { get; }
    private readonly string _password; // In a real app, use SecureString or better mechanism
    private readonly SshAuthenticationType _authType;
    private readonly string _keyFilePath;
    private readonly string _passphrase;

    private SshClient? _sshClient;
    private SftpClient? _sftpClient;
    private ShellStream? _shellStream;

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
    /// (null when the host has never been seen). Returns true to accept and remember the key.
    /// </summary>
    public Func<string, string, string?, bool>? HostKeyVerifier { get; set; }

    /// <summary>
    /// Set when the connection was aborted because the host key was rejected.
    /// </summary>
    public bool HostKeyRejected { get; private set; }

    public SshSession(string host, int port, string username, string password, SshAuthenticationType authType = SshAuthenticationType.Password, string keyFilePath = "", string passphrase = "")
    {
        Host = host;
        Port = port;
        Username = username;
        _password = password;
        _authType = authType;
        _keyFilePath = keyFilePath;
        _passphrase = passphrase;
    }

    public async Task ConnectAsync()
    {
        await Task.Run(() =>
        {
            try
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
                
                // Handle keyboard interactive if needed (simple auto-response)
                foreach(var method in connectionInfo.AuthenticationMethods) 
                {
                    if (method is KeyboardInteractiveAuthenticationMethod kiam)
                    {
                        kiam.AuthenticationPrompt += (sender, e) => {
                            foreach (var prompt in e.Prompts)
                            {
                                if (prompt.Request.ToLower().Contains("password"))
                                {
                                    prompt.Response = _password;
                                }
                            }
                        };
                    }
                }

                _sshClient = new SshClient(connectionInfo);
                _sshClient.HostKeyReceived += OnHostKeyReceived;
                _sshClient.Connect();

                if (HostKeyRejected)
                {
                    throw new System.Security.SecurityException("HostKeyRejected");
                }

                // Create shell stream
                // 80 cols, 24 rows, 800 width, 600 height, 1024 buffer size
                _shellStream = _sshClient.CreateShellStream("xterm-256color", 80, 24, 800, 600, 1024);
                _shellStream.DataReceived += (s, e) => DataReceived?.Invoke(Encoding.UTF8.GetString(e.Data));

                _sftpClient = new SftpClient(connectionInfo);
                _sftpClient.HostKeyReceived += OnHostKeyReceived;
                _sftpClient.Connect();

                Connected?.Invoke();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
            }
        });
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

        var verifier = HostKeyVerifier;
        if (verifier == null)
        {
            // No UI attached: refuse rather than silently trusting an unverified key.
            e.CanTrust = false;
            HostKeyRejected = true;
            return;
        }

        var accepted = verifier($"{Host}:{Port}", received, known);
        e.CanTrust = accepted;
        if (accepted)
        {
            Services.KnownHostsService.Trust(Host, Port, received);
        }
        else
        {
            HostKeyRejected = true;
        }
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
                    return cmd.Execute();
                }
                catch (Exception)
                {
                    // Log or handle? For now return empty or error
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
            _shellStream.Write(data);
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
        _shellStream?.Dispose();
        _sftpClient?.Disconnect();
        _sftpClient?.Dispose();
        _sshClient?.Disconnect();
        _sshClient?.Dispose();
        Disconnected?.Invoke();
    }
}
