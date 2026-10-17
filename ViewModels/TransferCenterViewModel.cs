using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JxTerminal.Models;
using JxTerminal.Services.Transfer;

namespace JxTerminal.ViewModels;

public partial class TransferCenterViewModel : ViewModelBase
{
    private readonly ITransferService _transferService;

    public ObservableCollection<TransferItem> Transfers => _transferService.Transfers;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedFilter = "All"; // "All", "Active", "Completed", "Failed"

    public ObservableCollection<TransferItem> FilteredTransfers { get; } = new();

    public bool HasNoTransfers => FilteredTransfers.Count == 0;

    public TransferCenterViewModel(ITransferService? transferService = null)
    {
        _transferService = transferService ?? TransferService.Instance;
        _transferService.Transfers.CollectionChanged += (_, _) => UpdateFilteredList();
        UpdateFilteredList();
    }

    partial void OnSearchTextChanged(string value) => UpdateFilteredList();
    partial void OnSelectedFilterChanged(string value) => UpdateFilteredList();

    [RelayCommand]
    public void SetFilter(string filter)
    {
        SelectedFilter = filter;
    }

    private void UpdateFilteredList()
    {
        FilteredTransfers.Clear();
        var search = SearchText.Trim().ToLowerInvariant();

        foreach (var item in Transfers)
        {
            // Filter by status
            bool matchesStatus = SelectedFilter switch
            {
                "Active" => item.Status is TransferStatus.Running or TransferStatus.Queued,
                "Completed" => item.Status == TransferStatus.Completed,
                "Failed" => item.Status is TransferStatus.Failed or TransferStatus.Canceled,
                _ => true
            };

            if (!matchesStatus) continue;

            // Filter by search
            if (string.IsNullOrEmpty(search) ||
                item.FileName.ToLowerInvariant().Contains(search) ||
                item.RemotePath.ToLowerInvariant().Contains(search) ||
                item.LocalPath.ToLowerInvariant().Contains(search) ||
                item.SessionTarget.ToLowerInvariant().Contains(search))
            {
                FilteredTransfers.Add(item);
            }
        }

        OnPropertyChanged(nameof(HasNoTransfers));
    }

    [RelayCommand]
    public void CancelTransfer(TransferItem item)
    {
        _transferService.CancelTransfer(item.Id);
    }

    [RelayCommand]
    public void RetryTransfer(TransferItem item)
    {
        _transferService.RetryTransfer(item.Id);
    }

    [RelayCommand]
    public void RemoveTransfer(TransferItem item)
    {
        _transferService.RemoveTransfer(item.Id);
        UpdateFilteredList();
    }

    [RelayCommand]
    public void ClearFinished()
    {
        _transferService.ClearFinished();
        UpdateFilteredList();
    }

    [RelayCommand]
    public void OpenLocalFolder(TransferItem item)
    {
        if (string.IsNullOrWhiteSpace(item.LocalPath)) return;

        try
        {
            var path = item.LocalPath;
            if (File.Exists(path))
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                    return;
                }
                path = Path.GetDirectoryName(path) ?? path;
            }

            if (Directory.Exists(path))
            {
                if (OperatingSystem.IsWindows()) Process.Start("explorer.exe", path);
                else if (OperatingSystem.IsMacOS()) Process.Start("open", path);
                else Process.Start("xdg-open", path);
            }
        }
        catch { /* ignore */ }
    }
}
