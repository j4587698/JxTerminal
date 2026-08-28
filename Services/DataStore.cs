using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using JxTerminal.Models;

namespace JxTerminal.Services;

public class DataStore
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // Paths are now managed by PathConfig

    /// <summary>
    /// Atomic write: dump to a temp file first, then swap it in. A crash mid-write can no
    /// longer truncate the real file.
    /// </summary>
    private static async Task WriteTextAtomicAsync(string path, string contents)
    {
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, contents);
        File.Move(temp, path, overwrite: true);
    }

    public static async Task SaveConnectionsAsync(IEnumerable<ConnectionGroup> groups)
    {
        try
        {
            // Use the Source Generated context for AOT compatibility
            var json = JsonSerializer.Serialize(groups, typeof(IEnumerable<ConnectionGroup>), AppJsonContext.Default);
            await WriteTextAtomicAsync(PathConfig.ConnectionsFile, json);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to save connections: {ex.Message}");
        }
    }

    public static async Task<ObservableCollection<ConnectionGroup>> LoadConnectionsAsync()
    {
        try
        {
            if (File.Exists(PathConfig.ConnectionsFile))
            {
                var json = await File.ReadAllTextAsync(PathConfig.ConnectionsFile);
                // Use the Source Generated context for AOT compatibility
                var groups = JsonSerializer.Deserialize(json, AppJsonContext.Default.ObservableCollectionConnectionGroup);
                return groups ?? new ObservableCollection<ConnectionGroup>();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load connections: {ex.Message}");
        }

        // Return default if no file or error
        return new ObservableCollection<ConnectionGroup>
        {
            new ConnectionGroup("Servers")
        };
    }

    public static async Task SaveSettingsAsync(TerminalSettings settings)
    {
        try
        {
            // Reflection-based serializer here: the source-gen context cannot see the
            // properties that CommunityToolkit.Mvvm generates for this type.
            var json = JsonSerializer.Serialize(settings, Indented);
            await WriteTextAtomicAsync(PathConfig.SettingsFile, json);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to save settings: {ex.Message}");
        }
    }

    public static async Task<TerminalSettings> LoadSettingsAsync()
    {
        try
        {
            if (File.Exists(PathConfig.SettingsFile))
            {
                var json = await File.ReadAllTextAsync(PathConfig.SettingsFile);
                return JsonSerializer.Deserialize<TerminalSettings>(json, Indented) ?? new TerminalSettings();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load settings: {ex.Message}");
        }

        return new TerminalSettings();
    }

    /// <summary>
    /// Synchronous twin of <see cref="LoadSettingsAsync"/> for app startup, where we must
    /// create the main window before returning from OnFrameworkInitializationCompleted.
    /// </summary>
    public static TerminalSettings LoadSettings()
    {
        try
        {
            if (File.Exists(PathConfig.SettingsFile))
            {
                var json = File.ReadAllText(PathConfig.SettingsFile);
                return JsonSerializer.Deserialize<TerminalSettings>(json, Indented) ?? new TerminalSettings();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load settings: {ex.Message}");
        }

        return new TerminalSettings();
    }

    public static async Task SaveSnippetsAsync(IEnumerable<SnippetItem> snippets)
    {
        try
        {
            var json = JsonSerializer.Serialize(snippets, typeof(IEnumerable<SnippetItem>), AppJsonContext.Default);
            await WriteTextAtomicAsync(PathConfig.SnippetsFile, json);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to save snippets: {ex.Message}");
        }
    }

    public static async Task<ObservableCollection<SnippetItem>> LoadSnippetsAsync()
    {
        try
        {
            if (File.Exists(PathConfig.SnippetsFile))
            {
                var json = await File.ReadAllTextAsync(PathConfig.SnippetsFile);
                var snippets = JsonSerializer.Deserialize(json, AppJsonContext.Default.ObservableCollectionSnippetItem);
                return snippets ?? new ObservableCollection<SnippetItem>();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load snippets: {ex.Message}");
        }

        return new ObservableCollection<SnippetItem>();
    }
}
