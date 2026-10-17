using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
    private readonly string _privateKeyContent;
    private readonly string _passphrase;
    private readonly Encoding _encoding;

    private SshClient? _sshClient;
    private SftpClient? _sftpClient;
    private ShellStream? _shellStream;

    // Stateful codec so multi-byte characters split across reads survive.
    private readonly Decoder _decoder;
    private readonly Encoder _encoder;

    /// <summary>
    /// Serializes all SFTP operations for this session: SSH.NET's SftpClient is not
    /// thread-safe, so listings and transfers must not run concurrently on it.
    /// </summary>
    public SemaphoreSlim SftpLock { get; } = new(1, 1);

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

    // Guards against raising Disconnected more than once per connection lifetime.
    private int _disconnectedInvoked;

    // Window dimensions (persists across connect lifecycle so CreateShellStream uses the actual size)
    private int _cols = 80;
    private int _rows = 24;
    private int _width = 800;
    private int _height = 600;

    public SshSession(string host, int port, string username, string password,
        SshAuthenticationType authType = SshAuthenticationType.Password,
        string keyFilePath = "", string passphrase = "", string encodingName = "UTF-8",
        string privateKeyContent = "")
    {
        Host = host;
        Port = port;
        Username = username;
        _password = password;
        _authType = authType;
        _keyFilePath = keyFilePath;
        _privateKeyContent = privateKeyContent;
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
        HostKeyRejected = false;
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
            if (outcome == ConnectOutcome.NeedsKeyDecision)
            {
                // The key changed between trusting and reconnecting: refuse rather than loop.
                _pendingKey = null;
                HostKeyRejected = true;
                ErrorOccurred?.Invoke(new System.Security.SecurityException("HostKeyRejected"));
            }
        }
    }

    public ConnectionInfo BuildConnectionInfo()
    {
        var methods = new List<AuthenticationMethod>();

        if (_authType == SshAuthenticationType.Key)
        {
            PrivateKeyFile? keyFile = null;
            if (!string.IsNullOrWhiteSpace(_privateKeyContent))
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(_privateKeyContent));
                keyFile = string.IsNullOrEmpty(_passphrase)
                    ? new PrivateKeyFile(stream)
                    : new PrivateKeyFile(stream, _passphrase);
            }
            else if (!string.IsNullOrWhiteSpace(_keyFilePath) && File.Exists(_keyFilePath))
            {
                keyFile = string.IsNullOrEmpty(_passphrase)
                    ? new PrivateKeyFile(_keyFilePath)
                    : new PrivateKeyFile(_keyFilePath, _passphrase);
            }

            if (keyFile != null)
            {
                methods.Add(new PrivateKeyAuthenticationMethod(Username, keyFile));
            }
            else
            {
                methods.Add(new PasswordAuthenticationMethod(Username, _password));
            }
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
                        // Single-prompt keyboard-interactive is almost always the
                        // password echo; multi-prompt (e.g. OTP) only answers the
                        // password entry and leaves the rest empty rather than
                        // sending the password to a verification-code field.
                        if (e.Prompts.Count() == 1
                            || request.Contains("password") || request.Contains("passwd")
                            || prompt.Request.Contains("密码"))
                        {
                            prompt.Response = _password;
                        }
                    }
                };
            }
        }

        return connectionInfo;
    }

    /// <summary>
    /// Creates and connects a dedicated SFTP client for background transfers.
    /// This avoids holding SftpLock on the shared UI client, allowing concurrent file browsing.
    /// Caller is responsible for disposing the returned client.
    /// </summary>
    public SftpClient? CreateDedicatedSftpClient()
    {
        if (!IsConnected) return null;
        try
        {
            var info = BuildConnectionInfo();
            var client = new SftpClient(info);
            client.HostKeyReceived += OnHostKeyReceived;
            client.Connect();
            return client;
        }
        catch (Exception ex)
        {
            Services.AppLog.Warn($"CreateDedicatedSftpClient failed: {ex.Message}");
            return null;
        }
    }

    private async Task<ConnectOutcome> ConnectCoreAsync()
    {
        // A reconnect reuses this session object; drop any half-dead previous client
        // so its DataReceived callbacks cannot duplicate output.
        CleanupClient();

        try
        {
            return await Task.Run(() =>
            {
                var connectionInfo = BuildConnectionInfo();
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

                // Create shell stream with the actual negotiated/measured terminal dimensions
                _shellStream = _sshClient.CreateShellStream("xterm-256color", (uint)_cols, (uint)_rows, (uint)_width, (uint)_height, 1024);
                try { _shellStream.ChangeWindowSize((uint)_cols, (uint)_rows, (uint)_width, (uint)_height); } catch { }
                _shellStream.DataReceived += (s, e) => DataReceived?.Invoke(Decode(e.Data));

                // Drain any early data received between CreateShellStream and attaching DataReceived
                try
                {
                    while (_shellStream.DataAvailable)
                    {
                        var buf = new byte[4096];
                        int read = _shellStream.Read(buf, 0, buf.Length);
                        if (read > 0)
                        {
                            var slice = new byte[read];
                            Array.Copy(buf, slice, read);
                            DataReceived?.Invoke(Decode(slice));
                        }
                        else break;
                    }
                }
                catch (Exception ex)
                {
                    Services.AppLog.Warn($"ShellStream initial drain error: {ex.Message}");
                }

                try
                {
                    _sftpClient = new SftpClient(connectionInfo);
                    _sftpClient.HostKeyReceived += OnHostKeyReceived;
                    _sftpClient.Connect();
                }
                catch (Exception ex)
                {
                    Services.AppLog.Warn($"SFTP connection initialization skipped: {ex.Message}");
                }

                // The SFTP attempt may have tripped the key callback; the main channel is
                // already established, so don't leak a stale pending decision into the
                // next connect attempt.
                _pendingKey = null;
                Interlocked.Exchange(ref _disconnectedInvoked, 0);

                Connected?.Invoke();
                return ConnectOutcome.Connected;
            });
        }
        catch (Exception ex)
        {
            // If the connection was aborted by OnHostKeyReceived (e.CanTrust = false),
            // SSH.NET throws SshConnectionException. We intercept this as NeedsKeyDecision
            // rather than a fatal connection error, allowing the user to verify and trust the host key.
            if (_pendingKey != null)
            {
                CleanupClient();
                return ConnectOutcome.NeedsKeyDecision;
            }

            ErrorOccurred?.Invoke(ex);
            return ConnectOutcome.Failed;
        }
    }

    private void OnHostKeyReceived(object? sender, Renci.SshNet.Common.HostKeyEventArgs e)
    {
        // Always derive the SHA256 fingerprint from the raw key bytes ourselves so the
        // stored format does not depend on which fingerprints the SSH.NET build exposes
        // (FingerPrintSHA256 vs MD5 hex changed across versions and caused spurious
        // "unknown host key" prompts for already-trusted servers).
        var received = e.HostKey is { Length: > 0 } hostKey
            ? ComputeSha256Fingerprint(hostKey)
            : (!string.IsNullOrEmpty(e.FingerPrintSHA256)
                ? e.FingerPrintSHA256.TrimEnd('=')
                : (e.FingerPrint != null ? Convert.ToHexString(e.FingerPrint) : string.Empty));

        var known = Services.KnownHostsService.GetFingerprint(Host, Port);

        if (!string.IsNullOrEmpty(received) && known != null
            && string.Equals(known.TrimEnd('='), received, StringComparison.Ordinal))
        {
            e.CanTrust = true;
            return;
        }

        // Never block inside the library callback: record the fingerprint, refuse this
        // attempt, and let the async pipeline ask the user before retrying.
        _pendingKey = (received, known);
        e.CanTrust = false;
    }

    /// <summary>OpenSSH-style SHA256 fingerprint: base64 without padding.</summary>
    private static string ComputeSha256Fingerprint(byte[] hostKey)
        => Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(hostKey)).TrimEnd('=');

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

    public async Task<string> ExecuteCommandAsync(string command, TimeSpan? timeout = null)
    {
        return await Task.Run(() =>
        {
            if (_sshClient != null && _sshClient.IsConnected)
            {
                try
                {
                    var cmd = _sshClient.CreateCommand(command);
                    cmd.CommandTimeout = timeout ?? TimeSpan.FromSeconds(20);
                    return cmd.Execute();
                }
                catch (Exception ex)
                {
                    // Callers (system monitor, SFTP probes) treat "" as "no data";
                    // log so a dead channel is at least diagnosable.
                    Services.AppLog.Warn($"ExecuteCommandAsync failed for '{command}': {ex.Message}");
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
            try
            {
                var bytes = Encode(data);
                _shellStream.Write(bytes, 0, bytes.Length);
                _shellStream.Flush();
            }
            catch (Exception ex)
            {
                // The link can die between the IsConnected check and the write;
                // never let that bubble up through a UI input event.
                Services.AppLog.Warn($"SshSession.Send failed: {ex.Message}");
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

        if (_shellStream != null && IsConnected)
        {
            try
            {
                _shellStream.ChangeWindowSize((uint)_cols, (uint)_rows, (uint)_width, (uint)_height);
            }
            catch (Exception ex)
            {
                Services.AppLog.Warn($"SshSession.ChangeWindowSize failed: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        try
        {
            CleanupClient();
        }
        catch { /* ignore */ }
        if (Interlocked.Exchange(ref _disconnectedInvoked, 1) == 0)
            Disconnected?.Invoke();
    }
}
