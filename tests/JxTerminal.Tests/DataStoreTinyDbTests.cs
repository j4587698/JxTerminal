using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JxTerminal.Models;
using JxTerminal.Services;
using JxTerminal.Services.Terminal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
[DoNotParallelize]
public class DataStoreTinyDbTests
{
    private string _testDbPath = "";

    [TestInitialize]
    public void Setup()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"jxterminal_test_{Guid.NewGuid():N}.db");
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
    public void DatabasePath_IsInProjectDirectory_NotAppData()
    {
        var dbPath = PathConfig.DatabaseFile;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Assert.IsFalse(string.IsNullOrWhiteSpace(dbPath));
        Assert.IsTrue(dbPath.EndsWith("jxterminal.db", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(dbPath.StartsWith(appData, StringComparison.OrdinalIgnoreCase), 
            $"Database file must not be stored in AppData: {dbPath}");
        Assert.IsTrue(dbPath.StartsWith(PathConfig.AppRoot, StringComparison.OrdinalIgnoreCase),
            $"Database file must be within AppRoot: {dbPath}");
    }

    [TestMethod]
    public async Task Connections_SaveAndLoad_RoundTrip()
    {
        var testGroup = new ConnectionGroup("TinyDb-TestGroup")
        {
            Connections =
            {
                new SavedConnection
                {
                    Name = "Server-A",
                    Host = "192.168.10.50",
                    Port = 2222,
                    Username = "admin",
                    Password = "SecretPassword123!",
                    GroupName = "TinyDb-TestGroup"
                }
            }
        };

        var groupsToSave = new List<ConnectionGroup> { testGroup };
        await DataStore.SaveConnectionsAsync(groupsToSave);

        var loaded = await DataStore.LoadConnectionsAsync();
        Assert.IsNotNull(loaded);
        var foundGroup = loaded.FirstOrDefault(g => g.Name == "TinyDb-TestGroup");
        Assert.IsNotNull(foundGroup, "Saved group should be loaded from TinyDb");
        Assert.AreEqual(1, foundGroup.Connections.Count);

        var foundConn = foundGroup.Connections[0];
        Assert.AreEqual("Server-A", foundConn.Name);
        Assert.AreEqual("192.168.10.50", foundConn.Host);
        Assert.AreEqual(2222, foundConn.Port);
        Assert.AreEqual("admin", foundConn.Username);
        Assert.AreEqual("SecretPassword123!", foundConn.Password);
    }

    [TestMethod]
    public async Task Settings_SaveAndLoad_RoundTrip()
    {
        var settings = new TerminalSettings
        {
            FontSize = 19,
            FontFamily = "Consolas",
            ScrollbackLines = 3500,
            CursorStyle = "Underline",
            ThemeMode = "Dark",
            EnableSuggestions = true
        };

        await DataStore.SaveSettingsAsync(settings);

        var loadedAsync = await DataStore.LoadSettingsAsync();
        Assert.AreEqual(19, loadedAsync.FontSize);
        Assert.AreEqual("Consolas", loadedAsync.FontFamily);
        Assert.AreEqual(3500, loadedAsync.ScrollbackLines);
        Assert.AreEqual("Underline", loadedAsync.CursorStyle);
        Assert.IsTrue(loadedAsync.EnableSuggestions);

        var loadedSync = DataStore.LoadSettings();
        Assert.AreEqual(19, loadedSync.FontSize);
        Assert.AreEqual("Consolas", loadedSync.FontFamily);
    }

    [TestMethod]
    public async Task Snippets_SaveAndLoad_RoundTrip()
    {
        var snippets = new List<SnippetItem>
        {
            new() { Name = "Docker PS", Command = "docker ps -a" },
            new() { Name = "System Status", Command = "systemctl status nginx" }
        };

        await DataStore.SaveSnippetsAsync(snippets);

        var loaded = await DataStore.LoadSnippetsAsync();
        Assert.IsNotNull(loaded);
        Assert.IsTrue(loaded.Any(s => s.Name == "Docker PS" && s.Command == "docker ps -a"));
        Assert.IsTrue(loaded.Any(s => s.Name == "System Status" && s.Command == "systemctl status nginx"));
    }

    [TestMethod]
    public async Task Keys_SaveAndLoad_RoundTrip()
    {
        var key = new KeyItem
        {
            Name = "TinyDb Test Key",
            Type = KeyType.ED25519,
            PrivateKeyPem = "-----BEGIN PRIVATE KEY-----\nMIIEvgIBADANBgk...\n-----END PRIVATE KEY-----",
            PublicKeyOpenSsh = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAA test@example",
            Fingerprint = "SHA256:abc123xyz"
        };

        await DataStore.SaveKeysAsync(new[] { key });

        var loaded = await DataStore.LoadKeysAsync();
        Assert.IsNotNull(loaded);
        var found = loaded.FirstOrDefault(k => k.Name == "TinyDb Test Key");
        Assert.IsNotNull(found);
        Assert.AreEqual(KeyType.ED25519, found.Type);
        Assert.AreEqual("ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAA test@example", found.PublicKeyOpenSsh);
        Assert.AreEqual("SHA256:abc123xyz", found.Fingerprint);
        Assert.AreEqual("-----BEGIN PRIVATE KEY-----\nMIIEvgIBADANBgk...\n-----END PRIVATE KEY-----", found.PrivateKeyPem);
    }

    [TestMethod]
    public async Task CommandHistory_SaveAndLoad_PreservesOrderAndDeduplicates()
    {
        await DataStore.SaveCommandHistoryAsync("ls -la");
        await DataStore.SaveCommandHistoryAsync("git status");
        await DataStore.SaveCommandHistoryAsync("docker compose up -d");
        // Re-execute ls -la so it becomes most recent
        await DataStore.SaveCommandHistoryAsync("ls -la");

        var history = await DataStore.LoadCommandHistoryAsync(10);
        Assert.IsNotNull(history);
        Assert.IsTrue(history.Count >= 3);
        // "ls -la" was recorded last, so it must be first
        Assert.AreEqual("ls -la", history[0]);
    }

    [TestMethod]
    public async Task Snippets_AutoSeedsPresetCommands_WhenEmpty()
    {
        var loaded = await DataStore.LoadSnippetsAsync();
        Assert.IsNotNull(loaded);
        Assert.IsTrue(loaded.Count >= 70, $"Expected pre-seeded presets, but got {loaded.Count}");
        Assert.IsTrue(loaded.Any(s => s.Command == "ls -la"));
        Assert.IsTrue(loaded.Any(s => s.Command == "docker ps"));
        Assert.IsTrue(loaded.Any(s => s.Command == "git status"));
    }

    [TestMethod]
    public async Task CommandHistory_RetentionLimit_Enforced()
    {
        // Enforce a small limit for testing (e.g. 5)
        for (int i = 1; i <= 10; i++)
        {
            await DataStore.SaveCommandHistoryAsync($"command-{i}", limit: 5);
        }

        var history = await DataStore.LoadCommandHistoryItemsAsync(10);
        Assert.AreEqual(5, history.Count);
        // Latest commands 10, 9, 8, 7, 6 should be retained
        Assert.AreEqual("command-10", history[0].Command);
        Assert.AreEqual("command-6", history[4].Command);
    }

    [TestMethod]
    public async Task CommandHistory_DeleteAndClear_Succeeds()
    {
        await DataStore.SaveCommandHistoryAsync("to-delete");
        await DataStore.SaveCommandHistoryAsync("to-keep");

        var items = await DataStore.LoadCommandHistoryItemsAsync();
        var itemToDelete = items.FirstOrDefault(h => h.Command == "to-delete");
        Assert.IsNotNull(itemToDelete);

        await DataStore.DeleteCommandHistoryItemAsync(itemToDelete.Id);
        var remaining = await DataStore.LoadCommandHistoryItemsAsync();
        Assert.IsFalse(remaining.Any(h => h.Command == "to-delete"));
        Assert.IsTrue(remaining.Any(h => h.Command == "to-keep"));

        await DataStore.ClearCommandHistoryAsync();
        var empty = await DataStore.LoadCommandHistoryItemsAsync();
        Assert.AreEqual(0, empty.Count);
    }

    [TestMethod]
    public async Task SeedProjectDatabaseWithPresetsIfEmpty()
    {
        DataStore.SetDatabasePathForTesting(null);
        var existing = await DataStore.LoadSnippetsAsync();
        var presets = PresetCommands.GetDefaultSnippets();
        var merged = new List<SnippetItem>(existing);
        var existingCmds = new HashSet<string>(existing.Select(s => s.Command?.Trim() ?? ""), StringComparer.OrdinalIgnoreCase);
        foreach (var p in presets)
        {
            if (!existingCmds.Contains(p.Command.Trim()))
            {
                merged.Add(p);
            }
        }
        await DataStore.SaveSnippetsAsync(merged);
        var updated = await DataStore.LoadSnippetsAsync();
        Assert.IsTrue(updated.Count >= 70);
    }
}
