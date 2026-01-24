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
    private static readonly string DataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "JxTerminal", 
        "connections.json");

    public static async Task SaveConnectionsAsync(IEnumerable<ConnectionGroup> groups)
    {
        try
        {
            var dir = Path.GetDirectoryName(DataPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(groups, options);
            await File.WriteAllTextAsync(DataPath, json);
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
            if (File.Exists(DataPath))
            {
                var json = await File.ReadAllTextAsync(DataPath);
                var groups = JsonSerializer.Deserialize<ObservableCollection<ConnectionGroup>>(json);
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
}
