using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace JxTerminal.Views;

public partial class AddGroupView : UserControl
{
    public AddGroupView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
