using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Threading.Tasks;
using JxTerminal.Models;
using JxTerminal.Services;
using JxTerminal.Services.Sync;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

/// <summary>
/// End-to-end verification of the client sync service against the real JxTerminal.Server:
/// spawns the server, then exercises register, login, token validation, push, list,
/// download, per-user isolation, delete and logout.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SyncServerIntegrationTests
{
    [TestMethod]
    public async Task ClientSyncService_Registers_LogsIn_AndBacksUp()
    {
        var siblingServerDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "JxTerminal.Server"));
        var innerServerDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "JxTerminal.Server"));
        var serverDir = Directory.Exists(siblingServerDir) ? siblingServerDir : innerServerDir;
        if (!Directory.Exists(serverDir))
        {
            Assert.Inconclusive("JxTerminal.Server project not found.");
        }

        var port = GetFreePort();
        var serverDataDir = Path.Combine(Path.GetTempPath(), $"jxserver_{Guid.NewGuid():N}");
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList =
                {
                    "run",
                    "--project", Path.Combine(serverDir, "JxTerminal.Server.csproj"),
                    "--urls", $"http://127.0.0.1:{port}",
                    "--Backup:Directory", serverDataDir
                },
                WorkingDirectory = serverDir,
                CreateNoWindow = true,
                UseShellExecute = false
            }
        };
        proc.Start();

        try
        {
            var service = SyncService.Instance;
            await service.SaveConfigAsync(new SyncConfig { ServerUrl = $"http://127.0.0.1:{port}", DeviceName = "integration-host" });

            // Wait for `dotnet run` to build and start the server.
            await WaitForServerAsync(service);

            // Health check (anonymous).
            Assert.IsTrue(await service.TestConnectionAsync(), "Health check should pass");

            // Not authenticated yet: backup operations must fail.
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.PushBackupAsync("nope"));

            // Register -> token is stored and valid.
            var registered = await service.RegisterAsync("alice", "secret123");
            Assert.AreEqual("alice", registered.Username);
            Assert.IsFalse(string.IsNullOrWhiteSpace(registered.Token));
            Assert.IsTrue(service.IsAuthenticated);
            Assert.IsTrue(await service.ValidateTokenAsync(), "Token issued by register should validate");

            // Duplicate registration surfaces the localized key for "username taken".
            var dup = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync("alice", "secret123"));
            Assert.AreEqual("SyncErrorUsernameTaken", dup.Message);

            // Logout invalidates the session; login with the wrong password fails.
            await service.LogoutAsync();
            Assert.IsFalse(service.IsAuthenticated);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync("alice", "wrong-password"));

            // Login with the right password.
            var loggedIn = await service.LoginAsync("alice", "secret123");
            Assert.AreEqual("alice", loggedIn.Username);
            Assert.IsTrue(service.IsAuthenticated);

            // Seed a temporary local data store with realistic data.
            var dbPath = Path.Combine(Path.GetTempPath(), $"jxsync_{Guid.NewGuid():N}.db");
            DataStore.SetDatabasePathForTesting(dbPath);
            try
            {
                await DataStore.SaveConnectionsAsync(new[]
                {
                    new ConnectionGroup("Prod")
                    {
                        Connections = { new SavedConnection { Name = "web1", Host = "web1.local", Username = "deploy", Password = "secret-login-pwd" } }
                    }
                });
                await DataStore.SaveSnippetsAsync(new[] { new SnippetItem { Name = "Disk", Command = "df -h" } });
                await DataStore.SaveSettingsAsync(new TerminalSettings { FontSize = 17, AiApiKey = "sk-e2ee-ai" });
                await DataStore.SaveCommandHistoryAsync("top -b -n 1");

                // Push
                var pushed = await service.PushBackupAsync("before migration");
                Assert.IsFalse(string.IsNullOrWhiteSpace(pushed.Id), "Push should return a backup id");

                // List
                var list = await service.ListBackupsAsync();
                Assert.AreEqual(1, list.Count);
                Assert.AreEqual("integration-host", list[0].DeviceName);

                // Download and verify the snapshot round-trips.
                var snapshot = await service.DownloadBackupAsync(pushed.Id);
                Assert.AreEqual("web1", snapshot.Connections[0].Connections[0].Name);
                Assert.AreEqual("secret-login-pwd", snapshot.Connections[0].Connections[0].Password);
                Assert.AreEqual("df -h", snapshot.Snippets[0].Command);
                Assert.IsNotNull(snapshot.Settings);
                Assert.AreEqual(17, snapshot.Settings!.FontSize);
                Assert.AreEqual("sk-e2ee-ai", snapshot.Settings.AiApiKey);
                Assert.AreEqual(1, snapshot.History.Count);
                Assert.AreEqual("top -b -n 1", snapshot.History[0].Command);

                // Restore into a fresh store.
                var dbPath2 = Path.Combine(Path.GetTempPath(), $"jxsync_{Guid.NewGuid():N}.db");
                DataStore.SetDatabasePathForTesting(dbPath2);
                try
                {
                    await service.ApplySnapshotAsync(snapshot);
                    var reloaded = await DataStore.LoadConnectionsAsync();
                    Assert.AreEqual("web1", reloaded[0].Connections[0].Name);
                    Assert.AreEqual("secret-login-pwd", reloaded[0].Connections[0].Password);

                    var reloadedHistory = await DataStore.LoadCommandHistoryItemsAsync();
                    Assert.AreEqual(1, reloadedHistory.Count);
                    Assert.AreEqual("top -b -n 1", reloadedHistory[0].Command);

                    var reloadedSettings = DataStore.LoadSettings();
                    Assert.AreEqual("sk-e2ee-ai", reloadedSettings.AiApiKey);
                }
                finally
                {
                    DataStore.SetDatabasePathForTesting(dbPath);
                    try { File.Delete(dbPath2); } catch { }
                }

                // A second account sees an empty list and cannot touch alice's backup.
                var bob = await service.RegisterAsync("bob", "secret123");
                Assert.AreEqual(0, (await service.ListBackupsAsync()).Count);
                var bobIsolation = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadBackupAsync(pushed.Id));
                Assert.AreEqual("SyncDownloadFailed", bobIsolation.Message);

                // Back to alice: delete succeeds, then the list is empty.
                await service.LoginAsync("alice", "secret123");
                await service.DeleteBackupAsync(pushed.Id);
                Assert.AreEqual(0, (await service.ListBackupsAsync()).Count);
            }
            finally
            {
                DataStore.SetDatabasePathForTesting(null);
                try { File.Delete(dbPath); } catch { }
            }
        }
        finally
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            proc.WaitForExit(3000);
            try { Directory.Delete(serverDataDir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task AdminApi_ManagesUsers_AccessControl_AndStaticWebUi()
    {
        var siblingServerDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "JxTerminal.Server"));
        var innerServerDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "JxTerminal.Server"));
        var serverDir = Directory.Exists(siblingServerDir) ? siblingServerDir : innerServerDir;
        if (!Directory.Exists(serverDir))
        {
            Assert.Inconclusive("JxTerminal.Server project not found.");
        }

        var port = GetFreePort();
        var serverDataDir = Path.Combine(Path.GetTempPath(), $"jxserver_admin_{Guid.NewGuid():N}");
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList =
                {
                    "run",
                    "--project", Path.Combine(serverDir, "JxTerminal.Server.csproj"),
                    "--urls", $"http://127.0.0.1:{port}",
                    "--Backup:Directory", serverDataDir
                },
                WorkingDirectory = serverDir,
                CreateNoWindow = true,
                UseShellExecute = false
            }
        };
        proc.Start();

        try
        {
            using var http = new System.Net.Http.HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            // 1. Wait for server
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var health = await http.GetAsync("/api/health");
                    if (health.IsSuccessStatusCode) break;
                }
                catch { }
                await Task.Delay(500);
            }

            // 2. Test static web UI
            var indexHtml = await http.GetStringAsync("/");
            Assert.IsTrue(indexHtml.Contains("JxTerminal Server"), "Static Web UI should be served at /");

            // 3. First user registration automatically becomes Admin
            var regAdminRes = await http.PostAsJsonAsync("/api/auth/register", new { username = "superadmin", password = "password123" });
            Assert.IsTrue(regAdminRes.IsSuccessStatusCode);
            var adminAuth = await regAdminRes.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
            Assert.IsNotNull(adminAuth);
            Assert.IsTrue(adminAuth["isAdmin"]?.GetValue<bool>() == true, "First registered user must be Admin");
            string adminToken = adminAuth["token"]!.GetValue<string>();

            // 4. Second user registers as normal user
            var regUserRes = await http.PostAsJsonAsync("/api/auth/register", new { username = "regularuser", password = "password123" });
            Assert.IsTrue(regUserRes.IsSuccessStatusCode);
            var userAuth = await regUserRes.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
            Assert.IsNotNull(userAuth);
            Assert.IsFalse(userAuth["isAdmin"]?.GetValue<bool>() == true, "Second registered user must NOT be Admin");
            string userToken = userAuth["token"]!.GetValue<string>();
            string userId = "";

            // 5. Admin lists users
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
            var usersList = await http.GetFromJsonAsync<System.Text.Json.Nodes.JsonArray>("/api/admin/users");
            Assert.IsNotNull(usersList);
            Assert.AreEqual(2, usersList.Count);

            foreach (var node in usersList)
            {
                if (node?["username"]?.GetValue<string>() == "regularuser")
                {
                    userId = node["id"]!.GetValue<string>();
                }
            }
            Assert.IsFalse(string.IsNullOrEmpty(userId));

            // 6. Admin deletes user
            var delRes = await http.DeleteAsync($"/api/admin/users/{userId}");
            Assert.AreEqual(HttpStatusCode.NoContent, delRes.StatusCode);

            // 7. Deleted user cannot log in
            http.DefaultRequestHeaders.Authorization = null;
            var reLoginRes = await http.PostAsJsonAsync("/api/auth/login", new { username = "regularuser", password = "password123" });
            Assert.IsFalse(reLoginRes.IsSuccessStatusCode, "Deleted user must not be able to log in");

            // 8. Admin configures registration secret
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
            var updateCfgRes = await http.PutAsJsonAsync("/api/admin/config", new { allowRegistration = true, registrationSecret = "INVITE_ONLY_123" });
            Assert.IsTrue(updateCfgRes.IsSuccessStatusCode);

            // 9. Register without secret fails
            http.DefaultRequestHeaders.Authorization = null;
            var failReg = await http.PostAsJsonAsync("/api/auth/register", new { username = "intruder", password = "password123" });
            Assert.AreEqual(HttpStatusCode.BadRequest, failReg.StatusCode);

            // 10. Register with correct secret succeeds
            var successReg = await http.PostAsJsonAsync("/api/auth/register", new { username = "vip_guest", password = "password123", secret = "INVITE_ONLY_123" });
            Assert.IsTrue(successReg.IsSuccessStatusCode);

            // 11. Admin closes registration completely
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
            await http.PutAsJsonAsync("/api/admin/config", new { allowRegistration = false, registrationSecret = "" });

            // 12. Register when closed fails
            http.DefaultRequestHeaders.Authorization = null;
            var closedReg = await http.PostAsJsonAsync("/api/auth/register", new { username = "late_guest", password = "password123" });
            Assert.AreEqual(HttpStatusCode.BadRequest, closedReg.StatusCode);
        }
        finally
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            proc.WaitForExit(3000);
            try { Directory.Delete(serverDataDir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task ClientSyncService_SyncNow_SynchronizesTwoDevices()
    {
        var siblingServerDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "JxTerminal.Server"));
        var innerServerDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "JxTerminal.Server"));
        var serverDir = Directory.Exists(siblingServerDir) ? siblingServerDir : innerServerDir;
        if (!Directory.Exists(serverDir))
        {
            Assert.Inconclusive("JxTerminal.Server project not found.");
        }

        var port = GetFreePort();
        var serverDataDir = Path.Combine(Path.GetTempPath(), $"jxserver_sync_{Guid.NewGuid():N}");
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList =
                {
                    "run",
                    "--project", Path.Combine(serverDir, "JxTerminal.Server.csproj"),
                    "--urls", $"http://127.0.0.1:{port}",
                    "--Backup:Directory", serverDataDir
                },
                WorkingDirectory = serverDir,
                CreateNoWindow = true,
                UseShellExecute = false
            }
        };
        proc.Start();

        try
        {
            var service = SyncService.Instance;
            await service.SaveConfigAsync(new SyncConfig { ServerUrl = $"http://127.0.0.1:{port}", DeviceName = "device-1" });
            await WaitForServerAsync(service);

            // Register account
            await service.RegisterAsync("syncuser", "syncpass123");
            Assert.IsTrue(service.IsAuthenticated);

            var dbPath1 = Path.Combine(Path.GetTempPath(), $"jxdev1_{Guid.NewGuid():N}.db");
            var dbPath2 = Path.Combine(Path.GetTempPath(), $"jxdev2_{Guid.NewGuid():N}.db");

            try
            {
                // ---- Device 1: Add a connection and SyncNow ----
                DataStore.SetDatabasePathForTesting(dbPath1);
                await DataStore.SaveConnectionsAsync(new[]
                {
                    new ConnectionGroup("Production")
                    {
                        Connections = { new SavedConnection { Name = "srv-1", Host = "1.2.3.4", Username = "admin", Password = "mypassword" } }
                    }
                });

                var syncResult1 = await service.SyncNowAsync();
                Assert.IsTrue(syncResult1.Success);
                Assert.AreEqual(SyncActionTaken.Pushed, syncResult1.Action);

                // ---- Device 2: Fresh database, same account, SyncNow ----
                DataStore.SetDatabasePathForTesting(dbPath2);
                service.Config.LastSyncTime = null; // Fresh device has never synced
                service.Config.DeviceName = "device-2";

                var syncResult2 = await service.SyncNowAsync();
                Assert.IsTrue(syncResult2.Success);
                Assert.AreEqual(SyncActionTaken.Pulled, syncResult2.Action);

                var loadedOnDev2 = await DataStore.LoadConnectionsAsync();
                Assert.AreEqual(1, loadedOnDev2.Count);
                Assert.AreEqual("srv-1", loadedOnDev2[0].Connections[0].Name);
                Assert.AreEqual("mypassword", loadedOnDev2[0].Connections[0].Password);

                // Device 2 with no changes syncing again: Up to date
                var syncResult2Noop = await service.SyncNowAsync();
                Assert.IsTrue(syncResult2Noop.Success);
                Assert.AreEqual(SyncActionTaken.None, syncResult2Noop.Action);

                // Device 2 adds a snippet and syncs
                await DataStore.SaveSnippetsAsync(new[] { new SnippetItem { Name = "DiskFree", Command = "df -h" } });
                var syncResult2Snippet = await service.SyncNowAsync();
                Assert.IsTrue(syncResult2Snippet.Success);
                Assert.AreEqual(SyncActionTaken.Pushed, syncResult2Snippet.Action);

                // ---- Back to Device 1: SyncNow pulls snippet from Device 2 ----
                DataStore.SetDatabasePathForTesting(dbPath1);
                await service.LoadConfigAsync();
                var syncResult1Pull = await service.SyncNowAsync();
                Assert.IsTrue(syncResult1Pull.Success);
                Assert.AreEqual(SyncActionTaken.Pulled, syncResult1Pull.Action);

                var loadedSnippetsOnDev1 = await DataStore.LoadSnippetsAsync();
                Assert.IsTrue(loadedSnippetsOnDev1.Any(s => s.Name == "DiskFree" && s.Command == "df -h"));
            }
            finally
            {
                DataStore.SetDatabasePathForTesting(null);
                try { File.Delete(dbPath1); } catch { }
                try { File.Delete(dbPath2); } catch { }
            }
        }
        finally
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            proc.WaitForExit(3000);
            try { Directory.Delete(serverDataDir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task ClientSyncService_AutoRepairsCloudSnapshot_WhenCredentialsPreserved()
    {
        var siblingServerDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "JxTerminal.Server"));
        var innerServerDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "JxTerminal.Server"));
        var serverDir = Directory.Exists(siblingServerDir) ? siblingServerDir : innerServerDir;
        if (!Directory.Exists(serverDir))
        {
            Assert.Inconclusive("JxTerminal.Server project not found.");
        }

        var port = GetFreePort();
        var serverDataDir = Path.Combine(Path.GetTempPath(), $"jxserver_{Guid.NewGuid():N}");
        Process StartServerProcess()
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    ArgumentList =
                    {
                        "run",
                        "--project", Path.Combine(serverDir, "JxTerminal.Server.csproj"),
                        "--no-build",
                        "--urls", $"http://127.0.0.1:{port}",
                        "--data-dir", serverDataDir
                    },
                    WorkingDirectory = serverDir,
                    CreateNoWindow = true,
                    UseShellExecute = false
                }
            };
            p.Start();
            return p;
        }

        var proc = StartServerProcess();

        try
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"jxsync_repair_{Guid.NewGuid():N}.db");
            DataStore.SetDatabasePathForTesting(dbPath);

            var service = SyncService.Instance;
            await service.SaveConfigAsync(new SyncConfig
            {
                ServerUrl = $"http://127.0.0.1:{port}",
                DeviceName = "device-1",
                AutoSyncEnabled = false
            });
            await WaitForServerAsync(service);

            try
            {
                // 1. Register with Password A and push a snapshot with an SSH password
                await service.RegisterAsync("repairuser", "Password_A_123");
                var group = new ConnectionGroup("Servers");
                group.Connections.Add(new SavedConnection
                {
                    Id = Guid.NewGuid(),
                    Name = "prod-server",
                    Host = "1.2.3.4",
                    Username = "root",
                    Password = "super-secret-ssh-password"
                });
                await DataStore.SaveConnectionsAsync(new[] { group });
                var push1 = await service.SyncNowAsync(forcePush: true);
                Assert.AreEqual(SyncActionTaken.Pushed, push1.Action);

                // 2. Stop server, reset password via CLI, and restart server
                try { proc.Kill(entireProcessTree: true); } catch { }
                proc.WaitForExit(3000);

                using var cliProc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        ArgumentList =
                        {
                            "run",
                            "--project", Path.Combine(serverDir, "JxTerminal.Server.csproj"),
                            "--no-build",
                            "--",
                            "--data-dir", serverDataDir,
                            "--reset-password", "repairuser", "Password_B_456"
                        },
                        WorkingDirectory = serverDir,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    }
                };
                cliProc.Start();
                await cliProc.WaitForExitAsync();
                Assert.AreEqual(0, cliProc.ExitCode);

                proc = StartServerProcess();
                await WaitForServerAsync(service);

                // 3. User logs in with the NEW Password B on the client
                // This derives the new _syncKey from Password B
                await service.LoginAsync("repairuser", "Password_B_456");

                // 4. Client runs SyncNowAsync()
                // It pulls the snapshot from server (encrypted with Password A).
                // Decryption of SSH password fails because tags don't match.
                // ApplySnapshotAsync retains the local password "super-secret-ssh-password".
                // credentialsPreserved is true -> auto-repairs by pushing new snapshot encrypted with Password B!
                var autoRepairResult = await service.SyncNowAsync();
                Assert.IsTrue(autoRepairResult.Success);
                Assert.AreEqual(SyncActionTaken.Pushed, autoRepairResult.Action);

                // 5. Verification 1: Local DB still has the intact SSH password
                var reloadedLocal = await DataStore.LoadConnectionsAsync();
                Assert.AreEqual("super-secret-ssh-password", reloadedLocal[0].Connections[0].Password);

                // 6. Verification 2: Pull the newly updated snapshot from cloud and decrypt with Password B
                var cloudSnapshot = await service.PullLatestSnapshotAsync();
                Assert.AreEqual("super-secret-ssh-password", cloudSnapshot.Connections[0].Connections[0].Password);
            }
            finally
            {
                DataStore.SetDatabasePathForTesting(null);
                try { File.Delete(dbPath); } catch { }
            }
        }
        finally
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            proc.WaitForExit(3000);
            try { Directory.Delete(serverDataDir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task ClientSyncService_UserCanChangeOwnPassword_And_AutoReEncryptsCloudSnapshot()
    {
        var siblingServerDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "JxTerminal.Server"));
        var innerServerDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "JxTerminal.Server"));
        var serverDir = Directory.Exists(siblingServerDir) ? siblingServerDir : innerServerDir;
        if (!Directory.Exists(serverDir))
        {
            Assert.Inconclusive("JxTerminal.Server project not found.");
        }

        var port = GetFreePort();
        var serverDataDir = Path.Combine(Path.GetTempPath(), $"jxserver_{Guid.NewGuid():N}");
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList =
                {
                    "run",
                    "--project", Path.Combine(serverDir, "JxTerminal.Server.csproj"),
                    "--no-build",
                    "--urls", $"http://127.0.0.1:{port}",
                    "--data-dir", serverDataDir
                },
                WorkingDirectory = serverDir,
                CreateNoWindow = true,
                UseShellExecute = false
            }
        };
        proc.Start();

        try
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"jxsync_chgpass_{Guid.NewGuid():N}.db");
            DataStore.SetDatabasePathForTesting(dbPath);

            var service = SyncService.Instance;
            await service.SaveConfigAsync(new SyncConfig
            {
                ServerUrl = $"http://127.0.0.1:{port}",
                DeviceName = "device-1",
                AutoSyncEnabled = false
            });
            await WaitForServerAsync(service);

            try
            {
                // 1. Register user and push snapshot
                await service.RegisterAsync("alice", "Pass_Old_123");
                var group = new ConnectionGroup("Servers");
                group.Connections.Add(new SavedConnection
                {
                    Id = Guid.NewGuid(),
                    Name = "alice-server",
                    Host = "10.0.0.1",
                    Username = "root",
                    Password = "alice-secret-ssh-password"
                });
                await DataStore.SaveConnectionsAsync(new[] { group });
                await service.SyncNowAsync(forcePush: true);

                // 2. Changing password with incorrect current password fails
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.ChangePasswordAsync("WrongPassword", "Pass_New_456"));
                Assert.AreEqual("SyncErrorCurrentPasswordIncorrect", ex.Message);

                // 3. Changing password with valid current password succeeds
                await service.ChangePasswordAsync("Pass_Old_123", "Pass_New_456");

                // 4. Verify login with old password now fails
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.LoginAsync("alice", "Pass_Old_123"));

                // 5. Verify login with new password succeeds
                var loginRes = await service.LoginAsync("alice", "Pass_New_456");
                Assert.AreEqual("alice", loginRes.Username);

                // 6. Verify cloud snapshot was re-encrypted with new password: pull and decrypt!
                var cloudSnapshot = await service.PullLatestSnapshotAsync();
                Assert.AreEqual("alice-secret-ssh-password", cloudSnapshot.Connections[0].Connections[0].Password);
            }
            finally
            {
                DataStore.SetDatabasePathForTesting(null);
                try { File.Delete(dbPath); } catch { }
            }
        }
        finally
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            proc.WaitForExit(3000);
            try { Directory.Delete(serverDataDir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task ServerCli_ResetPassword_And_UserManagement_WorksAccurately()
    {
        var siblingServerDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "JxTerminal.Server"));
        var innerServerDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "JxTerminal.Server"));
        var serverDir = Directory.Exists(siblingServerDir) ? siblingServerDir : innerServerDir;
        if (!Directory.Exists(serverDir))
        {
            Assert.Inconclusive("JxTerminal.Server project not found.");
        }

        var serverDataDir = Path.Combine(Path.GetTempPath(), $"jxcli_{Guid.NewGuid():N}");
        Directory.CreateDirectory(serverDataDir);

        async Task<(int ExitCode, string Output)> RunCliAsync(params string[] args)
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    WorkingDirectory = serverDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            proc.StartInfo.ArgumentList.Add("run");
            proc.StartInfo.ArgumentList.Add("--project");
            proc.StartInfo.ArgumentList.Add(Path.Combine(serverDir, "JxTerminal.Server.csproj"));
            proc.StartInfo.ArgumentList.Add("--no-build");
            proc.StartInfo.ArgumentList.Add("--");
            proc.StartInfo.ArgumentList.Add("--data-dir");
            proc.StartInfo.ArgumentList.Add(serverDataDir);
            foreach (var a in args) proc.StartInfo.ArgumentList.Add(a);

            proc.Start();
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return (proc.ExitCode, stdout + "\n" + stderr);
        }

        try
        {
            // 1. Initial list: no users
            var (code1, out1) = await RunCliAsync("--list-users");
            Assert.AreEqual(0, code1);
            Assert.IsTrue(out1.Contains("No registered users found"));

            // 2. Create admin
            var (code2, out2) = await RunCliAsync("--create-admin", "superadmin", "InitialPass123");
            Assert.AreEqual(0, code2);
            Assert.IsTrue(out2.Contains("created successfully"));

            // 3. List users: should show superadmin
            var (code3, out3) = await RunCliAsync("--list-users");
            Assert.AreEqual(0, code3);
            Assert.IsTrue(out3.Contains("superadmin") && out3.Contains("Admin"));

            // 4. Reset password
            var (code4, out4) = await RunCliAsync("--reset-password", "superadmin", "NewPass987654");
            Assert.AreEqual(0, code4);
            Assert.IsTrue(out4.Contains("successfully reset"));

            // 5. Delete user
            var (code5, out5) = await RunCliAsync("--delete-user", "superadmin");
            Assert.AreEqual(0, code5);
            Assert.IsTrue(out5.Contains("deleted successfully"));

            // 6. List users: empty again
            var (code6, out6) = await RunCliAsync("--list-users");
            Assert.AreEqual(0, code6);
            Assert.IsTrue(out6.Contains("No registered users found"));
        }
        finally
        {
            try { Directory.Delete(serverDataDir, recursive: true); } catch { }
        }
    }

    private static async Task WaitForServerAsync(SyncService service)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await service.TestConnectionAsync()) return;
            }
            catch
            {
                // Server not ready yet.
            }
            await Task.Delay(500);
        }
        throw new TimeoutException("JxTerminal.Server did not become ready in time.");
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
