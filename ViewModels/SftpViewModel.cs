using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Renci.SshNet.Sftp;
using JxTerminal.Models;

namespace JxTerminal.ViewModels;

public partial class SftpViewModel : ObservableObject
{
    private readonly SshSession _session;

    [ObservableProperty]
    private string _currentPath = "/";

    public ObservableCollection<ISftpFile> Files { get; } = new();

    public SftpViewModel(SshSession session)
    {
        _session = session;
    }

    [RelayCommand]
    public async Task RefreshFiles()
    {
        if (_session.SftpClient != null && _session.IsConnected)
        {
            await Task.Run(() =>
            {
                try
                {
                    var files = _session.SftpClient.ListDirectory(CurrentPath);
                    var sorted = files.OrderBy(f => !f.IsDirectory).ThenBy(f => f.Name);

                    // Dispatch to UI thread
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        Files.Clear();
                        foreach (var f in sorted)
                        {
                            Files.Add(f);
                        }
                    });
                }
                catch { /* Handle error */ }
            });
        }
    }

    [RelayCommand]
    public void Navigate(ISftpFile file)
    {
        if (file.IsDirectory)
        {
            if (file.Name == "..")
            {
                // Handle parent directory logic
            }
            else
            {
                CurrentPath = file.FullName;
                RefreshFilesCommand.Execute(null);
            }
        }
    }
}
