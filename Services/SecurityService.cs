using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace JxTerminal.Services;

public static class SecurityService
{
    // WARNING: For development/prototype only. 
    // In production with sync, this should be derived from a user's Master Password using PBKDF2/Argon2.
    // Fixed key ensures data can be recovered after app restart.
    private static readonly byte[] Key = 
        Encoding.UTF8.GetBytes("JxTerminal_Secret_Key_2026_Fixed"); // 32 bytes for AES-256

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";

        try
        {
            using var aes = Aes.Create();
            // Pad key to 32 bytes if needed, strictly speaking AES-256 needs 32 bytes
            var keyBytes = new byte[32];
            Array.Copy(Key, keyBytes, Math.Min(Key.Length, 32));
            aes.Key = keyBytes;
            
            aes.GenerateIV();
            var iv = aes.IV;

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();
            // Prepend IV to the stream so we can use it for decryption
            ms.Write(iv, 0, iv.Length);
            
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs))
            {
                sw.Write(plainText);
            }

            return Convert.ToBase64String(ms.ToArray());
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
            var fullCipher = Convert.FromBase64String(cipherText);
            
            using var aes = Aes.Create();
            var keyBytes = new byte[32];
            Array.Copy(Key, keyBytes, Math.Min(Key.Length, 32));
            aes.Key = keyBytes;

            // Extract IV (first 16 bytes)
            var iv = new byte[16];
            if (fullCipher.Length < 16) return ""; // Invalid data
            Array.Copy(fullCipher, 0, iv, 0, 16);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(fullCipher, 16, fullCipher.Length - 16);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);
            
            return sr.ReadToEnd();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Decryption failed: {ex.Message}");
            return "";
        }
    }
}
