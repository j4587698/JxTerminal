using CommunityToolkit.Mvvm.ComponentModel;
using JxTerminal.Models;

namespace JxTerminal.ViewModels;

public partial class TerminalViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title;

    public ITerminalSession Session { get; }
    public TerminalSettings Settings { get; }

    public TerminalViewModel(ITerminalSession session, TerminalSettings settings)
    {
        Session = session;
        Settings = settings;
        if (session is SshSession ssh)
            Title = ssh.Host;
        else
            Title = "Local Shell";

        Session.Disconnected += () => Title += " (Disconnected)";
    }
}
