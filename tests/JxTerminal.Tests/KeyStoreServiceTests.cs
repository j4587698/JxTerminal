using System;
using System.Threading.Tasks;
using JxTerminal.Models;
using JxTerminal.Services;
using JxTerminal.Services.KeyStore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
public class KeyStoreServiceTests
{
    [TestMethod]
    public async Task GenerateRsaKey_GeneratesValidPemAndOpenSshKey()
    {
        var service = new KeyStoreService();
        var key = await service.GenerateKeyAsync("test_rsa", KeyType.RSA2048);

        Assert.IsNotNull(key);
        Assert.AreEqual("test_rsa", key.Name);
        Assert.AreEqual(KeyType.RSA2048, key.Type);
        Assert.IsTrue(key.PrivateKeyPem.Trim().StartsWith("-----BEGIN PRIVATE KEY-----"));
        Assert.IsTrue(key.PublicKeyOpenSsh.StartsWith("ssh-rsa "));
        Assert.IsTrue(key.Fingerprint.StartsWith("SHA256:"));
    }

    [TestMethod]
    public void KeyItem_EncryptsAndDecryptsPrivateKeyProperly()
    {
        var key = new KeyItem
        {
            Name = "SecretKey",
            PrivateKeyPem = "-----BEGIN PRIVATE KEY-----\nMIIEvgIBADANBgkqhkiG9w0BAQEFAASCBKgwggSkAgEAAoIBAQC...\n-----END PRIVATE KEY-----"
        };

        var encrypted = key.EncryptedPrivateKey;
        Assert.IsFalse(string.IsNullOrEmpty(encrypted));
        Assert.AreNotEqual(key.PrivateKeyPem, encrypted);

        var loadedKey = new KeyItem
        {
            EncryptedPrivateKey = encrypted
        };

        Assert.AreEqual(key.PrivateKeyPem, loadedKey.PrivateKeyPem);
    }

    [TestMethod]
    public void ComputeFingerprint_CalculatesExpectedSha256()
    {
        var pubKey = "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQC3 test@machine";
        var fp = KeyStoreService.ComputeFingerprint(pubKey);

        Assert.IsTrue(fp.StartsWith("SHA256:"));
    }

    [TestMethod]
    public async Task GeneratedRsaKey_LoadsBackViaSshNet()
    {
        var service = new KeyStoreService();
        var key = await service.GenerateKeyAsync("roundtrip_rsa", KeyType.RSA2048);

        using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(key.PrivateKeyPem));
        using var keyFile = new Renci.SshNet.PrivateKeyFile(stream);
        Assert.IsNotNull(keyFile);
    }

    [TestMethod]
    public async Task GeneratedEcdsaKey_LoadsBackViaSshNet()
    {
        var service = new KeyStoreService();
        var key = await service.GenerateKeyAsync("roundtrip_ecdsa", KeyType.ECDSA);

        Assert.AreEqual(KeyType.ECDSA, key.Type);
        Assert.IsTrue(key.PublicKeyOpenSsh.StartsWith("ecdsa-sha2-nistp256 "));
        using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(key.PrivateKeyPem));
        using var keyFile = new Renci.SshNet.PrivateKeyFile(stream);
        Assert.IsNotNull(keyFile);
    }

    [TestMethod]
    public async Task GenerateEd25519_ThrowsInsteadOfSilentDowngrade()
    {
        var service = new KeyStoreService();
        await Assert.ThrowsExactlyAsync<NotSupportedException>(
            () => service.GenerateKeyAsync("no_ed25519", KeyType.ED25519));
    }

    [TestMethod]
    public async Task KeyItem_Serialization_WithAppJsonContext_PreservesAllProperties()
    {
        var service = new KeyStoreService();
        var key = await service.GenerateKeyAsync("serialization_test", KeyType.RSA2048);
        key.Notes = "Test notes";

        var json = System.Text.Json.JsonSerializer.Serialize(key, AppJsonContext.Default.KeyItem);
        Assert.IsTrue(json.Contains("serialization_test"), "JSON missing Name value");
        Assert.IsTrue(json.Contains("PublicKeyOpenSsh"), "JSON missing PublicKeyOpenSsh");
        Assert.IsTrue(json.Contains("Fingerprint"), "JSON missing Fingerprint");
        Assert.IsTrue(json.Contains("Test notes"), "JSON missing Notes");

        var deserialized = System.Text.Json.JsonSerializer.Deserialize(json, AppJsonContext.Default.KeyItem);
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(key.Name, deserialized.Name);
        Assert.AreEqual(key.PublicKeyOpenSsh, deserialized.PublicKeyOpenSsh);
        Assert.AreEqual(key.Fingerprint, deserialized.Fingerprint);
        Assert.AreEqual(key.Notes, deserialized.Notes);
    }

    [TestMethod]
    public async Task KeyStoreService_RecoverKeyMetadata_RecoversMissingPublicKeyAndFingerprint()
    {
        var service = new KeyStoreService();
        var original = await service.GenerateKeyAsync("recover_test", KeyType.RSA2048);

        // Corrupt/wipe metadata as if loaded from old buggy json
        var corrupted = new KeyItem
        {
            Id = original.Id,
            Name = "recover_test",
            EncryptedPrivateKey = original.EncryptedPrivateKey,
            PublicKeyOpenSsh = string.Empty,
            Fingerprint = string.Empty
        };

        KeyStoreService.RecoverKeyMetadata(corrupted);

        Assert.AreEqual(KeyType.RSA2048, corrupted.Type);
        Assert.IsTrue(corrupted.PublicKeyOpenSsh.StartsWith("ssh-rsa "));
        Assert.IsTrue(corrupted.Fingerprint.StartsWith("SHA256:"));
        Assert.AreEqual(original.PublicKeyOpenSsh, corrupted.PublicKeyOpenSsh);
        Assert.AreEqual(original.Fingerprint, corrupted.Fingerprint);
    }
}

