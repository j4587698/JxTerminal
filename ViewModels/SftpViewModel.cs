using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JxTerminal.Models;
using Renci.SshNet.Sftp;

namespace JxTerminal.ViewModels;

public partial class SftpViewModel : ObservableObject
{
    private readonly SshSession _session;

    /// <summary>Injected UI helpers so this VM never touches Avalonia visual types.</summary>
    private readonly Func<string, string, Task<bool>>? _confirm;
    private readonly Func<string, string, Task<string?>>? _prompt;
    private readonly Action<string>? _openInTerminal;
    private readonly Func<Task<string?>>? _getTerminalCwd;
    private readonly TerminalSettings? _settings;
    private IStorageProvider? _storageProvider;

    [ObservableProperty]
    private string _currentPath = "/";

    [ObservableProperty]
    private bool _isLoading;

    public bool IsLoadingAndEmpty => IsLoading && Files.Count == 0;

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsLoadingAndEmpty));

    [ObservableProperty]
    private SftpFileItem? _selectedFile;

    /// <summary>
    /// Last operation failure, shown as an inline banner. Empty when everything is fine.
    /// </summary>
    [ObservableProperty]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    // Active transfers tracking (non-blocking)
    [ObservableProperty]
    private int _activeTransferCount;

    [ObservableProperty]
    private double _overallTransferProgress;

    [ObservableProperty]
    private bool _overallTransferIndeterminate = true;

    public bool HasActiveTransfers => ActiveTransferCount > 0;

    public string ActiveTransferSummary
    {
        get
        {
            var l = Services.LocalizationService.Instance;
            return string.Format(l["ActiveTransfersCountFormat"], ActiveTransferCount);
        }
    }

    partial void OnActiveTransferCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasActiveTransfers));
        OnPropertyChanged(nameof(ActiveTransferSummary));
    }

    // Transfer state (retained for backward compatibility)
    [ObservableProperty]
    private bool _isTransferring;

    [ObservableProperty]
    private double _transferProgress; // 0..100, -1 = indeterminate

    public bool TransferIndeterminate => TransferProgress < 0;

    partial void OnTransferProgressChanged(double value) => OnPropertyChanged(nameof(TransferIndeterminate));

    [ObservableProperty]
    private string _transferLabel = "";

    private void ReportError(string key, string detail)
    {
        var text = string.Format(Services.LocalizationService.Instance[key], detail);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ErrorMessage = text);
    }

    [RelayCommand]
    private void DismissError() => ErrorMessage = "";

    public ObservableCollection<SftpFileItem> Files { get; } = new();

    private readonly string _sessionTarget;

    public SftpViewModel(
        SshSession session,
        Func<string, string, Task<bool>>? confirm = null,
        Func<string, string, Task<string?>>? prompt = null,
        Action<string>? openInTerminal = null,
        Func<Task<string?>>? getTerminalCwd = null,
        TerminalSettings? settings = null)
    {
        _session = session;
        _confirm = confirm;
        _prompt = prompt;
        _openInTerminal = openInTerminal;
        _getTerminalCwd = getTerminalCwd;
        _settings = settings;
        _sessionTarget = $"{session.Username}@{session.Host}";

        var transferService = Services.Transfer.TransferService.Instance;
        transferService.Transfers.CollectionChanged += OnTransfersCollectionChanged;
        foreach (var item in transferService.Transfers)
        {
            if (item.SessionTarget == _sessionTarget)
                item.PropertyChanged += OnTransferItemPropertyChanged;
        }
        UpdateActiveTransferCount();
    }

    private void OnTransfersCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (TransferItem item in e.NewItems)
            {
                if (item.SessionTarget == _sessionTarget)
                {
                    item.PropertyChanged += OnTransferItemPropertyChanged;
                }
            }
        }
        if (e.OldItems != null)
        {
            foreach (TransferItem item in e.OldItems)
            {
                if (item.SessionTarget == _sessionTarget)
                {
                    item.PropertyChanged -= OnTransferItemPropertyChanged;
                }
            }
        }
        UpdateActiveTransferCount();
    }

    private void OnTransferItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TransferItem.Status) ||
            e.PropertyName == nameof(TransferItem.TransferredBytes) ||
            e.PropertyName == nameof(TransferItem.TotalBytes))
        {
            UpdateActiveTransferCount();
            if (sender is TransferItem item && e.PropertyName == nameof(TransferItem.Status))
            {
                if (item.Status == TransferStatus.Completed && item.Direction == TransferDirection.Upload)
                {
                    var parentDir = Path.GetDirectoryName(item.RemotePath)?.Replace('\\', '/') ?? "/";
                    if (NormalizePath(parentDir) == NormalizePath(CurrentPath))
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = RefreshFiles());
                    }
                }
                else if (item.Status == TransferStatus.Failed)
                {
                    ReportError(
                        item.Direction == TransferDirection.Upload ? "SftpUploadFailed" : "SftpDownloadFailed",
                        item.ErrorMessage);
                }
            }
        }
    }

    private void UpdateActiveTransferCount()
    {
        var sessionTransfers = Services.Transfer.TransferService.Instance.Transfers
            .Where(t => t.SessionTarget == _sessionTarget && t.Status is TransferStatus.Queued or TransferStatus.Running)
            .ToList();

        var count = sessionTransfers.Count;
        double progress = 0;
        bool indeterminate = true;

        if (count > 0)
        {
            long totalBytes = 0;
            long transferredBytes = 0;
            bool hasDefiniteTotal = false;

            foreach (var t in sessionTransfers)
            {
                if (t.TotalBytes > 0)
                {
                    totalBytes += t.TotalBytes;
                    transferredBytes += t.TransferredBytes;
                    hasDefiniteTotal = true;
                }
            }

            if (hasDefiniteTotal && totalBytes > 0)
            {
                progress = Math.Clamp((double)transferredBytes / totalBytes * 100.0, 0, 100.0);
                indeterminate = false;
            }
        }

        void Apply()
        {
            ActiveTransferCount = count;
            OverallTransferProgress = progress;
            OverallTransferIndeterminate = indeterminate;
        }

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Apply();
            Avalonia.Threading.Dispatcher.UIThread.Post(Apply);
        }
    }

    private static string NormalizePath(string path)
    {
        var p = path.Replace('\\', '/').TrimEnd('/');
        return string.IsNullOrEmpty(p) ? "/" : p;
    }

    [RelayCommand]
    public void NavigateToTransfer()
    {
        MainWindowViewModel.Instance?.NavigateToTransfer();
    }

    /// <summary>Called by the view once it can resolve the platform storage provider.</summary>
    public void SetStorageProvider(IStorageProvider storageProvider) => _storageProvider = storageProvider;

    /// <summary>Synchronizes the SFTP path with the terminal's tracked directory.</summary>
    public void SetCurrentPathFromTerminal(string path, bool autoRefresh = true)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var clean = path.TrimEnd('/');
        if (string.IsNullOrEmpty(clean)) clean = "/";
        if (CurrentPath == clean) return;

        CurrentPath = clean;
        if (autoRefresh && _session.IsConnected)
        {
            _ = RefreshFiles();
        }
    }

    [RelayCommand]
    public async Task SyncWithTerminal()
    {
        if (_getTerminalCwd != null)
        {
            var cwd = await _getTerminalCwd();
            if (!string.IsNullOrWhiteSpace(cwd))
            {
                SetCurrentPathFromTerminal(cwd, autoRefresh: true);
                return;
            }
        }
        await RefreshFiles();
    }

    [RelayCommand]
    public void OpenInTerminal(SftpFileItem? target = null)
    {
        var item = target ?? SelectedFile;
        string targetPath;
        if (item != null)
        {
            targetPath = item.IsDirectory ? item.Path : CurrentPath;
        }
        else
        {
            targetPath = CurrentPath;
        }
        _openInTerminal?.Invoke(targetPath);
    }

    [RelayCommand]
    public void OpenCurrentPathInTerminal()
    {
        _openInTerminal?.Invoke(CurrentPath);
    }

    [RelayCommand]
    public async Task CopyPath(SftpFileItem? target = null)
    {
        var item = target ?? SelectedFile;
        if (item != null)
        {
            await Services.ClipboardService.SetTextAsync(item.Path);
        }
    }

    [RelayCommand]
    public async Task CopyName(SftpFileItem? target = null)
    {
        var item = target ?? SelectedFile;
        if (item != null)
        {
            await Services.ClipboardService.SetTextAsync(item.Name);
        }
    }

    [RelayCommand]
    public async Task CopyCurrentPath()
    {
        await Services.ClipboardService.SetTextAsync(CurrentPath);
    }

    [RelayCommand]
    public async Task RefreshFiles()
    {
        if (_session.SftpClient != null && _session.IsConnected)
        {
            IsLoading = true;
            await Task.Run(() =>
            {
                // SftpClient is not thread-safe: serialize with transfers and other ops.
                _session.SftpLock.Wait();
                try
                {
                    var files = _session.SftpClient.ListDirectory(CurrentPath);
                    var items = files
                        .Where(f => f.Name != ".") // Skip current dir marker
                        .OrderByDescending(f => f.IsDirectory)
                        .ThenBy(f => f.Name)
                        .Select(f => new SftpFileItem(f))
                        .ToList();

                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        Files.Clear();
                        foreach (var item in items)
                        {
                            Files.Add(item);
                        }
                        ErrorMessage = "";
                        OnPropertyChanged(nameof(IsLoadingAndEmpty));
                    });
                }
                catch (Exception ex)
                {
                    ReportError("SftpListFailed", ex.Message);
                }
                finally
                {
                    _session.SftpLock.Release();
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        IsLoading = false;
                        OnPropertyChanged(nameof(IsLoadingAndEmpty));
                    });
                }
            });
        }
    }

    [RelayCommand]
    public void Navigate(SftpFileItem item)
    {
        if (item.IsDirectory)
        {
            if (item.Name == "..")
            {
                if (CurrentPath != "/")
                {
                    var parts = CurrentPath.TrimEnd('/').Split('/');
                    if (parts.Length > 1)
                    {
                        CurrentPath = string.Join("/", parts.Take(parts.Length - 1));
                        if (string.IsNullOrEmpty(CurrentPath)) CurrentPath = "/";
                    }
                    else
                    {
                        CurrentPath = "/";
                    }
                }
            }
            else
            {
                CurrentPath = item.Path;
            }
            RefreshFilesCommand.Execute(null);
        }
    }

    [RelayCommand]
    public async Task NavigateUp()
    {
        if (CurrentPath == "/") return;
        var parts = CurrentPath.TrimEnd('/').Split('/');
        if (parts.Length > 1)
        {
            CurrentPath = string.Join("/", parts.Take(parts.Length - 1));
            if (string.IsNullOrEmpty(CurrentPath)) CurrentPath = "/";
        }
        else
        {
            CurrentPath = "/";
        }
        await RefreshFiles();
    }

    private static string JoinRemote(string dir, string name)
        => $"{dir.TrimEnd('/')}/{name}";

    /// <summary>
    /// A remote entry name must be a single path segment; reject separators and
    /// parent-traversal so dialogs cannot escape the current directory.
    /// </summary>
    private static bool IsValidRemoteName(string name)
        => !string.IsNullOrWhiteSpace(name)
           && name != "." && name != ".."
           && name.IndexOf('/') < 0 && name.IndexOf('\\') < 0;

    // ---------------- Directory operations ----------------

    [RelayCommand]
    public async Task CreateFolder()
    {
        var l = Services.LocalizationService.Instance;
        if (_prompt == null) return;

        var name = await _prompt(l["NewFolder"], l["PromptFolderName"]);
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!IsValidRemoteName(name))
        {
            ReportError("SftpInvalidName", name);
            return;
        }

        try
        {
            IsLoading = true;
            await Task.Run(() =>
            {
                _session.SftpLock.Wait();
                try { _session.SftpClient?.CreateDirectory(JoinRemote(CurrentPath, name)); }
                finally { _session.SftpLock.Release(); }
            });
            await RefreshFiles();
        }
        catch (Exception ex)
        {
            ReportError("SftpMkdirFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task CreateFile()
    {
        var l = Services.LocalizationService.Instance;
        if (_prompt == null) return;

        var name = await _prompt(l["NewFile"], l["PromptFileName"]);
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!IsValidRemoteName(name))
        {
            ReportError("SftpInvalidName", name);
            return;
        }

        try
        {
            IsLoading = true;
            await Task.Run(() =>
            {
                _session.SftpLock.Wait();
                try
                {
                    using var stream = _session.SftpClient?.Create(JoinRemote(CurrentPath, name));
                }
                finally { _session.SftpLock.Release(); }
            });
            await RefreshFiles();
        }
        catch (Exception ex)
        {
            ReportError("SftpCreateFileFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task DeleteSelected(SftpFileItem? target = null)
    {
        var l = Services.LocalizationService.Instance;
        var item = target ?? SelectedFile;
        if (item == null || item.Name == ".." || _confirm == null) return;

        if (!await _confirm(l["DeleteConfirmTitle"], string.Format(l["DeleteConfirmMessage"], item.Name)))
            return;

        try
        {
            IsLoading = true;
            await Task.Run(() =>
            {
                if (_session.SftpClient == null) return;
                _session.SftpLock.Wait();
                try
                {
                    if (item.IsDirectory) _session.SftpClient.DeleteDirectory(item.Path);
                    else _session.SftpClient.DeleteFile(item.Path);
                }
                finally { _session.SftpLock.Release(); }
            });
            await RefreshFiles();
        }
        catch (Exception ex)
        {
            ReportError("SftpDeleteFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RenameSelected(SftpFileItem? target = null)
    {
        var l = Services.LocalizationService.Instance;
        var item = target ?? SelectedFile;
        if (item == null || item.Name == ".." || _prompt == null) return;

        var newName = await _prompt(l["RenameItem"], l["PromptNewName"]);
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;
        if (!IsValidRemoteName(newName))
        {
            ReportError("SftpInvalidName", newName);
            return;
        }

        try
        {
            IsLoading = true;
            await Task.Run(() =>
            {
                _session.SftpLock.Wait();
                try { _session.SftpClient?.RenameFile(item.Path, JoinRemote(CurrentPath, newName)); }
                finally { _session.SftpLock.Release(); }
            });
            await RefreshFiles();
        }
        catch (Exception ex)
        {
            ReportError("SftpRenameFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ---------------- Transfers ----------------
    // All transfers are processed asynchronously in the background by TransferService
    // and displayed in the dedicated Transfer Center tab. SFTP file browsing remains
    // completely responsive and non-blocking.

    /// <summary>Cancels active transfers for this session.</summary>
    [RelayCommand]
    private void CancelTransfer()
    {
        var activeTransfers = Services.Transfer.TransferService.Instance.Transfers
            .Where(t => t.SessionTarget == _sessionTarget && t.Status is TransferStatus.Queued or TransferStatus.Running)
            .ToList();
        foreach (var t in activeTransfers)
        {
            Services.Transfer.TransferService.Instance.CancelTransfer(t.Id);
        }
    }

    [RelayCommand]
    public async Task DownloadFile(SftpFileItem? target = null)
    {
        var l = Services.LocalizationService.Instance;
        var fileItem = target ?? SelectedFile;
        if (fileItem == null || _storageProvider == null || _session.SftpClient == null)
            return;

        if (fileItem.IsDirectory)
        {
            var mode = _settings?.FolderTransferMode ?? FolderTransferMode.TarGz;
            var autoExtract = _settings?.AutoExtractAfterTransfer ?? true;
            await DownloadFolderWithModeAsync(fileItem, mode, autoExtract);
            return;
        }

        try
        {
            var file = await _storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = l["DownloadFile"],
                SuggestedFileName = fileItem.Name
            });

            if (file == null) return;

            var localPath = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(localPath))
            {
                ReportError("SftpDownloadFailed", "local path unavailable");
                return;
            }

            Services.Transfer.TransferService.Instance.EnqueueDownload(
                _session, fileItem.Path, localPath, fileItem.Size);
        }
        catch (Exception ex)
        {
            ReportError("SftpDownloadFailed", ex.Message);
        }
    }

    [RelayCommand]
    public Task DownloadItem(SftpFileItem? target = null) => DownloadFile(target);

    [RelayCommand]
    public async Task DownloadItemCompressedTar(SftpFileItem? target = null)
    {
        var fileItem = target ?? SelectedFile;
        if (fileItem == null) return;
        if (fileItem.IsDirectory)
            await DownloadFolderCompressedTar(fileItem);
        else
            await DownloadFileCompressedTar(fileItem);
    }

    [RelayCommand]
    public async Task DownloadItemCompressedZip(SftpFileItem? target = null)
    {
        var fileItem = target ?? SelectedFile;
        if (fileItem == null) return;
        if (fileItem.IsDirectory)
            await DownloadFolderCompressedZip(fileItem);
        else
            await DownloadFileCompressedZip(fileItem);
    }

    [RelayCommand]
    public async Task DownloadFileCompressedTar(SftpFileItem? target = null)
    {
        var fileItem = target ?? SelectedFile;
        if (fileItem == null || fileItem.IsDirectory) return;
        var autoExtract = _settings?.AutoExtractAfterTransfer ?? true;
        await DownloadFileCompressedWithModeAsync(fileItem, FolderTransferMode.TarGz, autoExtract);
    }

    [RelayCommand]
    public async Task DownloadFileCompressedZip(SftpFileItem? target = null)
    {
        var fileItem = target ?? SelectedFile;
        if (fileItem == null || fileItem.IsDirectory) return;
        var autoExtract = _settings?.AutoExtractAfterTransfer ?? true;
        await DownloadFileCompressedWithModeAsync(fileItem, FolderTransferMode.Zip, autoExtract);
    }

    private async Task DownloadFileCompressedWithModeAsync(SftpFileItem fileItem, FolderTransferMode mode, bool autoExtract)
    {
        var l = Services.LocalizationService.Instance;
        if (_storageProvider == null || _session.SftpClient == null) return;

        try
        {
            var folders = await _storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = l["DownloadFile"],
                AllowMultiple = false
            });

            if (folders == null || folders.Count == 0) return;
            var localSaveDir = folders[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(localSaveDir))
            {
                ReportError("SftpDownloadFailed", "local path unavailable");
                return;
            }

            Services.Transfer.TransferService.Instance.EnqueueDownloadFileCompressed(
                _session, fileItem.Path, localSaveDir, mode, autoExtract);
        }
        catch (Exception ex)
        {
            ReportError("SftpDownloadFailed", ex.Message);
        }
    }

    [RelayCommand]
    public async Task UploadFileCompressedTar()
    {
        var autoExtract = _settings?.AutoExtractAfterTransfer ?? true;
        await UploadFileCompressedWithModeAsync(FolderTransferMode.TarGz, autoExtract);
    }

    [RelayCommand]
    public async Task UploadFileCompressedZip()
    {
        var autoExtract = _settings?.AutoExtractAfterTransfer ?? true;
        await UploadFileCompressedWithModeAsync(FolderTransferMode.Zip, autoExtract);
    }

    private async Task UploadFileCompressedWithModeAsync(FolderTransferMode mode, bool autoExtract)
    {
        var l = Services.LocalizationService.Instance;
        if (_storageProvider == null || _session.SftpClient == null) return;

        try
        {
            var files = await _storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = l["UploadFile"],
                AllowMultiple = true
            });

            if (files == null || files.Count == 0) return;
            foreach (var f in files)
            {
                var localPath = f.TryGetLocalPath();
                if (!string.IsNullOrEmpty(localPath))
                {
                    if (Directory.Exists(localPath))
                    {
                        Services.Transfer.TransferService.Instance.EnqueueUploadFolder(
                            _session, localPath, CurrentPath, mode, autoExtract);
                    }
                    else
                    {
                        Services.Transfer.TransferService.Instance.EnqueueUploadFileCompressed(
                            _session, localPath, CurrentPath, mode, autoExtract);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ReportError("SftpUploadFailed", ex.Message);
        }
    }

    [RelayCommand]
    public async Task DownloadFolder(SftpFileItem? target = null)
    {
        var fileItem = target ?? SelectedFile;
        if (fileItem == null || !fileItem.IsDirectory) return;
        var mode = _settings?.FolderTransferMode ?? FolderTransferMode.TarGz;
        var autoExtract = _settings?.AutoExtractAfterTransfer ?? true;
        await DownloadFolderWithModeAsync(fileItem, mode, autoExtract);
    }

    [RelayCommand]
    public async Task DownloadFolderCompressedTar(SftpFileItem? target = null)
    {
        var fileItem = target ?? SelectedFile;
        if (fileItem == null || !fileItem.IsDirectory) return;
        var autoExtract = _settings?.AutoExtractAfterTransfer ?? true;
        await DownloadFolderWithModeAsync(fileItem, FolderTransferMode.TarGz, autoExtract);
    }

    [RelayCommand]
    public async Task DownloadFolderCompressedZip(SftpFileItem? target = null)
    {
        var fileItem = target ?? SelectedFile;
        if (fileItem == null || !fileItem.IsDirectory) return;
        var autoExtract = _settings?.AutoExtractAfterTransfer ?? true;
        await DownloadFolderWithModeAsync(fileItem, FolderTransferMode.Zip, autoExtract);
    }

    [RelayCommand]
    public async Task DownloadFolderStandard(SftpFileItem? target = null)
    {
        var fileItem = target ?? SelectedFile;
        if (fileItem == null || !fileItem.IsDirectory) return;
        await DownloadFolderWithModeAsync(fileItem, FolderTransferMode.Standard, false);
    }

    private async Task DownloadFolderWithModeAsync(SftpFileItem fileItem, FolderTransferMode mode, bool autoExtract)
    {
        var l = Services.LocalizationService.Instance;
        if (_storageProvider == null || _session.SftpClient == null) return;

        try
        {
            var folders = await _storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = l["DownloadFolder"],
                AllowMultiple = false
            });

            if (folders == null || folders.Count == 0) return;
            var localSaveDir = folders[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(localSaveDir))
            {
                ReportError("SftpDownloadFailed", "local path unavailable");
                return;
            }

            Services.Transfer.TransferService.Instance.EnqueueDownloadFolder(
                _session, fileItem.Path, localSaveDir, mode, autoExtract);
        }
        catch (Exception ex)
        {
            ReportError("SftpDownloadFailed", ex.Message);
        }
    }

    [RelayCommand]
    public async Task UploadFile()
    {
        var l = Services.LocalizationService.Instance;
        if (_storageProvider == null || _session.SftpClient == null) return;

        try
        {
            var files = await _storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = l["UploadFile"],
                AllowMultiple = true
            });

            await EnqueueUploadsAsync(files);
        }
        catch (Exception ex)
        {
            ReportError("SftpUploadFailed", ex.Message);
        }
    }

    [RelayCommand]
    public async Task UploadFolder()
    {
        var mode = _settings?.FolderTransferMode ?? FolderTransferMode.TarGz;
        var autoExtract = _settings?.AutoExtractAfterTransfer ?? true;
        await UploadFolderWithModeAsync(mode, autoExtract);
    }

    [RelayCommand]
    public async Task UploadFolderCompressedTar()
    {
        var autoExtract = _settings?.AutoExtractAfterTransfer ?? true;
        await UploadFolderWithModeAsync(FolderTransferMode.TarGz, autoExtract);
    }

    [RelayCommand]
    public async Task UploadFolderCompressedZip()
    {
        var autoExtract = _settings?.AutoExtractAfterTransfer ?? true;
        await UploadFolderWithModeAsync(FolderTransferMode.Zip, autoExtract);
    }

    [RelayCommand]
    public async Task UploadFolderStandard()
    {
        await UploadFolderWithModeAsync(FolderTransferMode.Standard, false);
    }

    private async Task UploadFolderWithModeAsync(FolderTransferMode mode, bool autoExtract)
    {
        var l = Services.LocalizationService.Instance;
        if (_storageProvider == null || _session.SftpClient == null) return;

        try
        {
            var folders = await _storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = l["UploadFolder"],
                AllowMultiple = true
            });

            if (folders == null || folders.Count == 0) return;

            foreach (var folder in folders)
            {
                var localPath = folder.TryGetLocalPath();
                if (string.IsNullOrEmpty(localPath) || !Directory.Exists(localPath)) continue;

                Services.Transfer.TransferService.Instance.EnqueueUploadFolder(
                    _session, localPath, CurrentPath, mode, autoExtract);
            }
        }
        catch (Exception ex)
        {
            ReportError("SftpUploadFailed", ex.Message);
        }
    }

    /// <summary>Entry point for drag &amp; drop uploads from the OS shell.</summary>
    public async Task UploadDroppedAsync(IEnumerable<IStorageItem> items)
    {
        if (_session.SftpClient == null) return;

        try
        {
            await EnqueueUploadsAsync(items);
        }
        catch (Exception ex)
        {
            ReportError("SftpUploadFailed", ex.Message);
        }
    }

    private async Task EnqueueUploadsAsync(IEnumerable<IStorageItem> items)
    {
        var l = Services.LocalizationService.Instance;
        var mode = _settings?.FolderTransferMode ?? FolderTransferMode.TarGz;
        var autoExtract = _settings?.AutoExtractAfterTransfer ?? true;
        int enqueuedCount = 0;

        foreach (var item in items)
        {
            var localPath = item.TryGetLocalPath();
            if (string.IsNullOrEmpty(localPath))
            {
                ReportError("SftpUploadFailed", $"{item.Name}: local path unavailable");
                continue;
            }

            if (Directory.Exists(localPath))
            {
                Services.Transfer.TransferService.Instance.EnqueueUploadFolder(
                    _session, localPath, CurrentPath, mode, autoExtract);
                enqueuedCount++;
                continue;
            }

            if (item is not IStorageFile picked) continue;

            string remotePath = JoinRemote(CurrentPath, picked.Name);

            // Overwrite confirmation up-front; the queued worker writes unconditionally.
            bool exists = await Task.Run(() =>
            {
                _session.SftpLock.Wait();
                try
                {
                    var attr = _session.SftpClient?.GetAttributes(remotePath);
                    return attr != null && !attr.IsDirectory;
                }
                catch { return false; }
                finally { _session.SftpLock.Release(); }
            });

            if (exists)
            {
                if (_confirm != null)
                {
                    bool overwrite = await _confirm(
                        l["UploadFile"],
                        string.Format(l["OverwriteConfirmMessage"], picked.Name));
                    if (!overwrite) continue;
                }

                // Confirmed overwrite: start clean instead of mixing old/new bytes.
                await Task.Run(() =>
                {
                    _session.SftpLock.Wait();
                    try { _session.SftpClient?.DeleteFile(remotePath); }
                    finally { _session.SftpLock.Release(); }
                });
            }

            Services.Transfer.TransferService.Instance.EnqueueUpload(_session, localPath, CurrentPath);
            enqueuedCount++;
        }
    }
}
