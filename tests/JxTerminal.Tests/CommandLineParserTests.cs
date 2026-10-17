using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Threading.Tasks;
using JxTerminal.Models;
using JxTerminal.Services.CommandLine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
public class CommandLineParserTests
{
    [TestMethod]
    public void Parse_EmptyArgs_ReturnsNoConnection()
    {
        var options = CommandLineParser.Parse(null);
        Assert.IsFalse(options.HasConnection);

        options = CommandLineParser.Parse([]);
        Assert.IsFalse(options.HasConnection);
    }

    [TestMethod]
    public void Parse_ClassicPutty_SshWithAllFlags()
    {
        string[] args = ["-ssh", "-l", "root", "-pw", "Secret123!", "192.168.1.100", "-P", "2222"];
        var options = CommandLineParser.Parse(args);

        Assert.IsTrue(options.HasConnection);
        Assert.IsNotNull(options.TargetConnection);
        Assert.AreEqual(ConnectionType.Ssh, options.TargetConnection.Type);
        Assert.AreEqual("root", options.TargetConnection.Username);
        Assert.AreEqual("Secret123!", options.TargetConnection.Password);
        Assert.AreEqual("192.168.1.100", options.TargetConnection.Host);
        Assert.AreEqual(2222, options.TargetConnection.Port);
        Assert.AreEqual(SshAuthenticationType.Password, options.TargetConnection.AuthenticationType);
    }

    [TestMethod]
    public void Parse_PositionalUserHostPort()
    {
        string[] args = ["admin@10.0.0.5", "2200"];
        var options = CommandLineParser.Parse(args);

        Assert.IsTrue(options.HasConnection);
        Assert.IsNotNull(options.TargetConnection);
        Assert.AreEqual(ConnectionType.Ssh, options.TargetConnection.Type);
        Assert.AreEqual("admin", options.TargetConnection.Username);
        Assert.AreEqual("10.0.0.5", options.TargetConnection.Host);
        Assert.AreEqual(2200, options.TargetConnection.Port);
        Assert.AreEqual(SshAuthenticationType.Ask, options.TargetConnection.AuthenticationType);
    }

    [TestMethod]
    public void Parse_HostColonPort()
    {
        string[] args = ["10.0.0.8:8022"];
        var options = CommandLineParser.Parse(args);

        Assert.IsTrue(options.HasConnection);
        Assert.IsNotNull(options.TargetConnection);
        Assert.AreEqual("10.0.0.8", options.TargetConnection.Host);
        Assert.AreEqual(8022, options.TargetConnection.Port);
    }

    [TestMethod]
    public void Parse_KeyFile_SetsKeyAuthentication()
    {
        string[] args = ["-ssh", "-i", @"C:\keys\prod.ppk", "-l", "deploy", "10.0.0.1"];
        var options = CommandLineParser.Parse(args);

        Assert.IsTrue(options.HasConnection);
        Assert.IsNotNull(options.TargetConnection);
        Assert.AreEqual(SshAuthenticationType.Key, options.TargetConnection.AuthenticationType);
        Assert.AreEqual(@"C:\keys\prod.ppk", options.TargetConnection.KeyFilePath);
        Assert.AreEqual("deploy", options.TargetConnection.Username);
        Assert.AreEqual("10.0.0.1", options.TargetConnection.Host);
        Assert.AreEqual(22, options.TargetConnection.Port);
    }

    [TestMethod]
    public void Parse_Telnet()
    {
        string[] args = ["-telnet", "router.internal", "-P", "2323"];
        var options = CommandLineParser.Parse(args);

        Assert.IsTrue(options.HasConnection);
        Assert.IsNotNull(options.TargetConnection);
        Assert.AreEqual(ConnectionType.Telnet, options.TargetConnection.Type);
        Assert.AreEqual("router.internal", options.TargetConnection.Host);
        Assert.AreEqual(2323, options.TargetConnection.Port);
    }

    [TestMethod]
    public void Parse_Serial_WithSercfg()
    {
        string[] args = ["-serial", "COM3", "-sercfg", "115200,8,n,1,r"];
        var options = CommandLineParser.Parse(args);

        Assert.IsTrue(options.HasConnection);
        Assert.IsNotNull(options.TargetConnection);
        Assert.AreEqual(ConnectionType.Serial, options.TargetConnection.Type);
        Assert.AreEqual("COM3", options.TargetConnection.SerialPortName);
        Assert.AreEqual(115200, options.TargetConnection.BaudRate);
        Assert.AreEqual(8, options.TargetConnection.DataBits);
        Assert.AreEqual(Parity.None, options.TargetConnection.Parity);
        Assert.AreEqual(StopBits.One, options.TargetConnection.StopBits);
        Assert.AreEqual(Handshake.RequestToSend, options.TargetConnection.Handshake);
    }

    [TestMethod]
    public void Parse_LoadSession_DirectAndShorthand()
    {
        var opt1 = CommandLineParser.Parse(["-load", "BastionServer"]);
        Assert.IsTrue(opt1.HasConnection);
        Assert.AreEqual("BastionServer", opt1.SessionName);

        var opt2 = CommandLineParser.Parse(["@BastionServer"]);
        Assert.IsTrue(opt2.HasConnection);
        Assert.AreEqual("BastionServer", opt2.SessionName);
    }

    [TestMethod]
    public void ResolveConnection_FindsExistingSession_AndAppliesOverrides()
    {
        var group = new ConnectionGroup("Servers");
        group.Connections.Add(new SavedConnection
        {
            Name = "Production-Bastion",
            Host = "bastion.example.com",
            Port = 22,
            Username = "default_user",
            Password = "old_password"
        });

        var options = CommandLineParser.Parse(["-load", "Production-Bastion", "-l", "operator", "-pw", "NewSecret!"]);
        var resolved = CommandLineParser.ResolveConnection(options, [group]);

        Assert.IsNotNull(resolved);
        Assert.AreEqual("bastion.example.com", resolved.Host);
        Assert.AreEqual("operator", resolved.Username); // Overridden
        Assert.AreEqual("NewSecret!", resolved.Password); // Overridden
    }

    [TestMethod]
    public void Parse_OpenSshCommandSyntax()
    {
        string[] args = ["ssh", "root@172.16.0.5", "-p", "2222", "-i", "/path/to/key"];
        var options = CommandLineParser.Parse(args);

        Assert.IsTrue(options.HasConnection);
        Assert.IsNotNull(options.TargetConnection);
        Assert.AreEqual(ConnectionType.Ssh, options.TargetConnection.Type);
        Assert.AreEqual("root", options.TargetConnection.Username);
        Assert.AreEqual("172.16.0.5", options.TargetConnection.Host);
        Assert.AreEqual(2222, options.TargetConnection.Port);
        Assert.AreEqual(SshAuthenticationType.Key, options.TargetConnection.AuthenticationType);
        Assert.AreEqual("/path/to/key", options.TargetConnection.KeyFilePath);
    }

    [TestMethod]
    public void Parse_WindowReuseFlags()
    {
        var opt1 = CommandLineParser.Parse(["-ssh", "host", "--new-window"]);
        Assert.IsTrue(opt1.ForceNewWindow);
        Assert.IsFalse(opt1.ForceReuseWindow);

        var opt2 = CommandLineParser.Parse(["-ssh", "host", "--reuse-window"]);
        Assert.IsFalse(opt2.ForceNewWindow);
        Assert.IsTrue(opt2.ForceReuseWindow);
    }

    [TestMethod]
    public async Task SingleInstance_PipeRoundTrip_TransmitsArgumentsAccurately()
    {
        string[] received = [];
        var tcs = new System.Threading.Tasks.TaskCompletionSource<string[]>();

        SingleInstanceService.StartServer(args =>
        {
            tcs.TrySetResult(args);
        });

        try
        {
            string[] testArgs = ["-ssh", "-l", "admin", "192.168.1.50", "-P", "2222", "-m", "echo 'hello world'"];
            bool sent = await SingleInstanceService.TrySendArgsToExistingInstanceAsync(testArgs, timeoutMs: 3000);

            Assert.IsTrue(sent, "TrySendArgsToExistingInstanceAsync should return true when server is listening");

            var completedTask = await System.Threading.Tasks.Task.WhenAny(tcs.Task, System.Threading.Tasks.Task.Delay(3000));
            Assert.AreEqual(tcs.Task, completedTask, "Server should have received arguments before timeout");

            var res = await tcs.Task;
            CollectionAssert.AreEqual(testArgs, res);
        }
        finally
        {
            SingleInstanceService.StopServer();
        }
    }
}

