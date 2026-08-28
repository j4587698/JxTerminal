using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JxTerminal.Services;

/// <summary>
/// Minimal known-hosts store: maps "host:port" to the SHA256 fingerprint that the user trusted.
/// </summary>
public static class KnownHostsService
{
    private static readonly object Sync = new();
    private static Dictionary<string, string>? _entries;

    private static Dictionary<string, string> Entries
    {
        get
        {
            if (_entries != null) return _entries;
            lock (Sync)
            {
                _entries ??= Load();
            }
            return _entries;
        }
    }

    private static Dictionary<string, string> Load()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(PathConfig.KnownHostsFile)) return map;

            foreach (var line in File.ReadAllLines(PathConfig.KnownHostsFile))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
                var parts = line.Split(' ', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2) continue;
                map[parts[0]] = parts[1];
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to read known_hosts: {ex.Message}");
        }
        return map;
    }

    private static string MakeKey(string host, int port) => $"{host}:{port}";

    /// <summary>
    /// Returns the trusted fingerprint for the host, or null when the host is unknown.
    /// </summary>
    public static string? GetFingerprint(string host, int port)
        => Entries.TryGetValue(MakeKey(host, port), out var fp) ? fp : null;

    public static void Trust(string host, int port, string fingerprint)
    {
        lock (Sync)
        {
            Entries[MakeKey(host, port)] = fingerprint;
            Persist();
        }
    }

    private static void Persist()
    {
        try
        {
            var lines = Entries.Select(kv => $"{kv.Key} {kv.Value}");
            var temp = PathConfig.KnownHostsFile + ".tmp";
            File.WriteAllLines(temp, lines);
            File.Move(temp, PathConfig.KnownHostsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to write known_hosts: {ex.Message}");
        }
    }
}
