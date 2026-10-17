using JxTerminal.Models;
using JxTerminal.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
public class KnownHostsServiceTests
{
    [TestMethod]
    public void Trust_And_GetFingerprint_WorksCorrectly()
    {
        var host = "test-host.local";
        var port = 2222;
        var fingerprint = "SHA256:abc123xyz456fakefingerprint";

        KnownHostsService.Trust(host, port, fingerprint);

        var retrieved = KnownHostsService.GetFingerprint(host, port);
        Assert.AreEqual(fingerprint, retrieved);
    }

    [TestMethod]
    public void GetFingerprint_UnknownHost_ReturnsNull()
    {
        var host = "non-existent-host-99999.local";
        var port = 22;

        var retrieved = KnownHostsService.GetFingerprint(host, port);
        Assert.IsNull(retrieved);
    }

    [TestMethod]
    public void SshSession_Constructs_WithExpectedDefaults()
    {
        var session = new SshSession("127.0.0.1", 22, "root", "password");
        Assert.AreEqual("127.0.0.1", session.Host);
        Assert.AreEqual(22, session.Port);
        Assert.AreEqual("root", session.Username);
        Assert.IsFalse(session.IsConnected);
        Assert.IsFalse(session.HostKeyRejected);
        Assert.IsNull(session.ShellStream);
        Assert.IsNull(session.SftpClient);
    }

    [TestMethod]
    public async System.Threading.Tasks.Task SshSession_HostKeyVerifier_CanBeAssignedAndInvoked()
    {
        var session = new SshSession("127.0.0.1", 22, "root", "password");
        bool invoked = false;
        session.HostKeyVerifier = (display, received, known) =>
        {
            invoked = true;
            return System.Threading.Tasks.Task.FromResult(true);
        };

        Assert.IsNotNull(session.HostKeyVerifier);
        var result = await session.HostKeyVerifier("127.0.0.1:22", "fingerprint1", null);
        Assert.IsTrue(invoked);
        Assert.IsTrue(result);
    }
}
