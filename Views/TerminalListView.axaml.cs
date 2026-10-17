using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using JxTerminal.Models;
using JxTerminal.ViewModels;

namespace JxTerminal.Views;

public partial class TerminalListView : UserControl
{
    public TerminalListView()
    {
        InitializeComponent();
    }

    private void SearchBox_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is TextBox tb) tb.Focus();
    }

    private TerminalViewModel? FindViewModel(Control control)
    {
        // Walk up visual tree to find parent with TerminalViewModel DataContext
        foreach (var ancestor in control.GetVisualAncestors())
        {
            if (ancestor is Control c && c.DataContext is TerminalViewModel vm)
                return vm;
        }
        return null;
    }

    private void Tab_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(sender as Control).Properties.IsMiddleButtonPressed) return;

        if (sender is Control { DataContext: TerminalViewModel tab }
            && DataContext is TerminalListViewModel list)
        {
            list.CloseTab(tab);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Finds the terminal control that belongs to the same tab as the given control.
    /// </summary>
    private static JxTerminal.Controls.TerminalControl? FindTerminal(Control control)
    {
        foreach (var ancestor in control.GetVisualAncestors().OfType<Control>())
        {
            var terminal = ancestor.GetVisualDescendants()
                .OfType<JxTerminal.Controls.TerminalControl>()
                .FirstOrDefault();
            if (terminal != null) return terminal;
        }
        return null;
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)    {
        if (sender is not Control control) return;

        switch (e.Key)
        {
            case Key.Enter:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) FindTerminal(control)?.FindPrevious();
                else FindTerminal(control)?.FindNext();
                e.Handled = true;
                break;
            case Key.Escape:
                FindViewModel(control)?.CloseSearchCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void FindNext_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control control) FindTerminal(control)?.FindNext();
    }

    private void FindPrevious_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control control) FindTerminal(control)?.FindPrevious();
    }
}
