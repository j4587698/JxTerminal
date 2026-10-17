using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using JxTerminal.Models;

namespace JxTerminal.Services.ImportExport;

public class ImportExportService
{
    private static ImportExportService? _instance;
    public static ImportExportService Instance => _instance ??= new ImportExportService();

    public async Task ExportConnectionsAsync(string filePath, IEnumerable<ConnectionGroup> groups)
    {
        var json = JsonSerializer.Serialize(groups, AppJsonContext.Default.IEnumerableConnectionGroup);
        await File.WriteAllTextAsync(filePath, json);
    }

    public async Task<List<ConnectionGroup>> ImportConnectionsAsync(string filePath)
    {
        if (!File.Exists(filePath)) return new List<ConnectionGroup>();

        var json = await File.ReadAllTextAsync(filePath);
        var groups = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListConnectionGroup);
        return groups ?? new List<ConnectionGroup>();
    }

    public List<SavedConnection> ParseOpenSshConfig(string content)
    {
        var connections = new List<SavedConnection>();
        var lines = content.Split('\n');

        SavedConnection? current = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;

            var parts = line.Split(new[] { ' ', '=' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            var key = parts[0].Trim();
            var val = parts[1].Trim().Trim('"');

            if (key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                // OpenSSH allows multiple patterns per Host line: `Host web db *.local`.
                // Create one connection per concrete alias instead of one bogus "web db" host.
                var aliases = val.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(a => !a.Contains('*') && !a.Contains('?') && !a.StartsWith('!'))
                    .ToArray();

                if (aliases.Length == 0)
                {
                    current = null;
                    continue;
                }

                if (current != null && !string.IsNullOrEmpty(current.Host))
                {
                    connections.Add(current);
                }

                current = new SavedConnection
                {
                    Name = aliases[0],
                    Host = aliases[0],
                    Type = ConnectionType.Ssh,
                    Port = 22,
                    Username = "", // No default: OpenSSH falls back to the local user, and blank is honest
                    GroupName = "SSH Config"
                };
            }
            else if (current != null)
            {
                if (key.Equals("HostName", StringComparison.OrdinalIgnoreCase))
                {
                    current.Host = val;
                }
                else if (key.Equals("User", StringComparison.OrdinalIgnoreCase))
                {
                    current.Username = val;
                }
                else if (key.Equals("Port", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(val, out int p)) current.Port = p;
                }
                else if (key.Equals("IdentityFile", StringComparison.OrdinalIgnoreCase))
                {
                    // Only expand a leading "~/" (a mid-path tilde is a literal character).
                    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var resolvedPath = val == "~" ? home
                        : val.StartsWith("~/", StringComparison.Ordinal) || val.StartsWith("~\\", StringComparison.Ordinal)
                            ? Path.Combine(home, val.Substring(2))
                            : val;
                    current.KeyFilePath = resolvedPath;
                    current.AuthenticationType = SshAuthenticationType.Key;
                }
            }
        }

        if (current != null && !string.IsNullOrEmpty(current.Host))
        {
            connections.Add(current);
        }

        return connections;
    }
}
