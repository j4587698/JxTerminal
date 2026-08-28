using JxTerminal.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
public class SnippetItemTests
{
    [TestMethod]
    public void NewSnippet_HasStableIdentity()
    {
        var a = new SnippetItem { Name = "df", Command = "df -h" };
        var b = new SnippetItem { Name = "df", Command = "df -h" };

        Assert.AreNotEqual(a.Id, b.Id);
        Assert.AreEqual("df", a.Name);
        Assert.AreEqual("df -h", a.Command);
    }
}
