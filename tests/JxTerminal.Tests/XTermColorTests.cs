using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTerm;
using XTerm.Options;
using XTerm.Common;

namespace JxTerminal.Tests;

[TestClass]
public class XTermColorTests
{
    [TestMethod]
    public void TestXTermColors()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 80, Rows = 24 });
        terminal.Write("\x1b[31mRed\x1b[32mGreen\x1b[38;5;196m256C\x1b[38;2;100;150;200mTrueColor\x1b[41mBgRed\x1b[0mNormal");
        
        var line = terminal.Buffer.Lines[0] as XTerm.Buffer.BufferLine;
        Assert.IsNotNull(line);

        for (int i = 0; i < 35; i++)
        {
            var cell = line[i];
            if (cell.Content == null) continue;
            var fg = cell.Attributes.GetFgColor();
            var fgMode = cell.Attributes.GetFgColorMode();
            var bg = cell.Attributes.GetBgColor();
            var bgMode = cell.Attributes.GetBgColorMode();
            Console.WriteLine($"Char '{cell.Content}' [{i}]: Fg={fg} (Mode={fgMode}), Bg={bg} (Mode={bgMode})");
        }
        
        foreach (var name in Enum.GetNames(typeof(ColorMode)))
        {
            Console.WriteLine($"ColorMode.{name} = {(int)Enum.Parse(typeof(ColorMode), name)}");
        }
    }
}
