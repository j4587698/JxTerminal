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
    private IStorageProvider? _storageProvider;

    [ObservableProperty]
    private string _currentPath = "/";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private SftpFileItem? _selectedFile;

    /// <summary>
    /// Last operation failure, shown as an inline banner. Empty when everything is fine.
    /// </summary>
    [ObservableProperty]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    // Transfer state (progress bar + cancel)
    [ObservableProperty]
    private bool _isTransferring;

    [ObservableProperty]
    private double _transferProgress; // 0..100, -1 = indeterminate

    public bool TransferIndeterminate => TransferProgress < 0;

    partial void OnTransferProgressChanged(double value) => OnPropertyChanged(nameof(TransferIndeterminate));

    [ObservableProperty]
    private string _transferLabel = "";

    private CancellationTokenSource? _transferCts;

    private void ReportError(string key, string detail)
    {
        var text = string.Format(Services.LocalizationService.Instance[key], detail);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ErrorMessage = text);
    }

    [RelayCommand]
    private void DismissError() => ErrorMessage = "";

    public ObservableCollection<SftpFileItem> Files { get; } = new();

    public SftpViewModel(
        SshSession session,
        Func<string, string, Task<bool>>? confirm = null,
        Func<string, string, Task<string?>>? prompt = null)
    {
        _session = session;
        _confirm = confirm;
        _prompt = prompt;
    }

    /// <summary>Called by the view once it can resolve the platform storage provider.</summary>
    public void SetStorageProvider(IStorageProvider storageProvider) => _storageProvider = storageProvider;

    [RelayCommand]
    public async Task RefreshFiles()
    {
        if (_session.SftpClient != null && _session.IsConnected)
        {
            IsLoading = true;
            await Task.Run(() =>
            {
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
                    });
                }
                catch (Exception ex)
                {
                    ReportError("SftpListFailed", ex.Message);
                }
                finally
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => IsLoading = false);
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

    // ---------------- Directory operations ----------------

    [RelayCommand]
    public async Task CreateFolder()
    {
        var l = Services.LocalizationService.Instance;
        if (_prompt == null) return;

        var name = await _prompt(l["NewFolder"], l["PromptFolderName"]);
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            IsLoading = true;
            await Task.Run(() => _session.SftpClient?.CreateDirectory(JoinRemote(CurrentPath, name)));
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
    public async Task DeleteSelected()
    {
        var l = Services.LocalizationService.Instance;
        var item = SelectedFile;
        if (item == null || item.Name == ".." || _confirm == null) return;

        if (!await _confirm(l["DeleteConfirmTitle"], string.Format(l["DeleteConfirmMessage"], item.Name)))
            return;

        try
        {
            IsLoading = true;
            await Task.Run(() =>
            {
                if (_session.SftpClient == null) return;
                if (item.IsDirectory) _session.SftpClient.DeleteDirectory(item.Path);
                else _session.SftpClient.DeleteFile(item.Path);
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
    public async Task RenameSelected()
    {
        var l = Services.LocalizationService.Instance;
        var item = SelectedFile;
        if (item == null || item.Name == ".." || _prompt == null) return;

        var newName = await _prompt(l["RenameItem"], l["PromptNewName"]);
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

        try
        {
            IsLoading = true;
            await Task.Run(() =>
                _session.SftpClient?.RenameFile(item.Path, JoinRemote(CurrentPath, newName)));
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

    /// <summary>Cancels the running upload/download at the next chunk boundary.</summary>
    [RelayCommand]
    private void CancelTransfer() => _transferCts?.Cancel();

    [RelayCommand]
    public async Task DownloadFile()
    {
        var l = Services.LocalizationService.Instance;
        var fileItem = SelectedFile;
        if (fileItem == null || fileItem.IsDirectory || _storageProvider == null || _session.SftpClient == null)
            return;

        try
        {
            var file = await _storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = l["DownloadFile"],
                SuggestedFileName = fileItem.Name
            });

            if (file == null) return;

            var sftpClient = _session.SftpClient;
            long totalSize = Math.Max(1, fileItem.Size);

            await RunTransferAsync(async token =>
            {
                TransferLabel = string.Format(l["DownloadingFormat"], fileItem.Name);

                await Task.Run(async () =>
                {
                    using var remoteStream = sftpClient.OpenRead(fileItem.Path);
                    using var localStream = await file.OpenWriteAsync();
                    var buffer = new byte[81920];
                    long done = 0;
                    int read;
                    while ((read = await remoteStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        await localStream.WriteAsync(buffer, 0, read, token);
                        done += read;
                        var pct = done * 100.0 / totalSize;
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => TransferProgress = pct);
                    }
                }, token);
            });
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

            if (!files.Any()) return;

            await RunTransferAsync(token =>
                UploadPickedFilesAsync(files, token));
        }
        catch (Exception ex)
        {
            ReportError("SftpUploadFailed", ex.Message);
        }
    }

    /// <summary>Entry point for drag &amp; drop uploads from the OS shell.</summary>
    public async Task UploadDroppedAsync(IEnumerable<IStorageItem> items)
    {
        var list = items.OfType<IStorageFile>().ToList();
        if (list.Count == 0 || _session.SftpClient == null) return;

        try
        {
            await RunTransferAsync(token =>
                UploadPickedFilesAsync(list, token));
        }
        catch (Exception ex)
        {
            ReportError("SftpUploadFailed", ex.Message);
        }
    }

    /// <summary>
    /// Shared transfer wrapper: owns the progress overlay state and cancellation source.
    /// </summary>
    private async Task RunTransferAsync(Func<CancellationToken, Task> body)
    {
        _transferCts = new CancellationTokenSource();
        var token = _transferCts.Token;
        IsTransferring = true;
        TransferProgress = -1; // indeterminate until the first chunk lands

        try
        {
            await body(token);
            TransferProgress = 100;
            ErrorMessage = "";
            await RefreshFiles();
        }
        catch (OperationCanceledException)
        {
            // User cancelled: nothing to report.
        }
        finally
        {
            IsTransferring = false;
        }
    }

    private async Task UploadPickedFilesAsync(IEnumerable<IStorageItem> items, CancellationToken token)
    {
        var l = Services.LocalizationService.Instance;
        if (_session.SftpClient == null) return;
        var sftpClient = _session.SftpClient;

        foreach (var file in items)
        {
            if (file is IStorageFile picked)
            {
                using var localStream = await picked.OpenReadAsync();
                await UploadStreamCoreAsync(sftpClient, localStream, file.Name,
                    l, token);
            }
        }
    }

    private async Task UploadStreamCoreAsync(
        Renci.SshNet.SftpClient sftpClient,
        Stream localStream,
        string displayName,
        Services.LocalizationService l,
        CancellationToken token)
    {
        string remotePath = JoinRemote(CurrentPath, displayName);

        // Probe remote state off the UI thread (GetAttributes is a network round-trip).
        bool exists = false;
        long totalSize = 1;
        try
        {
            (exists, totalSize) = await Task.Run(() =>
            {
                try
                {
                    var attr = sftpClient.GetAttributes(remotePath);
                    return (attr != null && !attr.IsDirectory, Math.Max(1, attr?.Size ?? 1));
                }
                catch
                {
                    return (false, 1L);
                }
            });
        }
        catch { /* never happens: tuple returned above */ }

        if (exists && _confirm != null)
        {
            bool overwrite = await _confirm(
                l["UploadFile"],
                string.Format(l["OverwriteConfirmMessage"], displayName));
            if (!overwrite) return;
        }

        TransferLabel = string.Format(l["UploadingFormat"], displayName);

        await Task.Run(async () =>
        {
            var buffer = new byte[81920];
            long done = 0;
            if (exists)
            {
                // Confirmed overwrite: start clean instead of mixing old/new bytes.
                sftpClient.DeleteFile(remotePath);
            }
            using var remoteStream = sftpClient.OpenWrite(remotePath);
            int read;
            while ((read = localStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                remoteStream.Write(buffer, 0, read);
                done += read;
                var pct = done * 100.0 / totalSize;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => TransferProgress = pct);
            }
        }, token);
    }
}
