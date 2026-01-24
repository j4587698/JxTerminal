using System.Collections.ObjectModel;

namespace JxTerminal.Models;

public class SessionManager
{
    private static SessionManager? _instance;
    public static SessionManager Instance => _instance ??= new SessionManager();

    public ObservableCollection<SshSession> Sessions { get; } = new();

    private SessionManager() { }

    public void AddSession(SshSession session)
    {
        Sessions.Add(session);
    }

    public void RemoveSession(SshSession session)
    {
        if (Sessions.Contains(session))
        {
            session.Dispose();
            Sessions.Remove(session);
        }
    }
}
