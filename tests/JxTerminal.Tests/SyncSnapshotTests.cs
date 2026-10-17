using System.Text.Json;
using JxTerminal.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
public class SyncSnapshotTests
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions CaseInsensitive = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private static SyncSnapshot RoundTrip(SyncSnapshot snapshot)
    {
        // Mirrors SyncService: the payload is serialized as the snapshot JSON string,
        // nested inside the server backup record, so we round-trip both layers.
        var payload = JsonSerializer.Serialize(snapshot, Indented);
        var recordJson = JsonSerializer.Serialize(new { snapshotJson = payload }, Indented);
        var record = JsonSerializer.Deserialize<RemoteBackupRecord>(recordJson, CaseInsensitive)!;
        return JsonSerializer.Deserialize<SyncSnapshot>(record.SnapshotJson, CaseInsensitive)!;
    }

    [TestMethod]
    public void RoundTrip_PreservesConnectionsSettingsSnippetsAndKeys()
    {
        var snapshot = new SyncSnapshot
        {
            AppVersion = "1.2.0",
            DeviceName = "workstation-01",
            Connections =
            {
                new ConnectionGroup("Production")
                {
                    Connections = { new SavedConnection { Name = "web1", Host = "web1.local", Port = 22, Username = "deploy", Type = ConnectionType.Ssh } }
                }
            },
            Settings = new TerminalSettings { FontSize = 17, RemoteEncoding = "GBK" },
            Snippets = { new SnippetItem { Name = "Disk", Command = "df -h" } },
            Keys = { new KeyItem { Name = "id_ed25519", Type = KeyType.ED25519, Fingerprint = "SHA256:abc" } }
        };

        var restored = RoundTrip(snapshot);

        Assert.AreEqual("1.2.0", restored.AppVersion);
        Assert.AreEqual("workstation-01", restored.DeviceName);
        Assert.AreEqual(1, restored.Connections.Count);
        Assert.AreEqual("Production", restored.Connections[0].Name);
        Assert.AreEqual("web1", restored.Connections[0].Connections[0].Name);
        Assert.AreEqual("web1.local", restored.Connections[0].Connections[0].Host);
        Assert.AreEqual("deploy", restored.Connections[0].Connections[0].Username);
        Assert.IsNotNull(restored.Settings);
        Assert.AreEqual(17, restored.Settings!.FontSize);
        Assert.AreEqual("GBK", restored.Settings.RemoteEncoding);
        Assert.AreEqual(1, restored.Snippets.Count);
        Assert.AreEqual("df -h", restored.Snippets[0].Command);
        Assert.AreEqual(1, restored.Keys.Count);
        Assert.AreEqual("SHA256:abc", restored.Keys[0].Fingerprint);
    }

    [TestMethod]
    public void Snapshot_DoesNotLeakPlaintextPasswords()
    {
        var snapshot = new SyncSnapshot
        {
            Connections =
            {
                new ConnectionGroup("Servers")
                {
                    Connections = { new SavedConnection { Name = "db", Host = "db.local", Password = "s3cret" } }
                }
            }
        };

        var payload = JsonSerializer.Serialize(snapshot, Indented);
        Assert.IsFalse(payload.Contains("s3cret"), "Passwords must be stored in encrypted form only");
    }

    [TestMethod]
    public void RoundTrip_WithSyncKey_PreservesSecretsAndHistoryAcrossDevices()
    {
        var syncKey = JxTerminal.Services.SecurityService.DeriveKey("user-pass-123", "alice");

        var snapshot = new SyncSnapshot
        {
            AppVersion = "1.3.0",
            DeviceName = "device-alpha",
            Connections =
            {
                new ConnectionGroup("Servers")
                {
                    Connections =
                    {
                        new SavedConnection
                        {
                            Name = "prod-db",
                            Host = "10.0.0.5",
                            Password = "super-db-password",
                            Passphrase = "my-key-passphrase"
                        }
                    }
                }
            },
            Keys =
            {
                new KeyItem
                {
                    Name = "backup_key",
                    PrivateKeyPem = "-----BEGIN OPENSSH PRIVATE KEY-----\nMIIBOgIBAAJ...\n-----END OPENSSH PRIVATE KEY-----"
                }
            },
            Settings = new TerminalSettings
            {
                AiApiKey = "sk-test-ai-key-secret"
            },
            History =
            {
                new TerminalHistoryItem("docker ps -a")
            }
        };

        // Device 1 serializes snapshot with syncKey
        string serializedPayload;
        using (JxTerminal.Services.SecurityService.WithKey(syncKey))
        {
            serializedPayload = JsonSerializer.Serialize(snapshot, Indented);
        }

        // Must not contain plaintexts
        Assert.IsFalse(serializedPayload.Contains("super-db-password"));
        Assert.IsFalse(serializedPayload.Contains("my-key-passphrase"));
        Assert.IsFalse(serializedPayload.Contains("sk-test-ai-key-secret"));
        Assert.IsFalse(serializedPayload.Contains("BEGIN OPENSSH PRIVATE KEY"));

        // Device 2 deserializes with syncKey
        SyncSnapshot restored;
        using (JxTerminal.Services.SecurityService.WithKey(syncKey))
        {
            restored = JsonSerializer.Deserialize<SyncSnapshot>(serializedPayload, CaseInsensitive)!;
        }

        Assert.AreEqual(1, restored.Connections.Count);
        var restoredConn = restored.Connections[0].Connections[0];
        Assert.AreEqual("super-db-password", restoredConn.Password);
        Assert.AreEqual("my-key-passphrase", restoredConn.Passphrase);

        Assert.AreEqual(1, restored.Keys.Count);
        Assert.IsTrue(restored.Keys[0].PrivateKeyPem.Contains("BEGIN OPENSSH PRIVATE KEY"));

        Assert.IsNotNull(restored.Settings);
        Assert.AreEqual("sk-test-ai-key-secret", restored.Settings.AiApiKey);

        Assert.AreEqual(1, restored.History.Count);
        Assert.AreEqual("docker ps -a", restored.History[0].Command);
    }
}
