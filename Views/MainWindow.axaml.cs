using Avalonia.Controls;
using JxTerminal.ViewModels;
using SukiUI.Controls;

namespace JxTerminal.Views;

public partial class MainWindow : SukiWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is MenuItemViewModel item)
        {
            item.Command?.Execute(null);
        }
    }
}