using System;
using System.IO;

namespace JxTerminal.Services;

/// <summary>
/// Minimal append-only file logger so persistence/decryption failures are visible
/// to the user instead of vanishing into the console.
/// </summary>
public static class AppLog
{
    private static readonly object Lock = new();

    public static string LogFile => Path.Combine(PathConfig.AppDataRoot, "logs", "app.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
                File.AppendAllText(LogFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
