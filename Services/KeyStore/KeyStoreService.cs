using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using JxTerminal.Models;

namespace JxTerminal.Services.KeyStore;

public class KeyStoreService : IKeyStoreService
{
    private static KeyStoreService? _instance;
    public static KeyStoreService Instance => _instance ??= new KeyStoreService();

    public ObservableCollection<KeyItem> Keys { get; } = new();

    public async Task LoadKeysAsync()
    {
        var list = await DataStore.LoadKeysAsync();
        Keys.Clear();
        bool needsResave = false;
        foreach (var item in list)
        {
            if ((string.IsNullOrWhiteSpace(item.PublicKeyOpenSsh) || string.IsNullOrWhiteSpace(item.Fingerprint))
                && !string.IsNullOrWhiteSpace(item.PrivateKeyPem))
            {
                RecoverKeyMetadata(item);
                needsResave = true;
            }
            Keys.Add(item);
        }

        if (needsResave)
        {
            await DataStore.SaveKeysAsync(Keys);
        }
    }

    public static void RecoverKeyMetadata(KeyItem item)
    {
        var keyComment = $"{item.Name}@{Environment.MachineName}";
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(item.PrivateKeyPem);
            item.Type = rsa.KeySize >= 4096 ? KeyType.RSA4096 : KeyType.RSA2048;
            item.PublicKeyOpenSsh = FormatOpenSshRsa(rsa, keyComment);
            item.Fingerprint = ComputeFingerprint(item.PublicKeyOpenSsh);
            return;
        }
        catch { }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(item.PrivateKeyPem);
            item.Type = KeyType.ECDSA;
            item.PublicKeyOpenSsh = FormatOpenSshEcdsa(ecdsa, keyComment);
            item.Fingerprint = ComputeFingerprint(item.PublicKeyOpenSsh);
            return;
        }
        catch { }

        item.Type = KeyType.ED25519;
        if (string.IsNullOrWhiteSpace(item.PublicKeyOpenSsh))
            item.PublicKeyOpenSsh = "(Imported Key)";
        if (string.IsNullOrWhiteSpace(item.Fingerprint))
            item.Fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(item.PrivateKeyPem))).TrimEnd('=');
    }

    public async Task<KeyItem> GenerateKeyAsync(string name, KeyType type, string? comment = null)
    {
        var item = new KeyItem
        {
            Name = name,
            Type = type,
            CreatedAt = DateTime.Now
        };

        var keyComment = string.IsNullOrWhiteSpace(comment) ? $"{name}@{Environment.MachineName}" : comment;

        switch (type)
        {
            case KeyType.RSA2048:
            case KeyType.RSA4096:
            {
                int bits = type == KeyType.RSA4096 ? 4096 : 2048;
                using var rsa = RSA.Create(bits);
                item.PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem();
                item.PublicKeyOpenSsh = FormatOpenSshRsa(rsa, keyComment);
                item.Fingerprint = ComputeFingerprint(item.PublicKeyOpenSsh);
                break;
            }
            case KeyType.ECDSA:
            {
                using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                item.PrivateKeyPem = ecdsa.ExportPkcs8PrivateKeyPem();
                item.PublicKeyOpenSsh = FormatOpenSshEcdsa(ecdsa, keyComment);
                item.Fingerprint = ComputeFingerprint(item.PublicKeyOpenSsh);
                break;
            }
            case KeyType.ED25519:
                // .NET has no built-in Ed25519 implementation; never silently substitute
                // a different algorithm (a previous version emitted RSA-2048 labeled as
                // ED25519). Imported ED25519 keys are still usable for authentication.
                throw new NotSupportedException(
                    "ED25519 key generation is not supported. Generate an ECDSA or RSA key instead, or import an existing ED25519 key.");
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        await SaveKeyAsync(item);
        return item;
    }

    public async Task<KeyItem> ImportKeyAsync(string name, string privateKeyContent, string? comment = null)
    {
        var item = new KeyItem
        {
            Name = name,
            PrivateKeyPem = privateKeyContent.Trim(),
            CreatedAt = DateTime.Now,
            Notes = comment ?? string.Empty
        };

        // Attempt to parse and extract public key and fingerprint
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(item.PrivateKeyPem);
            item.Type = rsa.KeySize >= 4096 ? KeyType.RSA4096 : KeyType.RSA2048;
            item.PublicKeyOpenSsh = FormatOpenSshRsa(rsa, comment ?? name);
            item.Fingerprint = ComputeFingerprint(item.PublicKeyOpenSsh);
        }
        catch
        {
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(item.PrivateKeyPem);
                item.Type = KeyType.ECDSA;
                item.PublicKeyOpenSsh = FormatOpenSshEcdsa(ecdsa, comment ?? name);
                item.Fingerprint = ComputeFingerprint(item.PublicKeyOpenSsh);
            }
            catch
            {
                // Generic import
                item.Type = KeyType.ED25519;
                item.PublicKeyOpenSsh = "(Imported Key)";
                item.Fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(privateKeyContent))).TrimEnd('=');
            }
        }

        await SaveKeyAsync(item);
        return item;
    }

    public async Task SaveKeyAsync(KeyItem key)
    {
        var existing = Keys.FirstOrDefault(k => k.Id == key.Id);
        if (existing == null)
        {
            Keys.Add(key);
        }
        else
        {
            int index = Keys.IndexOf(existing);
            Keys[index] = key;
        }

        await DataStore.SaveKeysAsync(Keys);
    }

    public async Task DeleteKeyAsync(Guid keyId)
    {
        var item = Keys.FirstOrDefault(k => k.Id == keyId);
        if (item != null)
        {
            Keys.Remove(item);
            await DataStore.SaveKeysAsync(Keys);
        }
    }

    public KeyItem? GetKey(Guid keyId)
    {
        return Keys.FirstOrDefault(k => k.Id == keyId);
    }

    public static string ComputeFingerprint(string openSshPubKey)
    {
        try
        {
            var parts = openSshPubKey.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var rawBytes = Convert.FromBase64String(parts[1]);
                var hash = SHA256.HashData(rawBytes);
                return "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');
            }
        }
        catch { /* ignore */ }

        return "SHA256:unknown";
    }

    private static string FormatOpenSshRsa(RSA rsa, string comment)
    {
        var parameters = rsa.ExportParameters(false);
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        WriteSshString(writer, "ssh-rsa"u8.ToArray());
        WriteSshMpint(writer, parameters.Exponent!);
        WriteSshMpint(writer, parameters.Modulus!);

        var raw = ms.ToArray();
        return $"ssh-rsa {Convert.ToBase64String(raw)} {comment}".Trim();
    }

    private static string FormatOpenSshEcdsa(ECDsa ecdsa, string comment)
    {
        var parameters = ecdsa.ExportParameters(false);
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        WriteSshString(writer, "ecdsa-sha2-nistp256"u8.ToArray());
        WriteSshString(writer, "nistp256"u8.ToArray());

        var q = new byte[1 + parameters.Q.X!.Length + parameters.Q.Y!.Length];
        q[0] = 0x04;
        Buffer.BlockCopy(parameters.Q.X, 0, q, 1, parameters.Q.X.Length);
        Buffer.BlockCopy(parameters.Q.Y, 0, q, 1 + parameters.Q.X.Length, parameters.Q.Y.Length);
        WriteSshBytes(writer, q);

        var raw = ms.ToArray();
        return $"ecdsa-sha2-nistp256 {Convert.ToBase64String(raw)} {comment}".Trim();
    }

    private static void WriteSshString(BinaryWriter writer, byte[] data)
    {
        writer.Write(ToBigEndian((uint)data.Length));
        writer.Write(data);
    }

    private static void WriteSshBytes(BinaryWriter writer, byte[] data)
    {
        writer.Write(ToBigEndian((uint)data.Length));
        writer.Write(data);
    }

    private static void WriteSshMpint(BinaryWriter writer, byte[] value)
    {
        if (value.Length == 0)
        {
            writer.Write(0);
            return;
        }

        // Leading 0x00 if high bit is set
        if ((value[0] & 0x80) != 0)
        {
            writer.Write(ToBigEndian((uint)(value.Length + 1)));
            writer.Write((byte)0);
            writer.Write(value);
        }
        else
        {
            writer.Write(ToBigEndian((uint)value.Length));
            writer.Write(value);
        }
    }

    private static uint ToBigEndian(uint val)
    {
        if (BitConverter.IsLittleEndian)
        {
            return ((val & 0x000000FF) << 24) |
                   ((val & 0x0000FF00) << 8)  |
                   ((val & 0x00FF0000) >> 8)  |
                   ((val & 0xFF000000) >> 24);
        }
        return val;
    }
}
