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
    // Paths are now managed by PathConfig

    public static async Task SaveConnectionsAsync(IEnumerable<ConnectionGroup> groups)
    {
        try
        {
            // Use the Source Generated context for AOT compatibility
            var json = JsonSerializer.Serialize(groups, typeof(IEnumerable<ConnectionGroup>), AppJsonContext.Default);
            await File.WriteAllTextAsync(PathConfig.ConnectionsFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save connections: {ex.Message}");
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
            Console.WriteLine($"Failed to load connections: {ex.Message}");
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
            var json = JsonSerializer.Serialize(settings, typeof(TerminalSettings), AppJsonContext.Default);
            await File.WriteAllTextAsync(PathConfig.SettingsFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    public static async Task<TerminalSettings> LoadSettingsAsync()
    {
        try
        {
            if (File.Exists(PathConfig.SettingsFile))
            {
                var json = await File.ReadAllTextAsync(PathConfig.SettingsFile);
                var settings = JsonSerializer.Deserialize(json, AppJsonContext.Default.TerminalSettings);
                return settings ?? new TerminalSettings();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load settings: {ex.Message}");
        }

        return new TerminalSettings();
    }
}
