using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JxTerminal.Models;
using Renci.SshNet;

namespace JxTerminal.Services.Transfer;

public class TransferService : ITransferService
{
    private static TransferService? _instance;
    public static TransferService Instance => _instance ??= new TransferService();

    public ObservableCollection<TransferItem> Transfers { get; } = new();
    private readonly SemaphoreSlim _concurrencyLimiter = new(3, 3);

    public TransferItem EnqueueUpload(SshSession session, string localFilePath, string remoteDirPath)
    {
        var fileInfo = new FileInfo(localFilePath);
        var fileName = fileInfo.Name;
        var remotePath = $"{remoteDirPath.TrimEnd('/')}/{fileName}";

        var item = new TransferItem
        {
            FileName = fileName,
            LocalPath = localFilePath,
            RemotePath = remotePath,
            Direction = TransferDirection.Upload,
            TotalBytes = fileInfo.Exists ? fileInfo.Length : 0,
            SessionTarget = $"{session.Username}@{session.Host}",
            Status = TransferStatus.Queued,
            StartTime = DateTime.Now
        };

        item.WorkerAction = async (token, progress) =>
        {
            if (!session.IsConnected)
                throw new InvalidOperationException("SFTP is not connected.");

            using var localStream = File.OpenRead(localFilePath);
            SftpClient? dedicatedClient = null;
            try
            {
                dedicatedClient = session.CreateDedicatedSftpClient();
            }
            catch (Exception ex)
            {
                Services.AppLog.Warn($"Could not create dedicated SFTP client: {ex.Message}");
            }

            if (dedicatedClient != null)
            {
                using (dedicatedClient)
                {
                    await StreamUploadAsync(dedicatedClient, localStream, remotePath, token, progress.Report);
                }
            }
            else
            {
                if (session.SftpClient == null)
                    throw new InvalidOperationException("SFTP is not connected.");

                await session.SftpLock.WaitAsync(token);
                try
                {
                    await StreamUploadAsync(session.SftpClient, localStream, remotePath, token, progress.Report);
                }
                finally
                {
                    session.SftpLock.Release();
                }
            }
        };

        Transfers.Insert(0, item);
        _ = ProcessItemAsync(item);
        return item;
    }

    public TransferItem EnqueueDownload(SshSession session, string remoteFilePath, string localSavePath, long fileSize)
    {
        var fileName = Path.GetFileName(remoteFilePath);

        var item = new TransferItem
        {
            FileName = fileName,
            LocalPath = localSavePath,
            RemotePath = remoteFilePath,
            Direction = TransferDirection.Download,
            TotalBytes = Math.Max(1, fileSize),
            SessionTarget = $"{session.Username}@{session.Host}",
            Status = TransferStatus.Queued,
            StartTime = DateTime.Now
        };

        item.WorkerAction = async (token, progress) =>
        {
            if (!session.IsConnected)
                throw new InvalidOperationException("SFTP is not connected.");

            using var localStream = File.Create(localSavePath);
            SftpClient? dedicatedClient = null;
            try
            {
                dedicatedClient = session.CreateDedicatedSftpClient();
            }
            catch (Exception ex)
            {
                Services.AppLog.Warn($"Could not create dedicated SFTP client: {ex.Message}");
            }

            if (dedicatedClient != null)
            {
                using (dedicatedClient)
                {
                    await StreamDownloadAsync(dedicatedClient, remoteFilePath, localStream, token, progress.Report);
                }
            }
            else
            {
                if (session.SftpClient == null)
                    throw new InvalidOperationException("SFTP is not connected.");

                await session.SftpLock.WaitAsync(token);
                try
                {
                    await StreamDownloadAsync(session.SftpClient, remoteFilePath, localStream, token, progress.Report);
                }
                finally
                {
                    session.SftpLock.Release();
                }
            }
        };

        Transfers.Insert(0, item);
        _ = ProcessItemAsync(item);
        return item;
    }

    public TransferItem EnqueueUploadFileCompressed(SshSession session, string localFilePath, string remoteDirPath, FolderTransferMode mode, bool autoExtract = true)
    {
        var fileName = Path.GetFileName(localFilePath);
        var remoteTargetDir = remoteDirPath.TrimEnd('/');
        var remoteFilePath = $"{remoteTargetDir}/{fileName}";

        var item = new TransferItem
        {
            FileName = fileName,
            LocalPath = localFilePath,
            RemotePath = remoteFilePath,
            Direction = TransferDirection.Upload,
            IsDirectory = false,
            IsCompressed = mode != FolderTransferMode.Standard,
            CompressionFormat = mode == FolderTransferMode.TarGz ? "tar.gz" : (mode == FolderTransferMode.Zip ? "zip" : string.Empty),
            SessionTarget = $"{session.Username}@{session.Host}",
            Status = TransferStatus.Queued,
            StartTime = DateTime.Now
        };

        item.WorkerAction = async (token, progress) =>
        {
            if (!session.IsConnected)
                throw new InvalidOperationException("SFTP is not connected.");

            if (mode is FolderTransferMode.TarGz or FolderTransferMode.Zip)
            {
                await UploadFolderCompressedInternalAsync(session, item, localFilePath, remoteTargetDir, mode, autoExtract, token, progress);
            }
            else
            {
                using var fs = File.OpenRead(localFilePath);
                item.TotalBytes = Math.Max(1, fs.Length);
                var client = session.CreateDedicatedSftpClient();
                if (client != null)
                {
                    using (client)
                    {
                        await StreamUploadAsync(client, fs, remoteFilePath, token, progress.Report);
                    }
                }
                else
                {
                    if (session.SftpClient == null) throw new InvalidOperationException("SFTP is not connected.");
                    await session.SftpLock.WaitAsync(token);
                    try
                    {
                        await StreamUploadAsync(session.SftpClient, fs, remoteFilePath, token, progress.Report);
                    }
                    finally
                    {
                        session.SftpLock.Release();
                    }
                }
            }
        };

        Transfers.Insert(0, item);
        _ = ProcessItemAsync(item);
        return item;
    }

    public TransferItem EnqueueDownloadFileCompressed(SshSession session, string remoteFilePath, string localSaveDir, FolderTransferMode mode, bool autoExtract = true)
    {
        var cleanRemotePath = remoteFilePath.TrimEnd('/');
        var fileName = cleanRemotePath.Split('/').Last();
        var localTargetPath = Path.Combine(localSaveDir, fileName);

        var item = new TransferItem
        {
            FileName = fileName,
            LocalPath = localTargetPath,
            RemotePath = cleanRemotePath,
            Direction = TransferDirection.Download,
            IsDirectory = false,
            IsCompressed = mode != FolderTransferMode.Standard,
            CompressionFormat = mode == FolderTransferMode.TarGz ? "tar.gz" : (mode == FolderTransferMode.Zip ? "zip" : string.Empty),
            SessionTarget = $"{session.Username}@{session.Host}",
            Status = TransferStatus.Queued,
            StartTime = DateTime.Now
        };

        item.WorkerAction = async (token, progress) =>
        {
            if (!session.IsConnected)
                throw new InvalidOperationException("SFTP is not connected.");

            if (mode is FolderTransferMode.TarGz or FolderTransferMode.Zip)
            {
                await DownloadFolderCompressedInternalAsync(session, item, cleanRemotePath, localSaveDir, mode, autoExtract, token, progress);
            }
            else
            {
                if (session.SftpClient == null) throw new InvalidOperationException("SFTP is not connected.");
                long size = 0;
                try { size = session.SftpClient.GetAttributes(cleanRemotePath)?.Size ?? 0; } catch { }
                item.TotalBytes = Math.Max(1, size);

                var dir = Path.GetDirectoryName(localTargetPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using var fs = File.Create(localTargetPath);
                var client = session.CreateDedicatedSftpClient();
                if (client != null)
                {
                    using (client)
                    {
                        await StreamDownloadAsync(client, cleanRemotePath, fs, token, progress.Report);
                    }
                }
                else
                {
                    await session.SftpLock.WaitAsync(token);
                    try
                    {
                        await StreamDownloadAsync(session.SftpClient, cleanRemotePath, fs, token, progress.Report);
                    }
                    finally
                    {
                        session.SftpLock.Release();
                    }
                }
            }
        };

        Transfers.Insert(0, item);
        _ = ProcessItemAsync(item);
        return item;
    }

    public TransferItem EnqueueUploadFolder(SshSession session, string localDirPath, string remoteDirPath, FolderTransferMode mode, bool autoExtract = true)
    {
        var dirInfo = new DirectoryInfo(localDirPath);
        var folderName = dirInfo.Name;
        var remoteTargetDir = $"{remoteDirPath.TrimEnd('/')}/{folderName}";

        var item = new TransferItem
        {
            FileName = folderName,
            LocalPath = localDirPath,
            RemotePath = remoteTargetDir,
            Direction = TransferDirection.Upload,
            IsDirectory = true,
            IsCompressed = mode != FolderTransferMode.Standard,
            CompressionFormat = mode == FolderTransferMode.TarGz ? "tar.gz" : (mode == FolderTransferMode.Zip ? "zip" : string.Empty),
            SessionTarget = $"{session.Username}@{session.Host}",
            Status = TransferStatus.Queued,
            StartTime = DateTime.Now
        };

        item.WorkerAction = async (token, progress) =>
        {
            if (!session.IsConnected)
                throw new InvalidOperationException("SFTP is not connected.");

            if (mode is FolderTransferMode.TarGz or FolderTransferMode.Zip)
            {
                await UploadFolderCompressedInternalAsync(session, item, localDirPath, remoteDirPath, mode, autoExtract, token, progress);
            }
            else
            {
                await UploadFolderRecursiveInternalAsync(session, item, localDirPath, remoteTargetDir, token, progress);
            }
        };

        Transfers.Insert(0, item);
        _ = ProcessItemAsync(item);
        return item;
    }

    public TransferItem EnqueueDownloadFolder(SshSession session, string remoteDirPath, string localSaveDir, FolderTransferMode mode, bool autoExtract = true)
    {
        var cleanRemotePath = remoteDirPath.TrimEnd('/');
        var folderName = cleanRemotePath.Split('/').Last();
        var localTargetDir = Path.Combine(localSaveDir, folderName);

        var item = new TransferItem
        {
            FileName = folderName,
            LocalPath = localTargetDir,
            RemotePath = cleanRemotePath,
            Direction = TransferDirection.Download,
            IsDirectory = true,
            IsCompressed = mode != FolderTransferMode.Standard,
            CompressionFormat = mode == FolderTransferMode.TarGz ? "tar.gz" : (mode == FolderTransferMode.Zip ? "zip" : string.Empty),
            SessionTarget = $"{session.Username}@{session.Host}",
            Status = TransferStatus.Queued,
            StartTime = DateTime.Now
        };

        item.WorkerAction = async (token, progress) =>
        {
            if (!session.IsConnected)
                throw new InvalidOperationException("SFTP is not connected.");

            if (mode is FolderTransferMode.TarGz or FolderTransferMode.Zip)
            {
                await DownloadFolderCompressedInternalAsync(session, item, cleanRemotePath, localSaveDir, mode, autoExtract, token, progress);
            }
            else
            {
                await DownloadFolderRecursiveInternalAsync(session, item, cleanRemotePath, localTargetDir, token, progress);
            }
        };

        Transfers.Insert(0, item);
        _ = ProcessItemAsync(item);
        return item;
    }

    private async Task UploadFolderCompressedInternalAsync(
        SshSession session, TransferItem item, string localDirPath, string remoteDirPath,
        FolderTransferMode mode, bool autoExtract, CancellationToken token, IProgress<long> progress)
    {
        var ext = mode == FolderTransferMode.TarGz ? ".tar.gz" : ".zip";
        var tempLocalArchive = Path.Combine(Path.GetTempPath(), $"jx_up_{Guid.NewGuid():N}{ext}");
        var remoteArchiveFileName = $"jx_up_{Guid.NewGuid():N}{ext}";
        var remoteArchivePath = $"{remoteDirPath.TrimEnd('/')}/{remoteArchiveFileName}";

        try
        {
            item.CurrentSubFileName = "正在本地压缩打包...";
            token.ThrowIfCancellationRequested();

            if (mode == FolderTransferMode.TarGz)
            {
                await ArchiveHelper.CreateTarGzAsync(localDirPath, tempLocalArchive, token);
            }
            else
            {
                await ArchiveHelper.CreateZipAsync(localDirPath, tempLocalArchive, token);
            }

            token.ThrowIfCancellationRequested();
            var archiveInfo = new FileInfo(tempLocalArchive);
            item.TotalBytes = Math.Max(1, archiveInfo.Length);
            item.CurrentSubFileName = "正在传输压缩包...";

            // Upload the single archive
            using var localStream = File.OpenRead(tempLocalArchive);
            SftpClient? dedicatedClient = null;
            try { dedicatedClient = session.CreateDedicatedSftpClient(); } catch { }

            if (dedicatedClient != null)
            {
                using (dedicatedClient)
                {
                    await StreamUploadAsync(dedicatedClient, localStream, remoteArchivePath, token, progress.Report);
                }
            }
            else
            {
                if (session.SftpClient == null) throw new InvalidOperationException("SFTP is not connected.");
                await session.SftpLock.WaitAsync(token);
                try
                {
                    await StreamUploadAsync(session.SftpClient, localStream, remoteArchivePath, token, progress.Report);
                }
                finally
                {
                    session.SftpLock.Release();
                }
            }

            if (autoExtract)
            {
                item.CurrentSubFileName = "正在服务器解压...";
                token.ThrowIfCancellationRequested();

                string extractCmd = mode == FolderTransferMode.TarGz
                    ? $"tar -xzf \"{remoteArchivePath}\" -C \"{remoteDirPath}\" && rm -f \"{remoteArchivePath}\""
                    : $"unzip -q -o \"{remoteArchivePath}\" -d \"{remoteDirPath}\" && rm -f \"{remoteArchivePath}\"";

                await session.ExecuteCommandAsync(extractCmd, TimeSpan.FromMinutes(10));
                item.CurrentSubFileName = "解压完成";
            }
            else
            {
                item.CurrentSubFileName = "压缩包传输完成";
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempLocalArchive))
                    File.Delete(tempLocalArchive);
            }
            catch { }
        }
    }

    private async Task DownloadFolderCompressedInternalAsync(
        SshSession session, TransferItem item, string remoteDirPath, string localSaveDir,
        FolderTransferMode mode, bool autoExtract, CancellationToken token, IProgress<long> progress)
    {
        var cleanRemote = remoteDirPath.TrimEnd('/');
        var folderName = cleanRemote.Split('/').Last();
        var parentRemote = cleanRemote.Substring(0, cleanRemote.LastIndexOf('/'));
        if (string.IsNullOrEmpty(parentRemote)) parentRemote = "/";

        var ext = mode == FolderTransferMode.TarGz ? ".tar.gz" : ".zip";
        var remoteTmpArchive = $"/tmp/jx_dl_{Guid.NewGuid():N}{ext}";
        var localTempArchive = Path.Combine(Path.GetTempPath(), $"jx_dl_{Guid.NewGuid():N}{ext}");

        try
        {
            item.CurrentSubFileName = "正在服务器打包...";
            token.ThrowIfCancellationRequested();

            // 1. Pack on remote
            string packCmd = mode == FolderTransferMode.TarGz
                ? $"tar -czf \"{remoteTmpArchive}\" -C \"{parentRemote}\" \"{folderName}\""
                : $"cd \"{parentRemote}\" && zip -r -q \"{remoteTmpArchive}\" \"{folderName}\"";

            await session.ExecuteCommandAsync(packCmd, TimeSpan.FromMinutes(10));
            token.ThrowIfCancellationRequested();

            // 2. Query file size
            long archiveSize = 0;
            SftpClient? dedicatedClient = null;
            try { dedicatedClient = session.CreateDedicatedSftpClient(); } catch { }

            if (dedicatedClient != null)
            {
                using (dedicatedClient)
                {
                    try { archiveSize = dedicatedClient.GetAttributes(remoteTmpArchive)?.Size ?? 0; } catch { }
                    item.TotalBytes = Math.Max(1, archiveSize);
                    item.CurrentSubFileName = "正在下载压缩包...";

                    using var localStream = File.Create(localTempArchive);
                    await StreamDownloadAsync(dedicatedClient, remoteTmpArchive, localStream, token, progress.Report);
                }
            }
            else
            {
                if (session.SftpClient == null) throw new InvalidOperationException("SFTP is not connected.");
                await session.SftpLock.WaitAsync(token);
                try
                {
                    try { archiveSize = session.SftpClient.GetAttributes(remoteTmpArchive)?.Size ?? 0; } catch { }
                    item.TotalBytes = Math.Max(1, archiveSize);
                    item.CurrentSubFileName = "正在下载压缩包...";

                    using var localStream = File.Create(localTempArchive);
                    await StreamDownloadAsync(session.SftpClient, remoteTmpArchive, localStream, token, progress.Report);
                }
                finally
                {
                    session.SftpLock.Release();
                }
            }

            token.ThrowIfCancellationRequested();

            // 3. Extract locally if autoExtract
            if (autoExtract)
            {
                item.CurrentSubFileName = "正在本地解压...";
                if (!Directory.Exists(localSaveDir))
                    Directory.CreateDirectory(localSaveDir);

                if (mode == FolderTransferMode.TarGz)
                {
                    await ArchiveHelper.ExtractTarGzAsync(localTempArchive, localSaveDir, token);
                }
                else
                {
                    await ArchiveHelper.ExtractZipAsync(localTempArchive, localSaveDir, token);
                }
                item.CurrentSubFileName = "解压完成";
            }
            else
            {
                var userTargetPath = Path.Combine(localSaveDir, $"{folderName}{ext}");
                File.Copy(localTempArchive, userTargetPath, true);
                item.CurrentSubFileName = "压缩包下载完成";
            }
        }
        finally
        {
            try
            {
                if (File.Exists(localTempArchive))
                    File.Delete(localTempArchive);
            }
            catch { }

            try
            {
                _ = session.ExecuteCommandAsync($"rm -f \"{remoteTmpArchive}\"");
            }
            catch { }
        }
    }

    private async Task UploadFolderRecursiveInternalAsync(
        SshSession session, TransferItem item, string localDirPath, string remoteTargetDir,
        CancellationToken token, IProgress<long> progress)
    {
        item.CurrentSubFileName = "正在扫描本地文件...";
        token.ThrowIfCancellationRequested();

        var localDir = new DirectoryInfo(localDirPath);
        var allFiles = localDir.GetFiles("*", SearchOption.AllDirectories);
        var allDirs = localDir.GetDirectories("*", SearchOption.AllDirectories);

        item.TotalFiles = allFiles.Length;
        long totalBytes = allFiles.Sum(f => f.Length);
        item.TotalBytes = Math.Max(1, totalBytes);
        item.TransferredFiles = 0;

        SftpClient? dedicatedClient = null;
        try { dedicatedClient = session.CreateDedicatedSftpClient(); } catch { }

        var client = dedicatedClient ?? session.SftpClient;
        if (client == null) throw new InvalidOperationException("SFTP is not connected.");

        bool needLock = dedicatedClient == null;
        if (needLock) await session.SftpLock.WaitAsync(token);

        try
        {
            // Ensure remote root folder exists
            EnsureRemoteDirectoryExists(client, remoteTargetDir);

            // Create subdirectories
            foreach (var d in allDirs)
            {
                token.ThrowIfCancellationRequested();
                var relPath = Path.GetRelativePath(localDirPath, d.FullName).Replace('\\', '/');
                var remoteSubDir = $"{remoteTargetDir}/{relPath}";
                EnsureRemoteDirectoryExists(client, remoteSubDir);
            }

            long totalBytesAccumulator = 0;

            // Upload files
            foreach (var f in allFiles)
            {
                token.ThrowIfCancellationRequested();
                var relPath = Path.GetRelativePath(localDirPath, f.FullName).Replace('\\', '/');
                item.CurrentSubFileName = relPath;

                var remoteFilePath = $"{remoteTargetDir}/{relPath}";
                using var fileStream = File.OpenRead(f.FullName);

                await StreamUploadAsync(client, fileStream, remoteFilePath, token, bytesRead =>
                {
                    progress.Report(totalBytesAccumulator + bytesRead);
                });

                totalBytesAccumulator += f.Length;
                item.TransferredFiles++;
            }
        }
        finally
        {
            if (needLock) session.SftpLock.Release();
            dedicatedClient?.Dispose();
        }
    }

    private async Task DownloadFolderRecursiveInternalAsync(
        SshSession session, TransferItem item, string remoteDirPath, string localTargetDir,
        CancellationToken token, IProgress<long> progress)
    {
        item.CurrentSubFileName = "正在扫描远程目录...";
        token.ThrowIfCancellationRequested();

        SftpClient? dedicatedClient = null;
        try { dedicatedClient = session.CreateDedicatedSftpClient(); } catch { }

        var client = dedicatedClient ?? session.SftpClient;
        if (client == null) throw new InvalidOperationException("SFTP is not connected.");

        bool needLock = dedicatedClient == null;
        if (needLock) await session.SftpLock.WaitAsync(token);

        try
        {
            var files = new List<(string remotePath, string relPath, long size)>();
            var dirs = new List<string>();

            ListRemoteFilesRecursively(client, remoteDirPath, "", files, dirs, token);

            item.TotalFiles = files.Count;
            long totalBytes = files.Sum(f => f.size);
            item.TotalBytes = Math.Max(1, totalBytes);
            item.TransferredFiles = 0;

            if (!Directory.Exists(localTargetDir))
                Directory.CreateDirectory(localTargetDir);

            foreach (var d in dirs)
            {
                token.ThrowIfCancellationRequested();
                var localSub = Path.Combine(localTargetDir, d.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(localSub))
                    Directory.CreateDirectory(localSub);
            }

            long totalBytesAccumulator = 0;

            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                item.CurrentSubFileName = file.relPath;

                var localFilePath = Path.Combine(localTargetDir, file.relPath.Replace('/', Path.DirectorySeparatorChar));
                var localParent = Path.GetDirectoryName(localFilePath);
                if (!string.IsNullOrEmpty(localParent) && !Directory.Exists(localParent))
                    Directory.CreateDirectory(localParent);

                using var fileStream = File.Create(localFilePath);
                await StreamDownloadAsync(client, file.remotePath, fileStream, token, bytesRead =>
                {
                    progress.Report(totalBytesAccumulator + bytesRead);
                });

                totalBytesAccumulator += file.size;
                item.TransferredFiles++;
            }
        }
        finally
        {
            if (needLock) session.SftpLock.Release();
            dedicatedClient?.Dispose();
        }
    }

    private static void EnsureRemoteDirectoryExists(SftpClient sftp, string remoteDirPath)
    {
        var normalized = remoteDirPath.Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrEmpty(normalized) || normalized == "/") return;

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "";

        foreach (var part in parts)
        {
            current += "/" + part;
            try
            {
                if (!sftp.Exists(current))
                {
                    sftp.CreateDirectory(current);
                }
            }
            catch
            {
                // Directory may already exist or concurrent creation
            }
        }
    }

    private static void ListRemoteFilesRecursively(
        SftpClient sftp,
        string remoteDirPath,
        string relativeBase,
        List<(string remotePath, string relPath, long size)> files,
        List<string> dirs,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var list = sftp.ListDirectory(remoteDirPath);
        foreach (var entry in list)
        {
            token.ThrowIfCancellationRequested();
            if (entry.Name == "." || entry.Name == "..") continue;

            var subRel = string.IsNullOrEmpty(relativeBase) ? entry.Name : $"{relativeBase}/{entry.Name}";
            if (entry.IsDirectory)
            {
                dirs.Add(subRel);
                ListRemoteFilesRecursively(sftp, entry.FullName, subRel, files, dirs, token);
            }
            else
            {
                files.Add((entry.FullName, subRel, entry.Length));
            }
        }
    }

    private static async Task StreamUploadAsync(
        SftpClient sftp, Stream localStream, string remotePath,
        CancellationToken token, Action<long> onBytesTransferred)
    {
        using var remoteStream = sftp.OpenWrite(remotePath);
        var buffer = new byte[65536];
        int read;
        long transferred = 0;

        while ((read = await localStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
        {
            token.ThrowIfCancellationRequested();
            remoteStream.Write(buffer, 0, read);
            transferred += read;
            onBytesTransferred(transferred);
        }
    }

    private static async Task StreamDownloadAsync(
        SftpClient sftp, string remotePath, Stream localStream,
        CancellationToken token, Action<long> onBytesTransferred)
    {
        using var remoteStream = sftp.OpenRead(remotePath);
        var buffer = new byte[65536];
        int read;
        long transferred = 0;

        while ((read = await remoteStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
        {
            token.ThrowIfCancellationRequested();
            await localStream.WriteAsync(buffer, 0, read, token);
            transferred += read;
            onBytesTransferred(transferred);
        }
    }

    private async Task ProcessItemAsync(TransferItem item)
    {
        await _concurrencyLimiter.WaitAsync();

        // The user may have cancelled while we waited for a concurrency slot.
        if (item.Status == TransferStatus.Canceled)
        {
            _concurrencyLimiter.Release();
            return;
        }

        var cts = new CancellationTokenSource();
        item.Cts = cts;
        item.Status = TransferStatus.Running;

        var lastTime = DateTime.Now;
        long lastBytes = 0;

        var progress = new Progress<long>(bytes =>
        {
            item.TransferredBytes = bytes;
            var now = DateTime.Now;
            var sec = (now - lastTime).TotalSeconds;
            if (sec >= 0.5)
            {
                item.SpeedBytesPerSec = (bytes - lastBytes) / sec;
                lastTime = now;
                lastBytes = bytes;
            }
        });

        try
        {
            if (item.WorkerAction != null)
            {
                await Task.Run(async () => await item.WorkerAction(cts.Token, progress));
            }

            item.TransferredBytes = item.TotalBytes;
            item.Status = TransferStatus.Completed;
            item.SpeedBytesPerSec = 0;
        }
        catch (OperationCanceledException)
        {
            item.Status = TransferStatus.Canceled;
            item.SpeedBytesPerSec = 0;
        }
        catch (Exception ex)
        {
            item.Status = TransferStatus.Failed;
            item.ErrorMessage = ex.Message;
            item.SpeedBytesPerSec = 0;
        }
        finally
        {
            cts.Dispose();
            item.Cts = null;
            _concurrencyLimiter.Release();
        }
    }

    public void CancelTransfer(Guid transferId)
    {
        var item = Transfers.FirstOrDefault(t => t.Id == transferId);
        if (item != null && item.Status is TransferStatus.Queued or TransferStatus.Running)
        {
            if (item.Cts != null)
            {
                item.Cts.Cancel();
            }
            else
            {
                item.Status = TransferStatus.Canceled;
            }
        }
    }

    public void RetryTransfer(Guid transferId)
    {
        var item = Transfers.FirstOrDefault(t => t.Id == transferId);
        if (item != null && item.Status is TransferStatus.Failed or TransferStatus.Canceled)
        {
            item.Status = TransferStatus.Queued;
            item.TransferredBytes = 0;
            item.ErrorMessage = string.Empty;
            _ = ProcessItemAsync(item);
        }
    }

    public void RemoveTransfer(Guid transferId)
    {
        var item = Transfers.FirstOrDefault(t => t.Id == transferId);
        if (item != null)
        {
            if (item.Status == TransferStatus.Running)
            {
                item.Cts?.Cancel();
            }
            Transfers.Remove(item);
        }
    }

    public void ClearFinished()
    {
        var finished = Transfers.Where(t => t.Status is TransferStatus.Completed or TransferStatus.Failed or TransferStatus.Canceled).ToList();
        foreach (var item in finished)
        {
            Transfers.Remove(item);
        }
    }
}
