using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using JxTerminal.Models;
using JxTerminal.Services.ImportExport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
public class ImportExportServiceTests
{
    [TestMethod]
    public void ParseOpenSshConfig_ParsesHostsCorrectly()
    {
        var config = @"
# Global comment
Host web-server
    HostName 192.168.1.100
    User ubuntu
    Port 2222
    IdentityFile ~/.ssh/id_rsa

Host db-server
    HostName db.local
    User postgres
    Port 5432
";
        var service = new ImportExportService();
        var results = service.ParseOpenSshConfig(config);

        Assert.AreEqual(2, results.Count);

        var web = results[0];
        Assert.AreEqual("web-server", web.Name);
        Assert.AreEqual("192.168.1.100", web.Host);
        Assert.AreEqual("ubuntu", web.Username);
        Assert.AreEqual(2222, web.Port);
        Assert.AreEqual(SshAuthenticationType.Key, web.AuthenticationType);
        Assert.IsTrue(web.KeyFilePath.Contains(".ssh"));

        var db = results[1];
        Assert.AreEqual("db-server", db.Name);
        Assert.AreEqual("db.local", db.Host);
        Assert.AreEqual("postgres", db.Username);
        Assert.AreEqual(5432, db.Port);
    }

    [TestMethod]
    public async Task ExportAndImportConnections_PreservesStructure()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var groups = new List<ConnectionGroup>
            {
                new ConnectionGroup("Production")
                {
                    Connections =
                    {
                        new SavedConnection { Name = "Prod-1", Host = "10.0.0.1", Username = "root", GroupName = "Production" }
                    }
                }
            };

            var service = new ImportExportService();
            await service.ExportConnectionsAsync(tempFile, groups);

            var imported = await service.ImportConnectionsAsync(tempFile);
            Assert.AreEqual(1, imported.Count);
            Assert.AreEqual("Production", imported[0].Name);
            Assert.AreEqual(1, imported[0].Connections.Count);
            Assert.AreEqual("Prod-1", imported[0].Connections[0].Name);
            Assert.AreEqual("10.0.0.1", imported[0].Connections[0].Host);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
