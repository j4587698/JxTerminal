using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JxTerminal.Models;
using JxTerminal.Services;
using JxTerminal.Services.Sync;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
[DoNotParallelize]
public class SyncPersistenceTests
{
    private string _testDbPath = "";

    [TestInitialize]
    public void Setup()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"jxterminal_sync_test_{Guid.NewGuid():N}.db");
        DataStore.SetDatabasePathForTesting(_testDbPath);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await SyncService.Instance.LogoutAsync();
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
    public async Task SyncConfig_SaveAndReload_RestoresAuthenticationAndSyncKey()
    {
        var sync = SyncService.Instance;
        var username = "alice";
        var password = "secretPassword123!";

        // 1. Configure and authenticate
        sync.SetSyncPassword(username, password);
        var originalKey = sync.CurrentSyncKey;
        Assert.IsNotNull(originalKey);

        var config = new SyncConfig
        {
            ServerUrl = "https://sync.example.com",
            Username = username,
            DeviceName = "laptop-01",
            Token = "jwt-test-token-value",
            TokenExpiresAt = DateTime.UtcNow.AddDays(7),
            EncryptedSyncKey = sync.Config.EncryptedSyncKey
        };
        await sync.SaveConfigAsync(config);

        // 2. Simulate restarting app / clearing in-memory state
        await sync.LogoutAsync();
        // Overwrite disk with the previously saved valid config
        await DataStore.SaveSyncConfigAsync(config);
        Assert.IsFalse(sync.IsAuthenticated);
        Assert.IsNull(sync.CurrentSyncKey);

        // 3. Load config from persistent store
        await sync.LoadConfigAsync();

        // 4. Verify authentication and key restoration
        Assert.AreEqual("jwt-test-token-value", sync.Config.Token, $"Token mismatch. EncryptedSyncKey={sync.Config.EncryptedSyncKey}");
        Assert.AreEqual(username, sync.Config.Username);
        Assert.IsTrue(DateTime.UtcNow < sync.Config.TokenExpiresAt, $"ExpiresAt was {sync.Config.TokenExpiresAt:O}, UtcNow is {DateTime.UtcNow:O}");
        Assert.IsTrue(sync.IsAuthenticated);
        Assert.AreEqual("https://sync.example.com", sync.Config.ServerUrl);
        Assert.AreEqual(username, sync.Config.Username);
        Assert.AreEqual("laptop-01", sync.Config.DeviceName);
        Assert.AreEqual("jwt-test-token-value", sync.Config.Token);
        Assert.IsNotNull(sync.CurrentSyncKey);
        CollectionAssert.AreEqual(originalKey, sync.CurrentSyncKey);
    }

    [TestMethod]
    public async Task SyncService_ExpiredToken_NotAuthenticated()
    {
        var sync = SyncService.Instance;
        var config = new SyncConfig
        {
            ServerUrl = "https://sync.example.com",
            Username = "bob",
            Token = "expired-token",
            TokenExpiresAt = DateTime.UtcNow.AddMinutes(-5) // Expired
        };
        await sync.SaveConfigAsync(config);

        await sync.LoadConfigAsync();

        Assert.IsFalse(sync.IsAuthenticated, "Expired token should not be authenticated");
    }

    [TestMethod]
    public async Task SyncService_Logout_PersistsClearedState()
    {
        var sync = SyncService.Instance;
        sync.SetSyncPassword("charlie", "pass123");
        await sync.SaveConfigAsync(new SyncConfig
        {
            ServerUrl = "https://sync.example.com",
            Username = "charlie",
            Token = "valid-token",
            TokenExpiresAt = DateTime.UtcNow.AddDays(1),
            EncryptedSyncKey = sync.Config.EncryptedSyncKey
        });

        Assert.IsTrue(sync.IsAuthenticated);

        // Perform logout
        await sync.LogoutAsync();

        Assert.IsFalse(sync.IsAuthenticated);
        Assert.IsNull(sync.CurrentSyncKey);
        Assert.AreEqual("", sync.Config.Token);

        // Reload from disk to verify persistence
        await sync.LoadConfigAsync();
        Assert.IsFalse(sync.IsAuthenticated);
        Assert.AreEqual("", sync.Config.Token);
        Assert.IsNull(sync.CurrentSyncKey);
    }

    [TestMethod]
    public async Task SyncViewModel_Initialization_RestoresAuthAndPreservesEncryptedSyncKey()
    {
        var sync = SyncService.Instance;
        sync.SetSyncPassword("dave", "daveSecretPass!");
        var expectedEncryptedKey = sync.Config.EncryptedSyncKey;

        var config = new SyncConfig
        {
            ServerUrl = "https://sync.jxterminal.com",
            Username = "dave",
            DeviceName = "dave-desktop",
            Token = "dave-jwt-token-999",
            TokenExpiresAt = DateTime.UtcNow.AddDays(14),
            EncryptedSyncKey = expectedEncryptedKey
        };
        await DataStore.SaveSyncConfigAsync(config);

        // Instantiate ViewModel
        var vm = new ViewModels.SyncViewModel();
        await vm.InitializationTask;

        // Verify loaded properties & auth state
        Assert.IsTrue(vm.IsLoggedIn);
        Assert.AreEqual("https://sync.jxterminal.com", vm.ServerUrl);
        Assert.AreEqual("dave", vm.Username);
        Assert.AreEqual("dave-desktop", vm.DeviceName);

        // Edit device name and save
        vm.DeviceName = "dave-laptop-updated";
        await vm.SaveConfigCommand.ExecuteAsync(null);

        // Verify EncryptedSyncKey was not wiped by SaveConfig
        var persisted = await DataStore.LoadSyncConfigAsync();
        Assert.AreEqual("dave-laptop-updated", persisted.DeviceName);
        Assert.AreEqual("dave-jwt-token-999", persisted.Token);
        Assert.AreEqual(expectedEncryptedKey, persisted.EncryptedSyncKey, "SaveConfig must preserve EncryptedSyncKey!");
    }

    [TestMethod]
    public async Task SyncConfig_AutoSyncAndLastSyncTime_PersistedProperly()
    {
        var sync = SyncService.Instance;
        var now = new DateTime(2026, 10, 11, 8, 30, 0, DateTimeKind.Utc);

        var config = new SyncConfig
        {
            ServerUrl = "https://sync.jxterminal.com",
            Username = "eve",
            AutoSyncEnabled = false,
            LastSyncTime = now
        };
        await sync.SaveConfigAsync(config);

        await sync.LoadConfigAsync();
        Assert.IsFalse(sync.Config.AutoSyncEnabled);
        Assert.AreEqual(now, sync.Config.LastSyncTime);

        // ViewModel loads these settings
        var vm = new ViewModels.SyncViewModel();
        await vm.InitializationTask;
        Assert.IsFalse(vm.AutoSyncEnabled);
        Assert.IsTrue(vm.LastSyncTimeText.Contains("2026-10-11") || vm.LastSyncTimeText.Contains("08:30"));

        // Toggle AutoSync
        vm.AutoSyncEnabled = true;
        await vm.SaveConfigCommand.ExecuteAsync(null);

        var reloaded = await DataStore.LoadSyncConfigAsync();
        Assert.IsTrue(reloaded.AutoSyncEnabled);
    }

    [TestMethod]
    public async Task ApplySnapshot_PreservesLocalSettingsAiApiKey_WhenRemoteSnapshotKeyIsEmpty()
    {
        var sync = SyncService.Instance;

        // 1. Local data store has valid AiApiKey and AiEndpoint
        var localSettings = new TerminalSettings
        {
            AiEndpoint = "https://local-endpoint.example.com",
            AiApiKey = "sk-local-secret-123456"
        };
        await DataStore.SaveSettingsAsync(localSettings);

        // 2. Remote snapshot arrives with new endpoint but EMPTY AiApiKey (e.g. decryption failed or unset)
        var remoteSnapshot = new SyncSnapshot
        {
            Settings = new TerminalSettings
            {
                AiEndpoint = "https://remote-endpoint.example.com",
                AiApiKey = "" // Empty remote key
            }
        };

        // 3. Apply snapshot
        var preserved = await sync.ApplySnapshotAsync(remoteSnapshot);

        // 4. Verification: credentialsPreserved flag must be true, and local AiApiKey retained
        Assert.IsTrue(preserved, "credentialsPreserved should be true when local AiApiKey is kept over empty remote key");

        var reloaded = DataStore.LoadSettings();
        Assert.AreEqual("https://remote-endpoint.example.com", reloaded.AiEndpoint);
        Assert.AreEqual("sk-local-secret-123456", reloaded.AiApiKey, "Local AiApiKey should be preserved");
    }
}
