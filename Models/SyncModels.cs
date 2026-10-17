using System;
using System.Collections.Generic;

namespace JxTerminal.Models;

/// <summary>
/// A full export of the local data store. This is the payload exchanged with the
/// sync server: connections, settings, snippets and key-store entries.
/// </summary>
public class SyncSnapshot
{
    public string AppVersion { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public DateTime ExportedAt { get; set; } = DateTime.Now;
    public List<ConnectionGroup> Connections { get; set; } = new();
    public TerminalSettings? Settings { get; set; }
    public List<SnippetItem> Snippets { get; set; } = new();
    public List<KeyItem> Keys { get; set; } = new();
    public List<TerminalHistoryItem> History { get; set; } = new();
}

/// <summary>
/// Server connection settings for cloud backup / sync. Stored locally in TinyDb
/// (AppSettingRecord "sync_config"). The bearer token is what authenticates requests;
/// the password itself is never persisted.
/// </summary>
public class SyncConfig
{
    public string ServerUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string Token { get; set; } = "";
    public DateTime TokenExpiresAt { get; set; }
    public string DeviceName { get; set; } = "";

    /// <summary>
    /// PBKDF2-derived sync key encrypted with the local machine's MasterKey so the client
    /// can encrypt/decrypt E2EE snapshots across restarts without re-entering the password.
    /// </summary>
    public string EncryptedSyncKey { get; set; } = "";

    /// <summary>
    /// When true, changes to connections, settings, snippets, and keys are automatically pushed to the cloud.
    /// </summary>
    public bool AutoSyncEnabled { get; set; } = true;

    /// <summary>
    /// Timestamp (UTC) of the last successful synchronization.
    /// </summary>
    public DateTime? LastSyncTime { get; set; }
}

public class RemoteSyncStatus
{
    public bool HasData { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Id { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string AppVersion { get; set; } = "";
}

public enum SyncActionTaken
{
    None,
    Pushed,
    Pulled
}

public class SyncResult
{
    public bool Success { get; set; }
    public SyncActionTaken Action { get; set; }
    public string Message { get; set; } = "";
    public DateTime? Timestamp { get; set; }
}

/// <summary>
/// Bearer token + account info returned by the server register / login endpoints.
/// </summary>
public class AuthResult
{
    public string Token { get; set; } = "";
    public string Username { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public bool IsAdmin { get; set; }
}

/// <summary>
/// Metadata of a remote backup as returned by the server list endpoint.
/// </summary>
public class RemoteBackupInfo
{
    public string Id { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Comment { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string AppVersion { get; set; } = "";
    public long SizeBytes { get; set; }
}

/// <summary>
/// A remote backup as returned by the server detail endpoint (metadata + payload).
/// </summary>
public class RemoteBackupRecord
{
    public string Id { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Comment { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string AppVersion { get; set; } = "";
    public string SnapshotJson { get; set; } = "";
}

public class SyncAuthRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string? Secret { get; set; }
}

public class SyncBackupUploadRequest
{
    public string DeviceName { get; set; } = "";
    public string Comment { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public string SnapshotJson { get; set; } = "";
}

public class SyncServerError
{
    public string? Error { get; set; }
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}
