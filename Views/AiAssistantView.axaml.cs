using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using JxTerminal.ViewModels;
using System.Collections.Specialized;

namespace JxTerminal.Views;

public partial class AiAssistantView : UserControl
{
    public AiAssistantView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_hooked != null)
        {
            _hooked.Messages.CollectionChanged -= OnMessagesChanged;
            _hooked = null;
        }
        if (DataContext is AiAssistantViewModel vm)
        {
            vm.Messages.CollectionChanged += OnMessagesChanged;
            _hooked = vm;
        }
    }

    private AiAssistantViewModel? _hooked;

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Keep the chat pinned to the newest message.
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.UIThread.Post(() => MessagesScroller.ScrollToEnd(), DispatcherPriority.Loaded);
        }
    }

    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is AiAssistantViewModel vm && vm.SendCommand.CanExecute(null))
        {
            vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }
}
