using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JxTerminal.Models;
using JxTerminal.Services.Transfer;
using JxTerminal.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
[DoNotParallelize]
public class SftpTransferIntegrationTests
{
    [TestInitialize]
    [TestCleanup]
    public void Cleanup()
    {
        TransferService.Instance.Transfers.Clear();
    }

    [TestMethod]
    public void ActiveTransferCount_TracksSessionTransfers()
    {
        var session = new SshSession("testhost.org", 22, "alice", "password");
        var vm = new SftpViewModel(session);

        Assert.AreEqual(0, vm.ActiveTransferCount);
        Assert.IsFalse(vm.HasActiveTransfers);

        var item = new TransferItem
        {
            FileName = "sample.tar.gz",
            SessionTarget = "alice@testhost.org",
            Direction = TransferDirection.Upload,
            Status = TransferStatus.Running,
            TotalBytes = 1000,
            TransferredBytes = 200
        };

        TransferService.Instance.Transfers.Add(item);
        FlushDispatcher();

        Assert.AreEqual(1, vm.ActiveTransferCount);
        Assert.IsTrue(vm.HasActiveTransfers);
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.ActiveTransferSummary));

        item.Status = TransferStatus.Completed;
        FlushDispatcher();

        Assert.AreEqual(0, vm.ActiveTransferCount);
        Assert.IsFalse(vm.HasActiveTransfers);
    }

    private static void FlushDispatcher()
    {
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        catch { }
    }

    [TestMethod]
    public void ActiveTransferCount_IgnoresOtherSessions()
    {
        var session = new SshSession("testhost.org", 22, "alice", "password");
        var vm = new SftpViewModel(session);

        var otherItem = new TransferItem
        {
            FileName = "other.iso",
            SessionTarget = "bob@anotherhost.net",
            Direction = TransferDirection.Download,
            Status = TransferStatus.Running,
            TotalBytes = 5000,
            TransferredBytes = 100
        };

        TransferService.Instance.Transfers.Add(otherItem);
        SpinWait.SpinUntil(() => false, 100);

        Assert.AreEqual(0, vm.ActiveTransferCount);
        Assert.IsFalse(vm.HasActiveTransfers);
    }

    [TestMethod]
    public void CancelTransfer_CancelsActiveSessionTransfers()
    {
        var session = new SshSession("testhost.org", 22, "charlie", "password");
        var vm = new SftpViewModel(session);

        var item = new TransferItem
        {
            FileName = "dataset.zip",
            SessionTarget = "charlie@testhost.org",
            Direction = TransferDirection.Upload,
            Status = TransferStatus.Running,
            TotalBytes = 2000,
            TransferredBytes = 500
        };

        TransferService.Instance.Transfers.Add(item);
        FlushDispatcher();
        Assert.AreEqual(1, vm.ActiveTransferCount);

        vm.CancelTransferCommand.Execute(null);

        Assert.AreEqual(TransferStatus.Canceled, item.Status);
        FlushDispatcher();
        Assert.AreEqual(0, vm.ActiveTransferCount);
    }

    [TestMethod]
    public void OverallTransferProgress_CalculatesWeightedProgress()
    {
        var session = new SshSession("testhost.org", 22, "david", "password");
        var vm = new SftpViewModel(session);

        Assert.AreEqual(0, vm.OverallTransferProgress);
        Assert.IsTrue(vm.OverallTransferIndeterminate);

        var item1 = new TransferItem
        {
            FileName = "file1.bin",
            SessionTarget = "david@testhost.org",
            Direction = TransferDirection.Upload,
            Status = TransferStatus.Running,
            TotalBytes = 1000,
            TransferredBytes = 500
        };
        var item2 = new TransferItem
        {
            FileName = "file2.bin",
            SessionTarget = "david@testhost.org",
            Direction = TransferDirection.Download,
            Status = TransferStatus.Running,
            TotalBytes = 1000,
            TransferredBytes = 300
        };

        TransferService.Instance.Transfers.Add(item1);
        TransferService.Instance.Transfers.Add(item2);
        FlushDispatcher();

        Assert.AreEqual(2, vm.ActiveTransferCount);
        Assert.IsFalse(vm.OverallTransferIndeterminate);
        Assert.AreEqual(40.0, vm.OverallTransferProgress, 0.01);

        item1.TransferredBytes = 1000;
        item1.Status = TransferStatus.Completed;
        FlushDispatcher();

        Assert.AreEqual(1, vm.ActiveTransferCount);
        Assert.IsFalse(vm.OverallTransferIndeterminate);
        Assert.AreEqual(30.0, vm.OverallTransferProgress, 0.01);
    }

    [TestMethod]
    public void IsLoadingAndEmpty_TracksLoadingAndFileCount()
    {
        var session = new SshSession("testhost.org", 22, "david", "password");
        var vm = new SftpViewModel(session);

        Assert.IsFalse(vm.IsLoading);
        Assert.IsFalse(vm.IsLoadingAndEmpty);

        vm.IsLoading = true;
        Assert.IsTrue(vm.IsLoadingAndEmpty);

        vm.Files.Add(new SftpFileItem("test.txt"));
        Assert.IsFalse(vm.IsLoadingAndEmpty);

        vm.IsLoading = false;
        Assert.IsFalse(vm.IsLoadingAndEmpty);
    }

    [TestMethod]
    public async Task ArchiveHelper_TarGz_CompressAndExtract_Success()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"jx_test_targz_{Guid.NewGuid():N}");
        var srcDir = Path.Combine(tempRoot, "source");
        var subDir = Path.Combine(srcDir, "sub");
        var outArchive = Path.Combine(tempRoot, "output.tar.gz");
        var extractDir = Path.Combine(tempRoot, "extracted");

        try
        {
            Directory.CreateDirectory(subDir);
            await File.WriteAllTextAsync(Path.Combine(srcDir, "hello.txt"), "Hello World");
            await File.WriteAllTextAsync(Path.Combine(subDir, "nested.txt"), "Nested Content");

            await ArchiveHelper.CreateTarGzAsync(srcDir, outArchive);
            Assert.IsTrue(File.Exists(outArchive));
            Assert.IsTrue(new FileInfo(outArchive).Length > 0);

            await ArchiveHelper.ExtractTarGzAsync(outArchive, extractDir);

            // Base directory is included in TarFile.CreateFromDirectory
            var extractedHello = Path.Combine(extractDir, "source", "hello.txt");
            var extractedNested = Path.Combine(extractDir, "source", "sub", "nested.txt");
            Assert.IsTrue(File.Exists(extractedHello));
            Assert.IsTrue(File.Exists(extractedNested));
            Assert.AreEqual("Hello World", await File.ReadAllTextAsync(extractedHello));
            Assert.AreEqual("Nested Content", await File.ReadAllTextAsync(extractedNested));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }
    }

    [TestMethod]
    public async Task ArchiveHelper_Zip_CompressAndExtract_Success()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"jx_test_zip_{Guid.NewGuid():N}");
        var srcDir = Path.Combine(tempRoot, "source");
        var subDir = Path.Combine(srcDir, "sub");
        var outArchive = Path.Combine(tempRoot, "output.zip");
        var extractDir = Path.Combine(tempRoot, "extracted");

        try
        {
            Directory.CreateDirectory(subDir);
            await File.WriteAllTextAsync(Path.Combine(srcDir, "hello.txt"), "Hello Zip");
            await File.WriteAllTextAsync(Path.Combine(subDir, "nested.txt"), "Nested Zip");

            await ArchiveHelper.CreateZipAsync(srcDir, outArchive);
            Assert.IsTrue(File.Exists(outArchive));
            Assert.IsTrue(new FileInfo(outArchive).Length > 0);

            await ArchiveHelper.ExtractZipAsync(outArchive, extractDir);

            var extractedHello = Path.Combine(extractDir, "source", "hello.txt");
            var extractedNested = Path.Combine(extractDir, "source", "sub", "nested.txt");
            Assert.IsTrue(File.Exists(extractedHello));
            Assert.IsTrue(File.Exists(extractedNested));
            Assert.AreEqual("Hello Zip", await File.ReadAllTextAsync(extractedHello));
            Assert.AreEqual("Nested Zip", await File.ReadAllTextAsync(extractedNested));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }
    }

    [TestMethod]
    public void TransferItem_FolderAggregate_SummaryAndIcon()
    {
        var item = new TransferItem
        {
            FileName = "my-project",
            IsDirectory = true,
            Direction = TransferDirection.Upload,
            Status = TransferStatus.Running,
            IsCompressed = true,
            CompressionFormat = "tar.gz",
            CurrentSubFileName = "正在服务器解压..."
        };

        Assert.AreEqual(Material.Icons.MaterialIconKind.FolderUpload, item.IconKind);
        Assert.IsTrue(item.DirectoryTransferSummary.Contains("tar.gz"));
        Assert.IsTrue(item.DirectoryTransferSummary.Contains("正在服务器解压..."));

        // Switch to standard recursive progress
        item.IsCompressed = false;
        item.TotalFiles = 50;
        item.TransferredFiles = 15;
        item.CurrentSubFileName = "src/app.vue";

        Assert.IsTrue(item.DirectoryTransferSummary.Contains("15/50"));
        Assert.IsTrue(item.DirectoryTransferSummary.Contains("src/app.vue"));
    }

    [TestMethod]
    public async Task ArchiveHelper_SingleFile_TarGz_CompressAndExtract()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"jx_test_single_tar_{Guid.NewGuid():N}");
        var srcFile = Path.Combine(tempRoot, "document.txt");
        var outArchive = Path.Combine(tempRoot, "document.txt.tar.gz");
        var extractDir = Path.Combine(tempRoot, "extracted");

        try
        {
            Directory.CreateDirectory(tempRoot);
            await File.WriteAllTextAsync(srcFile, "Antigravity Single File Tar Content");

            await ArchiveHelper.CreateTarGzAsync(srcFile, outArchive);
            Assert.IsTrue(File.Exists(outArchive));
            Assert.IsTrue(new FileInfo(outArchive).Length > 0);

            await ArchiveHelper.ExtractTarGzAsync(outArchive, extractDir);
            var extractedFile = Path.Combine(extractDir, "document.txt");
            Assert.IsTrue(File.Exists(extractedFile));
            Assert.AreEqual("Antigravity Single File Tar Content", await File.ReadAllTextAsync(extractedFile));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }
    }

    [TestMethod]
    public async Task ArchiveHelper_SingleFile_Zip_CompressAndExtract()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"jx_test_single_zip_{Guid.NewGuid():N}");
        var srcFile = Path.Combine(tempRoot, "single_data.csv");
        var outArchive = Path.Combine(tempRoot, "single_data.zip");
        var extractDir = Path.Combine(tempRoot, "extracted");

        try
        {
            Directory.CreateDirectory(tempRoot);
            await File.WriteAllTextAsync(srcFile, "col1,col2\r\nval1,val2");

            await ArchiveHelper.CreateZipAsync(srcFile, outArchive);
            Assert.IsTrue(File.Exists(outArchive));
            Assert.IsTrue(new FileInfo(outArchive).Length > 0);

            await ArchiveHelper.ExtractZipAsync(outArchive, extractDir);
            var extractedFile = Path.Combine(extractDir, "single_data.csv");
            Assert.IsTrue(File.Exists(extractedFile));
            Assert.AreEqual("col1,col2\r\nval1,val2", await File.ReadAllTextAsync(extractedFile));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }
    }

    [TestMethod]
    public void TransferItem_SingleFileCompressed_StatusAndBadges()
    {
        var item = new TransferItem
        {
            FileName = "large_dump.sql",
            IsDirectory = false,
            Direction = TransferDirection.Upload,
            Status = TransferStatus.Running,
            IsCompressed = true,
            CompressionFormat = "tar.gz",
            CurrentSubFileName = "正在解压..."
        };

        Assert.AreEqual(Material.Icons.MaterialIconKind.Upload, item.IconKind);
        Assert.IsTrue(item.HasDirectorySummary);
        Assert.AreEqual("正在解压...", item.DirectorySubStatus);
        Assert.IsTrue(item.DirectoryTransferSummary.Contains("tar.gz"));
        Assert.IsTrue(item.DirectoryTransferSummary.Contains("正在解压..."));
    }

    [TestMethod]
    public void SftpViewModel_UnifiedDownloadCommands_Exist()
    {
        var session = new SshSession("testhost.org", 22, "tester", "password");
        var vm = new SftpViewModel(session);

        Assert.IsNotNull(vm.DownloadItemCommand);
        Assert.IsNotNull(vm.DownloadItemCompressedTarCommand);
        Assert.IsNotNull(vm.DownloadItemCompressedZipCommand);
        Assert.IsNotNull(vm.DownloadFolderStandardCommand);
    }
}

