using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using JxTerminal.Models;

namespace JxTerminal.Services.Transfer;

public interface ITransferService
{
    ObservableCollection<TransferItem> Transfers { get; }
    TransferItem EnqueueUpload(SshSession session, string localFilePath, string remoteDirPath);
    TransferItem EnqueueDownload(SshSession session, string remoteFilePath, string localSavePath, long fileSize);
    TransferItem EnqueueUploadFileCompressed(SshSession session, string localFilePath, string remoteDirPath, FolderTransferMode mode, bool autoExtract = true);
    TransferItem EnqueueDownloadFileCompressed(SshSession session, string remoteFilePath, string localSaveDir, FolderTransferMode mode, bool autoExtract = true);
    TransferItem EnqueueUploadFolder(SshSession session, string localDirPath, string remoteDirPath, FolderTransferMode mode, bool autoExtract = true);
    TransferItem EnqueueDownloadFolder(SshSession session, string remoteDirPath, string localSaveDir, FolderTransferMode mode, bool autoExtract = true);
    void CancelTransfer(Guid transferId);
    void RetryTransfer(Guid transferId);
    void RemoveTransfer(Guid transferId);
    void ClearFinished();
}
