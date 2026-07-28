using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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

    [ObservableProperty]
    private string _currentPath = "/";

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Last operation failure, shown as an inline banner. Empty when everything is fine.
    /// </summary>
    [ObservableProperty]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    private void ReportError(string key, string detail)
    {
        var text = string.Format(Services.LocalizationService.Instance[key], detail);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ErrorMessage = text);
    }

    [RelayCommand]
    private void DismissError() => ErrorMessage = "";

    public ObservableCollection<SftpFileItem> Files { get; } = new();

    public SftpViewModel(SshSession session)
    {
        _session = session;
    }

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
                // Go up one level
                // Simple logic: remove last segment
                if (CurrentPath != "/")
                {
                    // Basic path manipulation, SSH.NET might handle '..' automatically if we pass it,
                    // but usually better to resolve the path string for UI display.
                    // For now, let's rely on listing the '..' item's full path if available or manual split.
                    // ISftpFile '..' usually has the full path to parent in .FullName? No, usually just Name="..".
                    
                    // Let's manually parse for now for simplicity
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

    // Requires passing the TopLevel or StorageProvider from View
    [RelayCommand]
    public async Task DownloadFile(object? parameter) 
    {
        if (parameter is not object[] args || args.Length < 2) return;
        var fileItem = args[0] as SftpFileItem;
        var storageProvider = args[1] as IStorageProvider;

        if (fileItem == null || storageProvider == null || fileItem.IsDirectory) return;

        try
        {
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Services.LocalizationService.Instance["DownloadFile"],
                SuggestedFileName = fileItem.Name
            });

            if (file != null && _session.SftpClient != null)
            {
                IsLoading = true;
                var sftpClient = _session.SftpClient;
                await Task.Run(async () =>
                {
                    using var remoteStream = sftpClient.OpenRead(fileItem.Path);
                    using var localStream = await file.OpenWriteAsync();
                    await remoteStream.CopyToAsync(localStream);
                });
                ErrorMessage = "";
            }
        }
        catch (Exception ex)
        {
            ReportError("SftpDownloadFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task UploadFile(IStorageProvider storageProvider)
    {
        if (storageProvider == null) return;

        try
        {
            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Services.LocalizationService.Instance["UploadFile"],
                AllowMultiple = true
            });

            if (files.Any())
            {
                IsLoading = true;
                if (_session.SftpClient != null)
                {
                    var sftpClient = _session.SftpClient;
                    await Task.Run(async () =>
                    {
                        foreach (var file in files)
                        {
                            using var localStream = await file.OpenReadAsync();
                            string remotePath = Path.Combine(CurrentPath, file.Name).Replace("\\", "/");
                            sftpClient.UploadFile(localStream, remotePath);
                        }
                    });
                }
                await RefreshFiles();
            }
        }
        catch (Exception ex)
        {
            ReportError("SftpUploadFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
