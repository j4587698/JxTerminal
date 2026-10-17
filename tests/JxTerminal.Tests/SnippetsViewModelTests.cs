using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JxTerminal.Models;
using JxTerminal.Services;
using JxTerminal.Services.Terminal;
using JxTerminal.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
[DoNotParallelize]
public class SnippetsViewModelTests
{
    private string _testDbPath = "";

    [TestInitialize]
    public void Setup()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"jxterminal_vm_test_{Guid.NewGuid():N}.db");
        DataStore.SetDatabasePathForTesting(_testDbPath);
    }

    [TestCleanup]
    public void Cleanup()
    {
        DataStore.SetDatabasePathForTesting(null);
        try
        {
            if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
            var wal = Path.Combine(Path.GetDirectoryName(_testDbPath)!, Path.GetFileNameWithoutExtension(_testDbPath) + "-wal.db");
            if (File.Exists(wal)) File.Delete(wal);
        }
        catch { }
    }

    [TestMethod]
    public async Task TabSwitching_UpdatesSelectionAndFilter()
    {
        var vm = new SnippetsViewModel(null);
        await vm.LoadAsync();

        Assert.IsTrue(vm.IsSnippetsTab);
        Assert.IsFalse(vm.IsHistoryTab);
        Assert.IsTrue(vm.SnippetCount > 0, "Default presets should be loaded");

        vm.SelectTab("History");
        Assert.IsFalse(vm.IsSnippetsTab);
        Assert.IsTrue(vm.IsHistoryTab);

        vm.SelectTab("Snippets");
        Assert.IsTrue(vm.IsSnippetsTab);
        Assert.IsFalse(vm.IsHistoryTab);
    }

    [TestMethod]
    public async Task Snippets_FilterByNameAndCommand()
    {
        var vm = new SnippetsViewModel(null);
        await vm.LoadAsync();

        vm.SearchText = "docker ps";
        Assert.IsTrue(vm.FilteredSnippets.Count > 0);
        Assert.IsTrue(vm.FilteredSnippets.All(s => s.Command.Contains("docker ps", StringComparison.OrdinalIgnoreCase)
                                                || s.Name.Contains("docker ps", StringComparison.OrdinalIgnoreCase)));

        vm.SearchText = "不存在的神秘命令XYZ999";
        Assert.AreEqual(0, vm.FilteredSnippets.Count);
        Assert.IsTrue(vm.HasNoSnippets);

        vm.SearchText = "";
        Assert.AreEqual(vm.SnippetCount, vm.FilteredSnippets.Count);
    }

    [TestMethod]
    public async Task History_FilterByCommand()
    {
        await DataStore.SaveCommandHistoryAsync("npm run build");
        await DataStore.SaveCommandHistoryAsync("npm test");
        await DataStore.SaveCommandHistoryAsync("cargo build --release");

        var vm = new SnippetsViewModel(null);
        await vm.LoadAsync();

        vm.SelectTab("History");
        Assert.AreEqual(3, vm.HistoryCount);

        vm.SearchText = "npm";
        Assert.AreEqual(2, vm.FilteredHistoryItems.Count);

        vm.SearchText = "cargo";
        Assert.AreEqual(1, vm.FilteredHistoryItems.Count);
        Assert.AreEqual("cargo build --release", vm.FilteredHistoryItems[0].Command);
    }

    [TestMethod]
    public async Task History_DeleteAndClear_UpdatesCounts()
    {
        await DataStore.SaveCommandHistoryAsync("cmd-1");
        await DataStore.SaveCommandHistoryAsync("cmd-2");

        var vm = new SnippetsViewModel(null);
        await vm.LoadAsync();

        vm.SelectTab("History");
        Assert.AreEqual(2, vm.HistoryCount);

        var first = vm.FilteredHistoryItems[0];
        await vm.DeleteHistoryItem(first);
        Assert.AreEqual(1, vm.HistoryCount);

        await vm.ClearHistory();
        Assert.AreEqual(0, vm.HistoryCount);
        Assert.IsTrue(vm.HasNoHistory);
    }

    [TestMethod]
    public async Task RestoreDefaultSnippets_AddsMissingPresets()
    {
        var vm = new SnippetsViewModel(null);
        await vm.LoadAsync();

        // Clear all snippets
        vm.Snippets.Clear();
        await vm.RestoreDefaultSnippets();

        Assert.IsTrue(vm.SnippetCount >= 70);
        Assert.IsTrue(vm.Snippets.Any(s => s.Command == "ls -la"));
    }
}
