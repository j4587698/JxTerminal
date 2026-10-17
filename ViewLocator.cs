using Avalonia.Controls;
using Avalonia.Controls.Templates;
using JxTerminal.ViewModels;
using JxTerminal.Views;

namespace JxTerminal;

/// <summary>
/// AOT-safe strongly-typed view locator that maps view models to views without reflection.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        return param switch
        {
            HostListViewModel vm => new HostListView { DataContext = vm },
            TerminalListViewModel vm => new TerminalListView { DataContext = vm },
            SettingsViewModel vm => new SettingsView { DataContext = vm },
            SnippetsViewModel vm => new SnippetsView { DataContext = vm },
            SnippetEditViewModel vm => new SnippetEditView { DataContext = vm },
            AddConnectionViewModel vm => new AddConnectionView { DataContext = vm },
            AddGroupViewModel vm => new AddGroupView { DataContext = vm },
            ChangePasswordViewModel vm => new ChangePasswordDialog { DataContext = vm },
            FontPickerViewModel vm => new FontPickerView { DataContext = vm },
            KeyManagerViewModel vm => new KeyManagerView { DataContext = vm },
            PlaceholderViewModel vm => new PlaceholderView { DataContext = vm },
            TransferCenterViewModel vm => new TransferCenterView { DataContext = vm },
            SystemMonitorViewModel vm => new SystemMonitorSidebarView { DataContext = vm },
            AiAssistantViewModel vm => new AiAssistantView { DataContext = vm },
            _ => param is null ? null : new TextBlock { Text = "Not Found: " + param.GetType().FullName }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
