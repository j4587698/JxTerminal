using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace JxTerminal.Services;

/// <summary>
/// Encrypts locally stored secrets (passwords, passphrases) with a per-installation random
/// master key. On Windows the key file itself is sealed with DPAPI (CurrentUser scope), so it
/// cannot be read by other user accounts. On other platforms the file is restricted to the owner.
/// </summary>
public static class SecurityService
{
    private const string V2Prefix = "v2:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    // Legacy key used by builds prior to the per-installation key. Kept for transparent migration
    // of already stored credentials only; never used for new ciphertext.
    private static readonly byte[] LegacyKey =
        Encoding.UTF8.GetBytes("JxTerminal_Secret_Key_2026_Fixed");

    private static readonly object KeyLock = new();
    private static byte[]? _masterKey;

    private static byte[] MasterKey
    {
        get
        {
            if (_masterKey != null) return _masterKey;
            lock (KeyLock)
            {
                _masterKey ??= LoadOrCreateMasterKey();
            }
            return _masterKey;
        }
    }

    private static byte[] LoadOrCreateMasterKey()
    {
        var path = PathConfig.MasterKeyFile;

        try
        {
            if (File.Exists(path))
            {
                var key = Unprotect(File.ReadAllBytes(path));
                if (key.Length == 32) return key;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to read master key, regenerating: {ex.Message}");
        }

        var fresh = RandomNumberGenerator.GetBytes(32);
        try
        {
            File.WriteAllBytes(path, Protect(fresh));
            RestrictToOwner(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to persist master key: {ex.Message}");
        }
        return fresh;
    }

    private static byte[] Protect(byte[] data)
        => OperatingSystem.IsWindows()
            ? ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser)
            : data;

    private static byte[] Unprotect(byte[] data)
        => OperatingSystem.IsWindows()
            ? ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser)
            : data;

    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to restrict key file permissions: {ex.Message}");
        }
    }

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";

        try
        {
            var plain = Encoding.UTF8.GetBytes(plainText);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var cipher = new byte[plain.Length];
            var tag = new byte[TagSize];

            using (var aes = new AesGcm(MasterKey, TagSize))
            {
                aes.Encrypt(nonce, plain, cipher, tag);
            }

            var payload = new byte[NonceSize + TagSize + cipher.Length];
            Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
            Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
            Buffer.BlockCopy(cipher, 0, payload, NonceSize + TagSize, cipher.Length);

            return V2Prefix + Convert.ToBase64String(payload);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Encryption failed: {ex.Message}");
            return "";
        }
    }

    public static string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return "";

        try
        {
            if (cipherText.StartsWith(V2Prefix, StringComparison.Ordinal))
            {
                var payload = Convert.FromBase64String(cipherText[V2Prefix.Length..]);
                if (payload.Length < NonceSize + TagSize) return "";

                var nonce = payload.AsSpan(0, NonceSize);
                var tag = payload.AsSpan(NonceSize, TagSize);
                var cipher = payload.AsSpan(NonceSize + TagSize);
                var plain = new byte[cipher.Length];

                using var aes = new AesGcm(MasterKey, TagSize);
                aes.Decrypt(nonce, cipher, tag, plain);
                return Encoding.UTF8.GetString(plain);
            }

            return DecryptLegacy(cipherText);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Decryption failed: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// Reads ciphertext written by older builds (AES-CBC with a hard-coded key) so that existing
    /// saved connections keep working. Values are re-encrypted with the new key on next save.
    /// </summary>
    private static string DecryptLegacy(string cipherText)
    {
        var fullCipher = Convert.FromBase64String(cipherText);
        if (fullCipher.Length <= 16) return "";

        using var aes = Aes.Create();
        var keyBytes = new byte[32];
        Array.Copy(LegacyKey, keyBytes, Math.Min(LegacyKey.Length, 32));
        aes.Key = keyBytes;

        var iv = new byte[16];
        Array.Copy(fullCipher, 0, iv, 0, 16);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream(fullCipher, 16, fullCipher.Length - 16);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var sr = new StreamReader(cs);
        return sr.ReadToEnd();
    }
}
