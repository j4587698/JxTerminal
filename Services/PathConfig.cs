using System;
using System.IO;

namespace JxTerminal.Services;

public static class PathConfig
{
    public static string AppDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "JxTerminal");

    public static string ConnectionsFile => Path.Combine(AppDataRoot, "connections.json");
    public static string SettingsFile => Path.Combine(AppDataRoot, "settings.json");
    public static string BackgroundsDir => Path.Combine(AppDataRoot, "Backgrounds");
    public static string MasterKeyFile => Path.Combine(AppDataRoot, "master.key");
    public static string KnownHostsFile => Path.Combine(AppDataRoot, "known_hosts");
    public static string SnippetsFile => Path.Combine(AppDataRoot, "snippets.json");

    static PathConfig()
    {
        if (!Directory.Exists(AppDataRoot)) Directory.CreateDirectory(AppDataRoot);
        if (!Directory.Exists(BackgroundsDir)) Directory.CreateDirectory(BackgroundsDir);
    }

    /// <summary>
    /// Resolves a potentially relative path to an absolute path within the AppData root.
    /// If the path is already absolute, it returns it as is (unless we want to enforce root).
    /// </summary>
    public static string GetFullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        
        // If it's already an absolute path, assume it's valid (or temp)
        // But for our settings logic, we prefer resolving relative to root.
        if (Path.IsPathRooted(path)) return path;

        return Path.Combine(AppDataRoot, path);
    }

    /// <summary>
    /// Converts an absolute path to a path relative to the AppData root.
    /// </summary>
    public static string GetRelativePath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return "";

        // Ensure directories end with separator for correct relative calculation
        var root = AppDataRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        
        if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath.Substring(root.Length);
        }

        return fullPath; // Fallback if not inside root
    }
}
