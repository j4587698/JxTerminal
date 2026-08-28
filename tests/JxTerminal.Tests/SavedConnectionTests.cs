using JxTerminal.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
public class SavedConnectionTests
{
    [TestMethod]
    public void DisplayTarget_Ssh_IncludesUserAndHost()
    {
        var c = new SavedConnection { Host = "10.0.0.5", Port = 22, Username = "root" };
        Assert.AreEqual("root@10.0.0.5:22", c.DisplayTarget);
    }

    [TestMethod]
    public void DisplayTarget_Serial_UsesClassicNotation()
    {
        var c = new SavedConnection
        {
            Type = ConnectionType.Serial,
            SerialPortName = "COM3",
            BaudRate = 9600
        };
        Assert.AreEqual("COM3 @ 9600 8N1", c.DisplayTarget);
    }

    [TestMethod]
    public void Clone_CopiesAllFields()
    {
        var c = new SavedConnection
        {
            Name = "web1",
            Host = "web1.local",
            Username = "deploy",
            AuthenticationType = SshAuthenticationType.Key,
            KeyFilePath = "/home/me/id_ed25519"
        };

        var clone = c.Clone();

        Assert.AreEqual(c.Name + " (Copy)", clone.Name);
        Assert.AreEqual(c.Host, clone.Host);
        Assert.AreEqual(c.Username, clone.Username);
        Assert.AreEqual(c.AuthenticationType, clone.AuthenticationType);
        Assert.AreEqual(c.KeyFilePath, clone.KeyFilePath);
        Assert.AreNotEqual(c.Id, clone.Id);
    }
}
