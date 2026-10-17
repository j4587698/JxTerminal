using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace JxTerminal.Models;

public enum TransferDirection
{
    Upload,
    Download
}

public enum TransferStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Canceled
}

public partial class TransferItem : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private string _localPath = string.Empty;
    [ObservableProperty] private string _remotePath = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKind))]
    private TransferDirection _direction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplaySpeed))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(IsQueued))]
    [NotifyPropertyChangedFor(nameof(IsCanceled))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(DirectoryTransferSummary))]
    [NotifyPropertyChangedFor(nameof(DirectorySubStatus))]
    [NotifyPropertyChangedFor(nameof(HasDirectorySummary))]
    private TransferStatus _status = TransferStatus.Queued;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercentage))]
    [NotifyPropertyChangedFor(nameof(DisplayProgress))]
    [NotifyPropertyChangedFor(nameof(DisplayPercentage))]
    private long _totalBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercentage))]
    [NotifyPropertyChangedFor(nameof(DisplayProgress))]
    [NotifyPropertyChangedFor(nameof(DisplayPercentage))]
    private long _transferredBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplaySpeed))]
    private double _speedBytesPerSec;

    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _sessionTarget = string.Empty;
    [ObservableProperty] private DateTime _startTime = DateTime.Now;

    // Folder and compression aggregate states
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKind))]
    [NotifyPropertyChangedFor(nameof(DirectoryTransferSummary))]
    [NotifyPropertyChangedFor(nameof(DirectorySubStatus))]
    [NotifyPropertyChangedFor(nameof(HasDirectorySummary))]
    private bool _isDirectory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectoryTransferSummary))]
    [NotifyPropertyChangedFor(nameof(DirectorySubStatus))]
    [NotifyPropertyChangedFor(nameof(HasDirectorySummary))]
    private bool _isCompressed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectoryTransferSummary))]
    [NotifyPropertyChangedFor(nameof(DirectorySubStatus))]
    [NotifyPropertyChangedFor(nameof(HasDirectorySummary))]
    private string _compressionFormat = string.Empty; // "tar.gz", "zip"

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectoryTransferSummary))]
    [NotifyPropertyChangedFor(nameof(DirectorySubStatus))]
    [NotifyPropertyChangedFor(nameof(HasDirectorySummary))]
    private int _totalFiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectoryTransferSummary))]
    [NotifyPropertyChangedFor(nameof(DirectorySubStatus))]
    [NotifyPropertyChangedFor(nameof(HasDirectorySummary))]
    private int _transferredFiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectoryTransferSummary))]
    [NotifyPropertyChangedFor(nameof(DirectorySubStatus))]
    [NotifyPropertyChangedFor(nameof(HasDirectorySummary))]
    private string _currentSubFileName = string.Empty;

    public CancellationTokenSource? Cts { get; set; }
    public Func<CancellationToken, IProgress<long>, Task>? WorkerAction { get; set; }

    public double ProgressPercentage => TotalBytes > 0 ? Math.Clamp((double)TransferredBytes * 100.0 / TotalBytes, 0.0, 100.0) : 0.0;

    public string DisplayPercentage => $"{ProgressPercentage:0.#}%";

    public bool IsRunning => Status == TransferStatus.Running;
    public bool IsCompleted => Status == TransferStatus.Completed;
    public bool IsFailed => Status == TransferStatus.Failed;
    public bool IsQueued => Status == TransferStatus.Queued;
    public bool IsCanceled => Status == TransferStatus.Canceled;

    public string StatusText => Status switch
    {
        TransferStatus.Queued => "排队中",
        TransferStatus.Running => "传输中",
        TransferStatus.Completed => "已完成",
        TransferStatus.Failed => "失败",
        TransferStatus.Canceled => "已取消",
        _ => Status.ToString()
    };

    public string DisplayProgress => $"{FormatSize(TransferredBytes)} / {FormatSize(TotalBytes)}";

    public string DisplaySpeed => Status == TransferStatus.Running
        ? Services.Monitoring.LinuxProcMonitorProvider.FormatSpeed(SpeedBytesPerSec)
        : string.Empty;

    public bool HasDirectorySummary => (IsDirectory || IsCompressed) && !string.IsNullOrEmpty(DirectorySubStatus);

    public string DirectorySubStatus
    {
        get
        {
            if (!IsDirectory && !IsCompressed) return string.Empty;

            if (IsCompressed)
            {
                if (!string.IsNullOrEmpty(CurrentSubFileName))
                {
                    return CurrentSubFileName;
                }
                return Status switch
                {
                    TransferStatus.Queued => "排队中",
                    TransferStatus.Running => "正在传输压缩包...",
                    TransferStatus.Completed => "解压完成",
                    TransferStatus.Failed => "传输失败",
                    TransferStatus.Canceled => "已取消",
                    _ => string.Empty
                };
            }

            // Standard recursive
            if (TotalFiles > 0)
            {
                var prefix = $"已传输 {TransferredFiles}/{TotalFiles} 个文件";
                if (!string.IsNullOrEmpty(CurrentSubFileName) && Status == TransferStatus.Running)
                {
                    return $"{prefix} · {CurrentSubFileName}";
                }
                return prefix;
            }

            if (!string.IsNullOrEmpty(CurrentSubFileName))
            {
                return CurrentSubFileName;
            }

            return Status switch
            {
                TransferStatus.Queued => "排队中",
                TransferStatus.Running => "正在扫描目录并传输...",
                TransferStatus.Completed => "文件夹传输完成",
                TransferStatus.Failed => "传输失败",
                TransferStatus.Canceled => "已取消",
                _ => string.Empty
            };
        }
    }

    public string DirectoryTransferSummary
    {
        get
        {
            if (!IsDirectory && !IsCompressed) return string.Empty;
            var sub = DirectorySubStatus;
            if (string.IsNullOrEmpty(sub)) return string.Empty;
            if (IsCompressed && !string.IsNullOrEmpty(CompressionFormat))
            {
                return $"[{CompressionFormat}] {sub}";
            }
            return sub;
        }
    }

    /// <summary>Cancel only makes sense while the transfer is queued or running.</summary>
    public bool CanCancel => Status is TransferStatus.Queued or TransferStatus.Running;

    /// <summary>Retry only makes sense after a failure or cancellation.</summary>
    public bool CanRetry => Status is TransferStatus.Failed or TransferStatus.Canceled;

    public MaterialIconKind IconKind
    {
        get
        {
            if (IsDirectory)
            {
                return Direction == TransferDirection.Upload
                    ? MaterialIconKind.FolderUpload
                    : MaterialIconKind.FolderDownload;
            }
            return Direction == TransferDirection.Upload
                ? MaterialIconKind.Upload
                : MaterialIconKind.Download;
        }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):0.0} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):0.0} GB";
    }
}
