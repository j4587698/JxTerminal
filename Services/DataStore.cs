using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using JxTerminal.Models;
using TinyDb.Core;
using TinyDb.Collections;
using TinyDb.Bson;

namespace JxTerminal.Services;

public class DataStore
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static readonly object _dbLock = new();
    private static TinyDbEngine? _dbInstance;
    private static string? _customDbPath;

    /// <summary>True when loading connections encountered an unreadable or corrupt state.</summary>
    public static bool ConnectionsLoadFailed { get; private set; }

    /// <summary>
    /// Overrides database path for unit testing isolation.
    /// </summary>
    public static void SetDatabasePathForTesting(string? path)
    {
        lock (_dbLock)
        {
            CloseDatabase();
            _customDbPath = path;
        }
    }

    /// <summary>
    /// Gets or initializes the singleton TinyDbEngine instance.
    /// The database file is located in the project root directory (PathConfig.DatabaseFile).
    /// </summary>
    public static TinyDbEngine GetDatabase()
    {
        if (_dbInstance != null) return _dbInstance;
        lock (_dbLock)
        {
            if (_dbInstance != null) return _dbInstance;

            var dbPath = _customDbPath ?? PathConfig.DatabaseFile;
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _dbInstance = new TinyDbEngine(dbPath);
            return _dbInstance;
        }
    }

    public static TinyDbEngine Database => GetDatabase();

    public static async Task SaveConnectionsAsync(IEnumerable<ConnectionGroup> groups)
    {
        try
        {
            var col = GetDatabase().GetCollection<ConnectionGroup>();
            await col.DeleteAllAsync();
            await col.InsertAsync(groups);
            Sync.SyncService.Instance.NotifyLocalDataChanged();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to save connections to TinyDb: {ex.Message}");
        }
    }

    public static async Task<ObservableCollection<ConnectionGroup>> LoadConnectionsAsync()
    {
        ConnectionsLoadFailed = false;
        try
        {
            var col = GetDatabase().GetCollection<ConnectionGroup>();
            var groups = await col.FindAllAsync();
            if (groups.Count > 0)
            {
                return new ObservableCollection<ConnectionGroup>(groups);
            }
        }
        catch (Exception ex)
        {
            ConnectionsLoadFailed = true;
            AppLog.Error($"Failed to load connections from TinyDb: {ex.Message}");
        }

        return new ObservableCollection<ConnectionGroup>
        {
            new ConnectionGroup("Servers")
        };
    }

    public static void SaveSettings(TerminalSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, AppJsonContext.Default.TerminalSettings);
            var col = GetDatabase().GetCollection<AppSettingRecord>();
            col.UpsertAsync(new AppSettingRecord("terminal_settings", json)).GetAwaiter().GetResult();
            Sync.SyncService.Instance.NotifyLocalDataChanged();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to save settings synchronously to TinyDb: {ex.Message}");
        }
    }

    public static async Task SaveSettingsAsync(TerminalSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, AppJsonContext.Default.TerminalSettings);
            var col = GetDatabase().GetCollection<AppSettingRecord>();
            await col.UpsertAsync(new AppSettingRecord("terminal_settings", json));
            Sync.SyncService.Instance.NotifyLocalDataChanged();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to save settings to TinyDb: {ex.Message}");
        }
    }

    public static async Task<TerminalSettings> LoadSettingsAsync()
    {
        try
        {
            var col = GetDatabase().GetCollection<AppSettingRecord>();
            var record = await col.FindByIdAsync("terminal_settings");
            if (record != null && !string.IsNullOrWhiteSpace(record.Value))
            {
                var settings = JsonSerializer.Deserialize(record.Value, AppJsonContext.Default.TerminalSettings) ?? new TerminalSettings();
                settings.ResolveColorScheme();
                return settings;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load settings from TinyDb: {ex.Message}");
        }

        return new TerminalSettings();
    }

    public static TerminalSettings LoadSettings()
    {
        try
        {
            var col = GetDatabase().GetCollection<AppSettingRecord>();
            var record = col.FindById("terminal_settings");
            if (record != null && !string.IsNullOrWhiteSpace(record.Value))
            {
                var settings = JsonSerializer.Deserialize(record.Value, AppJsonContext.Default.TerminalSettings) ?? new TerminalSettings();
                settings.ResolveColorScheme();
                return settings;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load settings from TinyDb: {ex.Message}");
        }

        return new TerminalSettings();
    }

    public static async Task SaveSnippetsAsync(IEnumerable<SnippetItem> snippets)
    {
        try
        {
            var col = GetDatabase().GetCollection<SnippetItem>();
            await col.DeleteAllAsync();
            await col.InsertAsync(snippets);
            Sync.SyncService.Instance.NotifyLocalDataChanged();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to save snippets to TinyDb: {ex.Message}");
        }
    }

    public static async Task<ObservableCollection<SnippetItem>> LoadSnippetsAsync()
    {
        try
        {
            var col = GetDatabase().GetCollection<SnippetItem>();
            var items = await col.FindAllAsync();
            if (items.Count == 0)
            {
                var defaults = Terminal.PresetCommands.GetDefaultSnippets();
                await col.InsertAsync(defaults);
                return new ObservableCollection<SnippetItem>(defaults);
            }
            return new ObservableCollection<SnippetItem>(items);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load snippets from TinyDb: {ex.Message}");
        }

        return new ObservableCollection<SnippetItem>();
    }

    public static async Task SaveSyncConfigAsync(SyncConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, AppJsonContext.Default.SyncConfig);
            var col = GetDatabase().GetCollection<AppSettingRecord>();
            await col.UpsertAsync(new AppSettingRecord("sync_config", json));
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to save sync config to TinyDb: {ex.Message}");
        }
    }

    public static async Task<SyncConfig> LoadSyncConfigAsync()
    {
        try
        {
            var col = GetDatabase().GetCollection<AppSettingRecord>();
            var record = await col.FindByIdAsync("sync_config");
            if (record != null && !string.IsNullOrWhiteSpace(record.Value))
            {
                return JsonSerializer.Deserialize(record.Value, AppJsonContext.Default.SyncConfig) ?? new SyncConfig();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load sync config from TinyDb: {ex.Message}");
        }

        return new SyncConfig();
    }

    public static async Task SaveKeysAsync(IEnumerable<KeyItem> keys)
    {
        try
        {
            var col = GetDatabase().GetCollection<KeyItem>();
            await col.DeleteAllAsync();
            await col.InsertAsync(keys);
            Sync.SyncService.Instance.NotifyLocalDataChanged();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to save keys to TinyDb: {ex.Message}");
        }
    }

    public static async Task<ObservableCollection<KeyItem>> LoadKeysAsync()
    {
        try
        {
            var col = GetDatabase().GetCollection<KeyItem>();
            var items = await col.FindAllAsync();
            return new ObservableCollection<KeyItem>(items);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load keys from TinyDb: {ex.Message}");
        }

        return new ObservableCollection<KeyItem>();
    }

    public static async Task SaveCommandHistoryAsync(string command, int? limit = null)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        try
        {
            var trimmed = command.Trim();
            var col = GetDatabase().GetCollection<TerminalHistoryItem>();
            await col.DeleteManyAsync(h => h.Command == trimmed);
            await col.InsertAsync(new TerminalHistoryItem(trimmed));

            var maxLimit = limit ?? LoadSettings().HistoryRetentionLimit;
            if (maxLimit <= 0) maxLimit = 1000;

            var count = await col.CountAsync();
            if (count > maxLimit)
            {
                var all = await col.FindAllAsync();
                var toRemove = all.OrderBy(h => h.Timestamp).Take((int)(count - maxLimit)).ToList();
                foreach (var item in toRemove)
                {
                    await col.DeleteAsync(new BsonBinary(item.Id));
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to save command history to TinyDb: {ex.Message}");
        }
    }

    public static async Task<List<string>> LoadCommandHistoryAsync(int limit = 1000)
    {
        try
        {
            var col = GetDatabase().GetCollection<TerminalHistoryItem>();
            var items = await col.FindAllAsync();
            return items.OrderByDescending(h => h.Timestamp)
                        .Select(h => h.Command)
                        .Distinct()
                        .Take(limit)
                        .ToList();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to load command history from TinyDb: {ex.Message}");
            return new List<string>();
        }
    }

    public static async Task<List<TerminalHistoryItem>> LoadCommandHistoryItemsAsync(int limit = 1000)
    {
        try
        {
            var col = GetDatabase().GetCollection<TerminalHistoryItem>();
            var items = await col.FindAllAsync();
            return items.OrderByDescending(h => h.Timestamp)
                        .Take(limit)
                        .ToList();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to load command history items from TinyDb: {ex.Message}");
            return new List<TerminalHistoryItem>();
        }
    }

    public static async Task DeleteCommandHistoryItemAsync(Guid id)
    {
        try
        {
            var col = GetDatabase().GetCollection<TerminalHistoryItem>();
            await col.DeleteAsync(new BsonBinary(id));
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to delete command history item from TinyDb: {ex.Message}");
        }
    }

    public static async Task ClearCommandHistoryAsync()
    {
        try
        {
            var col = GetDatabase().GetCollection<TerminalHistoryItem>();
            await col.DeleteAllAsync();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to clear command history in TinyDb: {ex.Message}");
        }
    }

    /// <summary>
    /// Replaces the current command history with a batch of history items (used by sync restoration).
    /// </summary>
    public static async Task SaveCommandHistoryBatchAsync(IEnumerable<TerminalHistoryItem> items)
    {
        try
        {
            var col = GetDatabase().GetCollection<TerminalHistoryItem>();
            await col.DeleteAllAsync();
            if (items != null && items.Any())
            {
                await col.InsertAsync(items);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to batch save command history in TinyDb: {ex.Message}");
        }
    }

    public static void CloseDatabase()
    {
        lock (_dbLock)
        {
            if (_dbInstance != null)
            {
                try
                {
                    _dbInstance.Dispose();
                }
                catch { }
                _dbInstance = null;
            }
        }
    }
}
