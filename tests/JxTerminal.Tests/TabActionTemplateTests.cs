using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
public class TabActionTemplateTests
{
    private static string FindRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "JxTerminal.csproj")))
                return dir;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        throw new DirectoryNotFoundException("Could not find repository root containing JxTerminal.csproj");
    }

    [TestMethod]
    public void AppStyles_TabAction_HasCustomControlTemplateWithoutLuminaPresenter()
    {
        var repoRoot = FindRepoRoot();
        var axamlPath = Path.Combine(repoRoot, "Styles", "AppStyles.axaml");
        Assert.IsTrue(File.Exists(axamlPath), "AppStyles.axaml must exist");

        var doc = XDocument.Load(axamlPath);
        var styles = doc.Descendants().Where(e => e.Name.LocalName == "Style" &&
            (string?)e.Attribute("Selector") == "Button.TabAction").ToList();

        Assert.IsTrue(styles.Any(), "Style with Selector='Button.TabAction' must exist");

        var templateSetter = styles
            .SelectMany(s => s.Elements().Where(e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == "Template"))
            .FirstOrDefault();

        Assert.IsNotNull(templateSetter, "Button.TabAction must have a Setter for Property='Template'");

        var controlTemplate = templateSetter.Elements().FirstOrDefault(e => e.Name.LocalName == "ControlTemplate");
        Assert.IsNotNull(controlTemplate, "Setter must define a ControlTemplate");

        // Root of the ControlTemplate must be Border Name="PART_Border"
        // Border provides solid full-bounds hit-testing across all empty space when Background is set.
        var rootBorder = controlTemplate.Elements().FirstOrDefault(e => e.Name.LocalName == "Border" && (string?)e.Attribute("Name") == "PART_Border");
        Assert.IsNotNull(rootBorder, "ControlTemplate must have a root Border Name='PART_Border' to span the full bounds with guaranteed hit testing");

        // Border must have Background="{TemplateBinding Background}"
        var bgAttr = (string?)rootBorder.Attribute("Background");
        Assert.AreEqual("{TemplateBinding Background}", bgAttr, "PART_Border must bind Background to {TemplateBinding Background}");

        // Inside Border must be ContentPresenter Name="PART_ContentPresenter"
        var innerPresenter = rootBorder.Elements().FirstOrDefault(e => e.Name.LocalName == "ContentPresenter" && (string?)e.Attribute("Name") == "PART_ContentPresenter");
        Assert.IsNotNull(innerPresenter, "Border must contain ContentPresenter Name='PART_ContentPresenter'");

        // LuminaButtonPresenter (which causes dead zones on transparent buttons) must NOT be present
        var hasLuminaPresenter = controlTemplate.Descendants().Any(e => e.Name.LocalName.Contains("LuminaButtonPresenter"));
        Assert.IsFalse(hasLuminaPresenter, "ControlTemplate must not contain LuminaButtonPresenter");

        // Hover & Pressed styles must target /template/ Border#PART_Border
        var hoverStyle = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Style" &&
            ((string?)e.Attribute("Selector"))?.Contains("Button.TabAction:pointerover /template/ Border#PART_Border") == true);
        Assert.IsNotNull(hoverStyle, "Pointerover style must target /template/ Border#PART_Border");

        var pressedStyle = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Style" &&
            ((string?)e.Attribute("Selector"))?.Contains("Button.TabAction:pressed /template/ Border#PART_Border") == true);
        Assert.IsNotNull(pressedStyle, "Pressed style must target /template/ Border#PART_Border");
    }

    [TestMethod]
    public void AppStyles_TabClose_HasCustomControlTemplate()
    {
        var repoRoot = FindRepoRoot();
        var axamlPath = Path.Combine(repoRoot, "Styles", "AppStyles.axaml");
        var doc = XDocument.Load(axamlPath);

        var styles = doc.Descendants().Where(e => e.Name.LocalName == "Style" &&
            (string?)e.Attribute("Selector") == "Button.TabClose").ToList();

        Assert.IsTrue(styles.Any(), "Style with Selector='Button.TabClose' must exist");

        var templateSetter = styles
            .SelectMany(s => s.Elements().Where(e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == "Template"))
            .FirstOrDefault();

        Assert.IsNotNull(templateSetter, "Button.TabClose must define a custom Template");

        var controlTemplate = templateSetter.Elements().FirstOrDefault(e => e.Name.LocalName == "ControlTemplate");
        Assert.IsNotNull(controlTemplate);

        var rootBorder = controlTemplate.Elements().FirstOrDefault(e => e.Name.LocalName == "Border" && (string?)e.Attribute("Name") == "PART_Border");
        Assert.IsNotNull(rootBorder, "Button.TabClose must have a root Border Name='PART_Border'");

        var innerPresenter = rootBorder.Elements().FirstOrDefault(e => e.Name.LocalName == "ContentPresenter" && (string?)e.Attribute("Name") == "PART_ContentPresenter");
        Assert.IsNotNull(innerPresenter, "Border must contain ContentPresenter Name='PART_ContentPresenter'");
    }

    [TestMethod]
    public void AppStyles_IconOnly_HasCustomControlTemplate()
    {
        var repoRoot = FindRepoRoot();
        var axamlPath = Path.Combine(repoRoot, "Styles", "AppStyles.axaml");
        var doc = XDocument.Load(axamlPath);

        var styles = doc.Descendants().Where(e => e.Name.LocalName == "Style" &&
            ((string?)e.Attribute("Selector"))?.Contains("IconOnly") == true &&
            ((string?)e.Attribute("Selector"))?.Contains("Button") == true).ToList();

        Assert.IsTrue(styles.Any(), "Style for IconOnly button must exist");

        var templateSetter = styles
            .SelectMany(s => s.Elements().Where(e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == "Template"))
            .FirstOrDefault();

        Assert.IsNotNull(templateSetter, "Button.IconOnly must define a custom Template");

        var controlTemplate = templateSetter.Elements().FirstOrDefault(e => e.Name.LocalName == "ControlTemplate");
        Assert.IsNotNull(controlTemplate);

        var rootBorder = controlTemplate.Elements().FirstOrDefault(e => e.Name.LocalName == "Border" && (string?)e.Attribute("Name") == "PART_Border");
        Assert.IsNotNull(rootBorder, "Button.IconOnly must have a root Border Name='PART_Border'");

        var innerPresenter = rootBorder.Elements().FirstOrDefault(e => e.Name.LocalName == "ContentPresenter" && (string?)e.Attribute("Name") == "PART_ContentPresenter");
        Assert.IsNotNull(innerPresenter, "Border must contain ContentPresenter Name='PART_ContentPresenter'");
    }

    [TestMethod]
    public void TerminalListView_TabActionButtons_DoNotHaveIsHitTestVisibleFalse()
    {
        var repoRoot = FindRepoRoot();
        var axamlPath = Path.Combine(repoRoot, "Views", "TerminalListView.axaml");
        var doc = XDocument.Load(axamlPath);

        // Find TabAction buttons
        var tabActionButtons = doc.Descendants()
            .Where(e => e.Name.LocalName == "Button" && ((string?)e.Attribute("Classes"))?.Contains("TabAction") == true)
            .ToList();

        Assert.AreEqual(3, tabActionButtons.Count, "Must find exactly 3 TabAction buttons (NewTab, ToggleAi, ToggleSftp)");

        foreach (var btn in tabActionButtons)
        {
            // Verify no children or panels have IsHitTestVisible="False"
            var falseHitTestElements = btn.Descendants()
                .Where(e => (string?)e.Attribute("IsHitTestVisible") == "False")
                .ToList();

            Assert.AreEqual(0, falseHitTestElements.Count,
                $"TabAction button ({btn.Attribute("ToolTip.Tip")}) must not have elements with IsHitTestVisible='False'");
        }

        // Verify SFTP button binds to IsSftpVisible (not IsAiVisible typo)
        var sftpBtn = tabActionButtons.FirstOrDefault(b => ((string?)b.Attribute("Command"))?.Contains("ToggleSftp") == true);
        Assert.IsNotNull(sftpBtn, "ToggleSftp button must exist");

        var sftpXElements = sftpBtn.Descendants().ToList();
        var bindsSftp = sftpXElements.Any(e => ((string?)e.Attribute("IsVisible"))?.Contains("IsSftpVisible") == true);
        Assert.IsTrue(bindsSftp, "SFTP button must bind its visibility to SelectedTab.IsSftpVisible");

        var accidentalAiBind = sftpXElements.Any(e => ((string?)e.Attribute("IsVisible"))?.Contains("IsAiVisible") == true);
        Assert.IsFalse(accidentalAiBind, "SFTP button must not bind to IsAiVisible");
    }

    [TestMethod]
    public void TabActionButtons_ViewModelCommands_ExecuteCorrectly()
    {
        var vm = new ViewModels.MainWindowViewModel();
        vm.NavigateToTerminal();

        // Create a terminal tab
        var session = new Models.PtyLocalShellSession("Auto");
        var termVm = new ViewModels.TerminalViewModel(session, new Models.TerminalSettings(), new Models.SavedConnection { Name = "TestTab" });
        vm.TerminalList.Tabs.Add(termVm);

        // Test NewTab button command execution
        bool requestCreateFired = false;
        vm.TerminalList.RequestCreateConnection += () => { requestCreateFired = true; };
        vm.TerminalList.CreateConnectionCommand.Execute(null);
        Assert.IsTrue(requestCreateFired, "CreateConnectionCommand must trigger RequestCreateConnection");

        // Test AI button command execution
        Assert.IsFalse(termVm.IsAiVisible, "Initially AI panel is hidden");
        vm.TerminalList.ToggleAiCommand.Execute(null);
        Assert.IsTrue(termVm.IsAiVisible, "ToggleAiCommand must toggle IsAiVisible to true");
        vm.TerminalList.ToggleAiCommand.Execute(null);
        Assert.IsFalse(termVm.IsAiVisible, "ToggleAiCommand again must toggle IsAiVisible to false");

        // Test SFTP button command execution
        Assert.IsFalse(termVm.IsSftpVisible, "Initially SFTP sidebar is hidden");
        vm.TerminalList.ToggleSftpCommand.Execute(null);
        Assert.IsTrue(termVm.IsSftpVisible, "ToggleSftpCommand must toggle IsSftpVisible to true");
        vm.TerminalList.ToggleSftpCommand.Execute(null);
        Assert.IsFalse(termVm.IsSftpVisible, "ToggleSftpCommand again must toggle IsSftpVisible to false");

        // Test mutual exclusivity: opening AI closes SFTP
        vm.TerminalList.ToggleSftpCommand.Execute(null);
        Assert.IsTrue(termVm.IsSftpVisible);
        vm.TerminalList.ToggleAiCommand.Execute(null);
        Assert.IsTrue(termVm.IsAiVisible);
        Assert.IsFalse(termVm.IsSftpVisible, "Opening AI must close SFTP sidebar");
    }
}
