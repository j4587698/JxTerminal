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
    private const long MaxLogBytes = 5 * 1024 * 1024; // 5 MB, then rotate once

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

                // Poor man's rotation: keep one previous generation so the log can
                // never grow without bound on long-running installs.
                if (File.Exists(LogFile) && new FileInfo(LogFile).Length > MaxLogBytes)
                {
                    var backup = LogFile + ".1";
                    try { File.Delete(backup); } catch { }
                    File.Move(LogFile, backup);
                }

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
