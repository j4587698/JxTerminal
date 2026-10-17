using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using JxTerminal.Models;

namespace JxTerminal.Services.Sync;

/// <summary>
/// Uploads full configuration snapshots to the JxTerminal.Server and restores them
/// locally. Authentication uses a registered account: the client logs in, receives a
/// bearer token, and sends it on every backup request. Snapshots contain connections,
/// terminal settings, snippets and the SSH key store; secrets travel in the same
/// encrypted form they use on disk.
/// </summary>
public class SyncService
{
    private static SyncService? _instance;
    public static SyncService Instance => _instance ??= new SyncService();

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public SyncConfig Config { get; private set; } = new();

    private byte[]? _syncKey;
    public byte[]? CurrentSyncKey => _syncKey;

    public bool IsApplyingSnapshot { get; private set; }
    private bool _hasLocalChanges;
    private readonly System.Threading.SemaphoreSlim _syncLock = new(1, 1);
    private System.Threading.CancellationTokenSource? _debounceCts;

    public event Action<string>? SyncStatusChanged;
    public event Action<SyncResult>? SyncCompleted;
    public event Action? SnapshotApplied;

    public SyncService()
    {
        SecurityService.SyncKeyProvider = () => _syncKey;
    }

    public bool IsAuthenticated =>
        !string.IsNullOrWhiteSpace(Config.Token)
        && !string.IsNullOrWhiteSpace(Config.Username)
        && DateTime.UtcNow < Config.TokenExpiresAt;

    public async Task LoadConfigAsync()
    {
        Config = await DataStore.LoadSyncConfigAsync();
        if (!string.IsNullOrWhiteSpace(Config.EncryptedSyncKey))
        {
            var raw = SecurityService.Decrypt(Config.EncryptedSyncKey);
            if (!string.IsNullOrEmpty(raw))
            {
                try
                {
                    _syncKey = Convert.FromBase64String(raw);
                }
                catch { }
            }
        }
    }

    public void SetSyncPassword(string username, string password)
    {
        if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
        {
            _syncKey = SecurityService.DeriveKey(password, username.Trim().ToLowerInvariant());
            Config.EncryptedSyncKey = SecurityService.Encrypt(Convert.ToBase64String(_syncKey));
        }
    }

    public async Task SaveConfigAsync(SyncConfig config)
    {
        Config = config;
        await DataStore.SaveSyncConfigAsync(config);
    }

    public string BaseUrl => (Config.ServerUrl ?? "").Trim().TrimEnd('/');

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (IsAuthenticated)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Config.Token);
        }
    }

    private static string AppVersion
    {
        get
        {
            var v = typeof(SyncService).Assembly.GetName().Version;
            return v != null ? $"{v.Major}.{v.Minor}" : "0.1";
        }
    }

    private void ThrowIfUnauthenticated()
    {
        if (!IsAuthenticated)
        {
            throw new InvalidOperationException("SyncNotLoggedIn");
        }
    }

    /// <summary>
    /// Pings the server health endpoint to verify the URL. Does not require a token.
    /// Returns (success, optionalErrorMessage).
    /// </summary>
    public async Task<(bool Success, string? Error)> TestConnectionDetailedAsync()
    {
        if (!IsConfigured) return (false, "SyncNotConfigured");

        try
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/health");
            using var response = await _http.SendAsync(request, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }
            return (false, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
        }
        catch (TaskCanceledException)
        {
            return (false, "SyncErrorConnectionTimeout");
        }
        catch (HttpRequestException ex)
        {
            return (false, ex.Message);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Pings the server health endpoint to verify the URL. Does not require a token.
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        var (success, _) = await TestConnectionDetailedAsync();
        return success;
    }

    /// <summary>
    /// Verifies the stored token against the server (GET /api/auth/me). Returns false
    /// when the token is missing, expired, or rejected.
    /// </summary>
    public async Task<bool> ValidateTokenAsync()
    {
        if (!IsConfigured || !IsAuthenticated) return false;

        try
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/auth/me");
            ApplyAuth(request);

            using var response = await _http.SendAsync(request, cts.Token);
            if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403)
            {
                await LogoutAsync();
                return false;
            }
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Registers a new account on the server and stores the returned token.
    /// </summary>
    public async Task<AuthResult> RegisterAsync(string username, string password, string? secret = null)
    {
        if (!IsConfigured) throw new InvalidOperationException("SyncNotConfigured");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/auth/register")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new SyncAuthRequest { Username = username, Password = password, Secret = secret }, AppJsonContext.Default.SyncAuthRequest),
                Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw await ErrorFromAsync(response, "SyncRegisterFailed");
        }

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.AuthResult) ?? new AuthResult();
        SetSyncPassword(username, password);
        await ApplyAuthResultAsync(result, username);
        return result;
    }

    /// <summary>
    /// Signs in to an existing account and stores the returned token.
    /// </summary>
    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        if (!IsConfigured) throw new InvalidOperationException("SyncNotConfigured");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/auth/login")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new SyncAuthRequest { Username = username, Password = password }, AppJsonContext.Default.SyncAuthRequest),
                Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw await ErrorFromAsync(response, "SyncLoginFailed");
        }

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.AuthResult) ?? new AuthResult();
        SetSyncPassword(username, password);
        await ApplyAuthResultAsync(result, username);
        return result;
    }

    private async Task ApplyAuthResultAsync(AuthResult result, string username)
    {
        Config.Username = string.IsNullOrWhiteSpace(result.Username) ? username.Trim() : result.Username;
        Config.Token = result.Token ?? "";
        Config.TokenExpiresAt = result.ExpiresAt != default ? result.ExpiresAt : DateTime.UtcNow.AddDays(7);
        Config.LastSyncTime = null;
        await SaveConfigAsync(Config);
    }

    /// <summary>
    /// Clears the stored token (the server account itself is not deleted).
    /// </summary>
    public async Task LogoutAsync()
    {
        _syncKey = null;
        Config = new SyncConfig
        {
            ServerUrl = Config.ServerUrl,
            DeviceName = Config.DeviceName
        };
        await SaveConfigAsync(Config);
    }

    private static async Task<Exception> ErrorFromAsync(HttpResponseMessage response, string defaultKey)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync();
            var error = JsonSerializer.Deserialize(body, AppJsonContext.Default.SyncServerError);
            if (error != null && !string.IsNullOrWhiteSpace(error.Error))
            {
                var mapped = ServerErrorMap.TryGetValue(error.Error, out var key) ? key : error.Error;
                return new InvalidOperationException(mapped);
            }
        }
        catch
        {
            // Fall through to the generic message.
        }

        if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403)
        {
            return new InvalidOperationException("SyncAuthInvalid");
        }

        return new InvalidOperationException(defaultKey);
    }

    /// <summary>Maps known server validation messages to localizable resource keys.</summary>
    private static readonly Dictionary<string, string> ServerErrorMap = new()
    {
        ["Username is already taken."] = "SyncErrorUsernameTaken",
        ["Username must be at least 3 characters."] = "SyncErrorUsernameTooShort",
        ["Password must be at least 6 characters."] = "SyncErrorPasswordTooShort",
        ["Current password is incorrect."] = "SyncErrorCurrentPasswordIncorrect"
    };

    /// <summary>
    /// Changes the current user's account password on the server, switches the client's
    /// end-to-end encryption key, and immediately re-encrypts and pushes a fresh snapshot.
    /// </summary>
    public async Task ChangePasswordAsync(string currentPassword, string newPassword)
    {
        if (!IsConfigured) throw new InvalidOperationException("SyncNotConfigured");
        ThrowIfUnauthenticated();

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            throw new InvalidOperationException("SyncErrorPasswordTooShort");
        }

        var req = new ChangePasswordRequest
        {
            CurrentPassword = currentPassword ?? "",
            NewPassword = newPassword
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/auth/change-password")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(req, AppJsonContext.Default.ChangePasswordRequest),
                Encoding.UTF8, "application/json")
        };
        ApplyAuth(request);

        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw await ErrorFromAsync(response, "SyncChangePasswordFailed");
        }

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.AuthResult) ?? new AuthResult();

        // 1. Update token and username
        Config.Username = string.IsNullOrWhiteSpace(result.Username) ? Config.Username : result.Username;
        Config.Token = result.Token ?? Config.Token;
        Config.TokenExpiresAt = result.ExpiresAt != default ? result.ExpiresAt : Config.TokenExpiresAt;

        // 2. Derive new sync key with the new password
        SetSyncPassword(Config.Username, newPassword);
        await SaveConfigAsync(Config);

        // 3. Re-encrypt and push current snapshot with the new key so cloud snapshot is seamlessly updated
        await PushCurrentSnapshotAsync("PasswordChanged");
    }

    /// <summary>
    /// Builds a full snapshot of the local data store, re-encrypting secrets with the sync key
    /// so other authenticated devices can decrypt them.
    /// </summary>
    public async Task<SyncSnapshot> ExportSnapshotAsync()
    {
        var connections = await DataStore.LoadConnectionsAsync();
        var snippets = await DataStore.LoadSnippetsAsync();
        var keys = await DataStore.LoadKeysAsync();
        var history = await DataStore.LoadCommandHistoryItemsAsync();
        var settings = DataStore.LoadSettings();

        return new SyncSnapshot
        {
            AppVersion = AppVersion,
            DeviceName = (Config.DeviceName ?? "").Trim(),
            ExportedAt = DateTime.Now,
            Connections = connections.ToList(),
            Settings = settings,
            Snippets = snippets.ToList(),
            Keys = keys.ToList(),
            History = history.ToList()
        };
    }

    /// <summary>
    /// Uploads a new backup of the current configuration. Throws on failure.
    /// </summary>
    public async Task<RemoteBackupInfo> PushBackupAsync(string? comment)
    {
        if (!IsConfigured) throw new InvalidOperationException("SyncNotConfigured");
        ThrowIfUnauthenticated();

        var snapshot = await ExportSnapshotAsync();
        string payload;
        using (SecurityService.WithKey(_syncKey))
        {
            payload = JsonSerializer.Serialize(snapshot, AppJsonContext.Default.SyncSnapshot);
        }

        var uploadReq = new SyncBackupUploadRequest
        {
            DeviceName = snapshot.DeviceName,
            Comment = comment ?? "",
            AppVersion = snapshot.AppVersion,
            SnapshotJson = payload
        };
        var requestBody = JsonSerializer.Serialize(uploadReq, AppJsonContext.Default.SyncBackupUploadRequest);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/backups")
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };
        ApplyAuth(request);

        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw await ErrorFromAsync(response, "SyncPushFailed");
        }

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize(json, AppJsonContext.Default.RemoteBackupInfo) ?? new RemoteBackupInfo();
    }

    /// <summary>
    /// Lists backups stored on the server (newest first).
    /// </summary>
    public async Task<List<RemoteBackupInfo>> ListBackupsAsync()
    {
        if (!IsConfigured) return new List<RemoteBackupInfo>();
        ThrowIfUnauthenticated();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/backups");
        ApplyAuth(request);

        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw await ErrorFromAsync(response, "SyncListFailed");
        }

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize(json, AppJsonContext.Default.ListRemoteBackupInfo) ?? new List<RemoteBackupInfo>();
    }

    /// <summary>
    /// Downloads a backup snapshot from the server.
    /// </summary>
    public async Task<SyncSnapshot> DownloadBackupAsync(string id)
    {
        if (!IsConfigured) throw new InvalidOperationException("SyncNotConfigured");
        ThrowIfUnauthenticated();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/backups/{id}");
        ApplyAuth(request);

        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw await ErrorFromAsync(response, "SyncDownloadFailed");
        }

        var json = await response.Content.ReadAsStringAsync();
        var record = JsonSerializer.Deserialize(json, AppJsonContext.Default.RemoteBackupRecord) ?? new RemoteBackupRecord();

        SyncSnapshot? snapshot = null;
        using (SecurityService.WithKey(_syncKey))
        {
            snapshot = JsonSerializer.Deserialize(record.SnapshotJson ?? "", AppJsonContext.Default.SyncSnapshot);
        }

        return snapshot ?? new SyncSnapshot();
    }

    /// <summary>
    /// Downloads a backup and overwrites the local data store with its contents.
    /// </summary>
    public async Task<SyncSnapshot> RestoreBackupAsync(string id)
    {
        var snapshot = await DownloadBackupAsync(id);
        await ApplySnapshotAsync(snapshot);
        return snapshot;
    }

    /// <summary>
    /// Applies a snapshot to the local data store (connections, settings, snippets, keys, history).
    /// Returns true if local credentials were preserved because the snapshot's credentials were empty/missing.
    /// </summary>
    public async Task<bool> ApplySnapshotAsync(SyncSnapshot snapshot)
    {
        IsApplyingSnapshot = true;
        var credentialsPreserved = false;
        try
        {
            if (snapshot.Connections != null)
            {
                // Preserve local credentials if remote snapshot has empty/failed passwords
                try
                {
                    var localGroups = await DataStore.LoadConnectionsAsync();
                    var localPasswords = new Dictionary<Guid, (string Password, string Passphrase)>();
                    foreach (var g in localGroups)
                    {
                        foreach (var c in g.Connections)
                        {
                            if (!string.IsNullOrEmpty(c.Password) || !string.IsNullOrEmpty(c.Passphrase))
                            {
                                localPasswords[c.Id] = (c.Password, c.Passphrase);
                            }
                        }
                    }

                    if (localPasswords.Count > 0)
                    {
                        foreach (var g in snapshot.Connections)
                        {
                            foreach (var c in g.Connections)
                            {
                                if (localPasswords.TryGetValue(c.Id, out var existing))
                                {
                                    if (string.IsNullOrEmpty(c.Password) && !string.IsNullOrEmpty(existing.Password))
                                    {
                                        c.Password = existing.Password;
                                        credentialsPreserved = true;
                                    }
                                    if (string.IsNullOrEmpty(c.Passphrase) && !string.IsNullOrEmpty(existing.Passphrase))
                                    {
                                        c.Passphrase = existing.Passphrase;
                                        credentialsPreserved = true;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"Failed to preserve local credentials during snapshot apply: {ex.Message}");
                }

                await DataStore.SaveConnectionsAsync(snapshot.Connections);
            }
            if (snapshot.Settings != null)
            {
                try
                {
                    var localSettings = DataStore.LoadSettings();
                    if (string.IsNullOrEmpty(snapshot.Settings.AiApiKey) && !string.IsNullOrEmpty(localSettings.AiApiKey))
                    {
                        snapshot.Settings.AiApiKey = localSettings.AiApiKey;
                        credentialsPreserved = true;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"Failed to preserve local settings credentials during snapshot apply: {ex.Message}");
                }

                await DataStore.SaveSettingsAsync(snapshot.Settings);
            }
            if (snapshot.Snippets != null)
            {
                await DataStore.SaveSnippetsAsync(snapshot.Snippets);
            }
            if (snapshot.Keys != null)
            {
                try
                {
                    var localKeys = await DataStore.LoadKeysAsync();
                    var localKeyPems = localKeys.Where(k => !string.IsNullOrEmpty(k.PrivateKeyPem)).ToDictionary(k => k.Id, k => k.PrivateKeyPem);
                    foreach (var k in snapshot.Keys)
                    {
                        if (string.IsNullOrEmpty(k.PrivateKeyPem) && localKeyPems.TryGetValue(k.Id, out var existingPem))
                        {
                            k.PrivateKeyPem = existingPem;
                            credentialsPreserved = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"Failed to preserve local keys during snapshot apply: {ex.Message}");
                }

                await DataStore.SaveKeysAsync(snapshot.Keys);
            }
            if (snapshot.History != null && snapshot.History.Count > 0)
            {
                await DataStore.SaveCommandHistoryBatchAsync(snapshot.History);
            }

            return credentialsPreserved;
        }
        finally
        {
            IsApplyingSnapshot = false;
        }
    }

    /// <summary>
    /// Notifies that local data has changed and debounces an automatic background push to cloud.
    /// </summary>
    public void NotifyLocalDataChanged()
    {
        if (IsApplyingSnapshot) return;
        if (!IsAuthenticated || !Config.AutoSyncEnabled) return;

        _hasLocalChanges = true;
        _debounceCts?.Cancel();
        _debounceCts = new System.Threading.CancellationTokenSource();
        var token = _debounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2500, token);
                if (!token.IsCancellationRequested)
                {
                    await SyncNowAsync();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppLog.Warn($"Auto-sync background task failed: {ex.Message}");
            }
        }, token);
    }

    /// <summary>
    /// Queries remote sync metadata to see if remote data exists and when it was updated.
    /// </summary>
    public async Task<RemoteSyncStatus> GetRemoteSyncStatusAsync()
    {
        if (!IsConfigured) return new RemoteSyncStatus();
        ThrowIfUnauthenticated();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/sync/status");
            ApplyAuth(request);

            using var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize(json, AppJsonContext.Default.RemoteSyncStatus) ?? new RemoteSyncStatus();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"GetRemoteSyncStatusAsync /api/sync/status failed, using fallback: {ex.Message}");
        }

        // Fallback for servers without /api/sync/status
        var backups = await ListBackupsAsync();
        if (backups.Count > 0)
        {
            var latest = backups[0];
            return new RemoteSyncStatus
            {
                HasData = true,
                Id = latest.Id,
                DeviceName = latest.DeviceName,
                UpdatedAt = latest.CreatedAt,
                AppVersion = latest.AppVersion
            };
        }

        return new RemoteSyncStatus { HasData = false };
    }

    /// <summary>
    /// Downloads and decrypts the latest snapshot from the server.
    /// </summary>
    public async Task<SyncSnapshot> PullLatestSnapshotAsync()
    {
        if (!IsConfigured) throw new InvalidOperationException("SyncNotConfigured");
        ThrowIfUnauthenticated();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/sync/latest");
            ApplyAuth(request);

            using var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var record = JsonSerializer.Deserialize(json, AppJsonContext.Default.RemoteBackupRecord);
                if (record != null && !string.IsNullOrWhiteSpace(record.SnapshotJson))
                {
                    using (SecurityService.WithKey(_syncKey))
                    {
                        return JsonSerializer.Deserialize(record.SnapshotJson, AppJsonContext.Default.SyncSnapshot) ?? new SyncSnapshot();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"PullLatestSnapshotAsync /api/sync/latest failed: {ex.Message}");
        }

        // Fallback to downloading first backup
        var backups = await ListBackupsAsync();
        if (backups.Count > 0)
        {
            return await DownloadBackupAsync(backups[0].Id);
        }

        return new SyncSnapshot();
    }

    /// <summary>
    /// Pushes the current local configuration to the server as the latest sync snapshot.
    /// </summary>
    public async Task<RemoteBackupInfo> PushCurrentSnapshotAsync(string? comment = null)
    {
        if (!IsConfigured) throw new InvalidOperationException("SyncNotConfigured");
        ThrowIfUnauthenticated();

        var snapshot = await ExportSnapshotAsync();
        string payload;
        using (SecurityService.WithKey(_syncKey))
        {
            payload = JsonSerializer.Serialize(snapshot, AppJsonContext.Default.SyncSnapshot);
        }

        var uploadReq = new SyncBackupUploadRequest
        {
            DeviceName = snapshot.DeviceName,
            Comment = comment ?? "AutoSync",
            AppVersion = snapshot.AppVersion,
            SnapshotJson = payload
        };
        var requestBody = JsonSerializer.Serialize(uploadReq, AppJsonContext.Default.SyncBackupUploadRequest);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/sync")
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            };
            ApplyAuth(request);

            using var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize(json, AppJsonContext.Default.RemoteBackupInfo) ?? new RemoteBackupInfo();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"PushCurrentSnapshotAsync /api/sync failed, using fallback: {ex.Message}");
        }

        return await PushBackupAsync(comment ?? "AutoSync");
    }

    /// <summary>
    /// Performs an end-to-end synchronization cycle:
    /// compares remote vs local, pulls or pushes as needed, and updates timestamps.
    /// </summary>
    public async Task<SyncResult> SyncNowAsync(bool forcePush = false, bool forcePull = false)
    {
        if (!IsConfigured || !IsAuthenticated)
        {
            return new SyncResult { Success = false, Message = "SyncNotLoggedIn" };
        }

        if (!await _syncLock.WaitAsync(0))
        {
            return new SyncResult { Success = true, Action = SyncActionTaken.None, Message = "SyncInProgress" };
        }

        try
        {
            SyncStatusChanged?.Invoke("SyncStatusSyncing");

            var status = await GetRemoteSyncStatusAsync();
            SyncActionTaken action = SyncActionTaken.None;

            DateTime? syncedTimestamp = null;

            if (forcePush || (!status.HasData && _hasLocalChanges))
            {
                var pushInfo = await PushCurrentSnapshotAsync();
                syncedTimestamp = pushInfo.CreatedAt != default ? pushInfo.CreatedAt : DateTime.UtcNow;
                action = SyncActionTaken.Pushed;
            }
            else if (forcePull && status.HasData)
            {
                var snapshot = await PullLatestSnapshotAsync();
                var credentialsPreserved = await ApplySnapshotAsync(snapshot);
                SnapshotApplied?.Invoke();
                if (credentialsPreserved)
                {
                    AppLog.Info("Local credentials preserved over empty remote snapshot. Auto-repairing remote snapshot...");
                    var pushInfo = await PushCurrentSnapshotAsync("AutoRepairAfterPull");
                    syncedTimestamp = pushInfo.CreatedAt != default ? pushInfo.CreatedAt : DateTime.UtcNow;
                    action = SyncActionTaken.Pushed;
                }
                else
                {
                    syncedTimestamp = status.UpdatedAt ?? DateTime.UtcNow;
                    action = SyncActionTaken.Pulled;
                }
            }
            else if (!status.HasData)
            {
                // Remote has no data at all: push local data to seed the cloud
                var pushInfo = await PushCurrentSnapshotAsync();
                syncedTimestamp = pushInfo.CreatedAt != default ? pushInfo.CreatedAt : DateTime.UtcNow;
                action = SyncActionTaken.Pushed;
            }
            else
            {
                if (_hasLocalChanges)
                {
                    var pushInfo = await PushCurrentSnapshotAsync();
                    syncedTimestamp = pushInfo.CreatedAt != default ? pushInfo.CreatedAt : DateTime.UtcNow;
                    action = SyncActionTaken.Pushed;
                }
                else
                {
                    var remoteTime = status.UpdatedAt ?? DateTime.MinValue;
                    var localLastSync = Config.LastSyncTime ?? DateTime.MinValue;

                    // Remote is newer or never synced on this device
                    if (Config.LastSyncTime == null || remoteTime > localLastSync)
                    {
                        var snapshot = await PullLatestSnapshotAsync();
                        var credentialsPreserved = await ApplySnapshotAsync(snapshot);
                        SnapshotApplied?.Invoke();
                        if (credentialsPreserved)
                        {
                            AppLog.Info("Local credentials preserved over empty remote snapshot. Auto-repairing remote snapshot...");
                            var pushInfo = await PushCurrentSnapshotAsync("AutoRepairAfterPull");
                            syncedTimestamp = pushInfo.CreatedAt != default ? pushInfo.CreatedAt : DateTime.UtcNow;
                            action = SyncActionTaken.Pushed;
                        }
                        else
                        {
                            syncedTimestamp = status.UpdatedAt ?? DateTime.UtcNow;
                            action = SyncActionTaken.Pulled;
                        }
                    }
                    else
                    {
                        action = SyncActionTaken.None;
                    }
                }
            }

            _hasLocalChanges = false;
            if (syncedTimestamp.HasValue)
            {
                Config.LastSyncTime = syncedTimestamp.Value;
                await SaveConfigAsync(Config);
            }

            var result = new SyncResult
            {
                Success = true,
                Action = action,
                Timestamp = Config.LastSyncTime,
                Message = action switch
                {
                    SyncActionTaken.Pushed => "SyncPushed",
                    SyncActionTaken.Pulled => "SyncPulled",
                    _ => "SyncUpToDate"
                }
            };

            SyncCompleted?.Invoke(result);
            SyncStatusChanged?.Invoke(result.Message);
            return result;
        }
        catch (Exception ex)
        {
            AppLog.Error($"SyncNowAsync failed: {ex.Message}");
            SyncStatusChanged?.Invoke("SyncFailed");
            var result = new SyncResult { Success = false, Message = ex.Message };
            SyncCompleted?.Invoke(result);
            return result;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    /// <summary>
    /// Deletes a backup from the server. Throws on failure.
    /// </summary>
    public async Task DeleteBackupAsync(string id)
    {
        if (!IsConfigured) throw new InvalidOperationException("SyncNotConfigured");
        ThrowIfUnauthenticated();

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/api/backups/{id}");
        ApplyAuth(request);

        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw await ErrorFromAsync(response, "SyncDeleteFailed");
        }
    }
}
