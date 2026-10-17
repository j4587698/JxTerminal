using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using JxTerminal.Models;
using JxTerminal.Services;
using JxTerminal.Services.Terminal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
public class TerminalSuggestionTests
{
    private string _testDbPath = "";
    private TerminalSuggestionService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"jxterminal_sug_test_{Guid.NewGuid():N}.db");
        DataStore.SetDatabasePathForTesting(_testDbPath);
        _service = new TerminalSuggestionService();
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
    public void GetSuggestions_ReturnsEmpty_ForEmptyOrWhitespace()
    {
        Assert.AreEqual(0, _service.GetSuggestions("").Count);
        Assert.AreEqual(0, _service.GetSuggestions("   ").Count);
        Assert.AreEqual(0, _service.GetSuggestions(null!).Count);
    }

    [TestMethod]
    public void GetSuggestions_PrefixMatch_BuiltInCommands()
    {
        var results = _service.GetSuggestions("dock");
        Assert.IsTrue(results.Any(r => r.Text.StartsWith("docker", System.StringComparison.OrdinalIgnoreCase)));

        var gitResults = _service.GetSuggestions("git");
        Assert.IsTrue(gitResults.Any(r => r.Text == "git" || r.Text.StartsWith("git ")));
    }

    [TestMethod]
    public void GetSuggestions_SubcommandsAndFlags()
    {
        var sysResults = _service.GetSuggestions("systemctl sta");
        Assert.IsTrue(sysResults.Any(r => r.Text == "systemctl status" || r.Text == "systemctl start"));

        var dockerResults = _service.GetSuggestions("docker ");
        Assert.IsTrue(dockerResults.Any(r => r.Text.StartsWith("docker ")));
    }

    [TestMethod]
    public void RecordHistory_PrioritizesHistoryCommands()
    {
        _service.RecordHistory("systemctl restart nginx");
        _service.RecordHistory("custom-deploy --env prod");

        var results = _service.GetSuggestions("custom-deploy");
        Assert.IsTrue(results.Count > 0);
        Assert.AreEqual("custom-deploy --env prod", results[0].Text);
        Assert.AreEqual(SuggestionCategory.History, results[0].Category);
        Assert.AreEqual("历史", results[0].CategoryBadge);
    }

    [TestMethod]
    public void CalculateSuffix_CalculatesCorrectSuffixText()
    {
        // Exact prefix completion
        var item1 = new TerminalSuggestionItem("docker", "Docker container engine", SuggestionCategory.Command);
        Assert.AreEqual("er", _service.CalculateSuffix("dock", item1));

        // Subcommand completion
        var item2 = new TerminalSuggestionItem("systemctl status", "View service status", SuggestionCategory.Subcommand);
        Assert.AreEqual("tus", _service.CalculateSuffix("systemctl sta", item2));

        // Already complete
        Assert.AreEqual("", _service.CalculateSuffix("docker", item1));

        // Multi-word tail replacement
        var item3 = new TerminalSuggestionItem("grep -rn", "Recursive search with line numbers", SuggestionCategory.Flag);
        Assert.AreEqual("rn", _service.CalculateSuffix("grep -", item3));
    }

    [TestMethod]
    public void SnippetSuggestions_MatchCorrectly()
    {
        var snippets = new List<SnippetItem>
        {
            new SnippetItem { Name = "Clean Docker", Command = "docker system prune -af" },
            new SnippetItem { Name = "Disk Check", Command = "df -h" }
        };

        var results = _service.GetSuggestions("docker sys", snippets);
        var snippet = results.FirstOrDefault(r => r.Category == SuggestionCategory.Snippet);
        Assert.IsNotNull(snippet);
        Assert.AreEqual("docker system prune -af", snippet.Text);
        Assert.AreEqual("Clean Docker", snippet.Description);
        Assert.AreEqual("快捷", snippet.CategoryBadge);
    }

    [TestMethod]
    public void CategoryBadge_ProvidesDistinctLabels()
    {
        Assert.AreEqual("命令", new TerminalSuggestionItem("ls", "", SuggestionCategory.Command).CategoryBadge);
        Assert.AreEqual("子命令", new TerminalSuggestionItem("git commit", "", SuggestionCategory.Subcommand).CategoryBadge);
        Assert.AreEqual("参数", new TerminalSuggestionItem("-la", "", SuggestionCategory.Flag).CategoryBadge);
        Assert.AreEqual("历史", new TerminalSuggestionItem("history cmd", "", SuggestionCategory.History).CategoryBadge);
        Assert.AreEqual("快捷", new TerminalSuggestionItem("snippet cmd", "", SuggestionCategory.Snippet).CategoryBadge);
    }

    [TestMethod]
    public void Settings_EnableSuggestions_DefaultsToTrue_AndSerializesCorrectly()
    {
        var settings = new TerminalSettings();
        Assert.IsTrue(settings.EnableSuggestions, "EnableSuggestions should default to true");

        settings.EnableSuggestions = false;
        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<TerminalSettings>(json)!;
        Assert.IsFalse(restored.EnableSuggestions);
    }

    [TestMethod]
    public void GetSuggestions_LsInput_RanksLsFirstAndBeforeLsof()
    {
        var results = _service.GetSuggestions("ls");
        Assert.IsTrue(results.Count > 0);
        // "ls" itself must be the absolute #1 option
        Assert.AreEqual("ls", results[0].Text);

        // Options immediately following should be ls variants (ls -la, ls -lh, ls -l, ls -a, etc.)
        var lsVariants = results.Where(r => r.Text.StartsWith("ls ")).ToList();
        Assert.IsTrue(lsVariants.Count > 0, "ls variants like ls -la must be included");

        int lsofIndex = results.ToList().FindIndex(r => r.Text.StartsWith("lsof", System.StringComparison.OrdinalIgnoreCase));
        if (lsofIndex >= 0)
        {
            int lsLaIndex = results.ToList().FindIndex(r => r.Text == "ls -la" || r.Text == "ls -l");
            Assert.IsTrue(lsLaIndex >= 0 && lsLaIndex < lsofIndex, "'ls -la' or 'ls -l' must rank before 'lsof'");
        }
    }

    [TestMethod]
    public void GetSuggestions_LsWithSpace_ExcludesLsofCompletely()
    {
        var results = _service.GetSuggestions("ls ");
        Assert.IsTrue(results.Count > 0);
        // All suggestions must be for "ls", never "lsof"
        foreach (var item in results)
        {
            Assert.IsTrue(item.Text.StartsWith("ls ", System.StringComparison.OrdinalIgnoreCase), 
                $"Expected item starting with 'ls ', but got '{item.Text}'");
            Assert.IsFalse(item.Text.StartsWith("lsof", System.StringComparison.OrdinalIgnoreCase));
        }
    }

    [TestMethod]
    public void GetSuggestions_BaseCommands_RankExactCommandTop()
    {
        Assert.AreEqual("cd", _service.GetSuggestions("cd")[0].Text);
        Assert.AreEqual("git", _service.GetSuggestions("git")[0].Text);
        Assert.AreEqual("docker", _service.GetSuggestions("docker")[0].Text);
        Assert.AreEqual("cat", _service.GetSuggestions("cat")[0].Text);
        Assert.AreEqual("grep", _service.GetSuggestions("grep")[0].Text);
    }

    [TestMethod]
    public void GetSuggestions_ExpandedCapacity_ReturnsMoreThanSevenItems()
    {
        // "docker " or "git " has dozens of subcommands and flags in the database
        var dockerResults = _service.GetSuggestions("docker ");
        Assert.IsTrue(dockerResults.Count > 7, $"Expected more than 7 docker suggestions, got {dockerResults.Count}");

        var gitResults = _service.GetSuggestions("git ");
        Assert.IsTrue(gitResults.Count > 7, $"Expected more than 7 git suggestions, got {gitResults.Count}");
    }
}
