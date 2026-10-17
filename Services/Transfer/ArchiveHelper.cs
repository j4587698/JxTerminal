using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace JxTerminal.Services.Transfer;

/// <summary>
/// Utility methods for archiving and unarchiving directories using .NET built-in Tar and Zip support.
/// </summary>
public static class ArchiveHelper
{
    /// <summary>
    /// Compresses a local directory or single file into a .tar.gz archive.
    /// </summary>
    public static async Task CreateTarGzAsync(string sourcePath, string targetFile, CancellationToken token = default)
    {
        var targetParent = Path.GetDirectoryName(targetFile);
        if (!string.IsNullOrEmpty(targetParent) && !Directory.Exists(targetParent))
        {
            Directory.CreateDirectory(targetParent);
        }

        if (File.Exists(sourcePath))
        {
            await Task.Run(async () =>
            {
                token.ThrowIfCancellationRequested();
                await using var fileStream = File.Create(targetFile);
                await using var gzipStream = new GZipStream(fileStream, CompressionLevel.Fastest);
                await using var tarWriter = new TarWriter(gzipStream, TarEntryFormat.Pax, leaveOpen: false);
                await tarWriter.WriteEntryAsync(sourcePath, Path.GetFileName(sourcePath), token);
            }, token);
            return;
        }

        if (!Directory.Exists(sourcePath))
            throw new DirectoryNotFoundException($"Source path not found: {sourcePath}");

        await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var fileStream = File.Create(targetFile);
            using var gzipStream = new GZipStream(fileStream, CompressionLevel.Fastest);
            TarFile.CreateFromDirectory(sourcePath, gzipStream, includeBaseDirectory: true);
        }, token);
    }

    /// <summary>
    /// Extracts a .tar.gz archive into the specified destination directory.
    /// </summary>
    public static async Task ExtractTarGzAsync(string archiveFile, string destinationDir, CancellationToken token = default)
    {
        if (!File.Exists(archiveFile))
            throw new FileNotFoundException($"Archive file not found: {archiveFile}");

        if (!Directory.Exists(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var fileStream = File.OpenRead(archiveFile);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzipStream, destinationDir, overwriteFiles: true);
        }, token);
    }

    /// <summary>
    /// Compresses a local directory or single file into a .zip archive.
    /// </summary>
    public static async Task CreateZipAsync(string sourcePath, string targetFile, CancellationToken token = default)
    {
        var targetParent = Path.GetDirectoryName(targetFile);
        if (!string.IsNullOrEmpty(targetParent) && !Directory.Exists(targetParent))
        {
            Directory.CreateDirectory(targetParent);
        }

        if (File.Exists(sourcePath))
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using var archive = ZipFile.Open(targetFile, ZipArchiveMode.Create);
                archive.CreateEntryFromFile(sourcePath, Path.GetFileName(sourcePath), CompressionLevel.Fastest);
            }, token);
            return;
        }

        if (!Directory.Exists(sourcePath))
            throw new DirectoryNotFoundException($"Source path not found: {sourcePath}");

        await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            ZipFile.CreateFromDirectory(sourcePath, targetFile, CompressionLevel.Fastest, includeBaseDirectory: true);
        }, token);
    }

    /// <summary>
    /// Extracts a .zip archive into the specified destination directory.
    /// </summary>
    public static async Task ExtractZipAsync(string archiveFile, string destinationDir, CancellationToken token = default)
    {
        if (!File.Exists(archiveFile))
            throw new FileNotFoundException($"Archive file not found: {archiveFile}");

        if (!Directory.Exists(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            ZipFile.ExtractToDirectory(archiveFile, destinationDir, overwriteFiles: true);
        }, token);
    }
}
