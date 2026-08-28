using JxTerminal.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
public class SecurityServiceTests
{
    [TestMethod]
    public void EncryptDecrypt_RoundTrips()
    {
        const string secret = "p@ssw0rd-中文-🔐";
        var cipher = SecurityService.Encrypt(secret);

        Assert.AreNotEqual(secret, cipher);
        Assert.IsTrue(cipher.StartsWith("v2:", System.StringComparison.Ordinal));
        Assert.AreEqual(secret, SecurityService.Decrypt(cipher));
    }

    [TestMethod]
    public void Encrypt_EmptyInput_ReturnsEmpty()
    {
        Assert.AreEqual("", SecurityService.Encrypt(""));
    }

    [TestMethod]
    public void Decrypt_Garbage_ReturnsEmpty()
    {
        Assert.AreEqual("", SecurityService.Decrypt("not-base64-!!!"));
        Assert.AreEqual("", SecurityService.Decrypt("v2:AAAA"));
        Assert.AreEqual("", SecurityService.Decrypt(""));
    }
}
