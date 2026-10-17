using System;
using System.IO;

namespace JxTerminal.Services;

public static class PathConfig
{
    private static readonly Lazy<string> _appRoot = new(() =>
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        var current = new DirectoryInfo(dir);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "JxTerminal.csproj")) ||
                File.Exists(Path.Combine(current.FullName, "JxTerminal.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        return AppDomain.CurrentDomain.BaseDirectory;
    });

    /// <summary>
    /// Application/Project root directory for portable storage.
    /// In development, resolves to the repository root containing JxTerminal.csproj.
    /// In published builds, resolves to AppDomain.CurrentDomain.BaseDirectory.
    /// </summary>
    public static string AppRoot => _appRoot.Value;

    /// <summary>
    /// Portable data root directory located in the project directory, NOT in AppData.
    /// Aliased to AppDataRoot for backward compatibility with UI bindings and settings displays.
    /// </summary>
    public static string AppDataRoot => AppRoot;

    /// <summary>
    /// Embedded TinyDb database file located in the project root.
    /// </summary>
    public static string DatabaseFile => Path.Combine(AppRoot, "jxterminal.db");

    public static string BackgroundsDir => Path.Combine(AppRoot, "Backgrounds");
    public static string MasterKeyFile => Path.Combine(AppRoot, "master.key");
    public static string KnownHostsFile => Path.Combine(AppRoot, "known_hosts");

    static PathConfig()
    {
        if (!Directory.Exists(AppRoot)) Directory.CreateDirectory(AppRoot);
        if (!Directory.Exists(BackgroundsDir)) Directory.CreateDirectory(BackgroundsDir);
    }

    /// <summary>
    /// Resolves a potentially relative path to an absolute path within the data root.
    /// </summary>
    public static string GetFullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        if (Path.IsPathRooted(path)) return path;
        return Path.Combine(AppRoot, path);
    }

    /// <summary>
    /// Converts an absolute path to a path relative to the data root.
    /// </summary>
    public static string GetRelativePath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return "";

        var root = AppRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (fullPath.StartsWith(root, comparison))
        {
            return fullPath.Substring(root.Length);
        }

        return fullPath;
    }
}
