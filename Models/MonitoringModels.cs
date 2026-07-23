using CommunityToolkit.Mvvm.ComponentModel;

namespace JxTerminal.Models;

/// <summary>
/// 进程信息（用于显示Top CPU进程）
/// </summary>
public partial class ProcessInfo : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayCpu))]
    private int _pid;
    
    [ObservableProperty]
    private string _name = "";
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayCpu))]
    private double _cpuPercent;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayMem))]
    private double _memPercent;
    
    [ObservableProperty]
    private string _user = "";
    
    public string DisplayCpu => $"{CpuPercent:0.0}%";
    public string DisplayMem => $"{MemPercent:0.0}%";
    
    /// <summary>
    /// 从另一个 ProcessInfo 就地更新所有属性
    /// </summary>
    public void UpdateFrom(ProcessInfo other)
    {
        Pid = other.Pid;
        Name = other.Name;
        CpuPercent = other.CpuPercent;
        MemPercent = other.MemPercent;
        User = other.User;
    }
}

/// <summary>
/// 网络端口信息
/// </summary>
public partial class PortInfo : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private int _port;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private string _protocol = "TCP";
    
    [ObservableProperty]
    private string _state = "LISTEN";
    
    [ObservableProperty]
    private string _process = "";
    
    [ObservableProperty]
    private string _localAddress = "";
    
    public string Display => $"{Protocol}:{Port}";
    
    /// <summary>
    /// 从另一个 PortInfo 就地更新所有属性
    /// </summary>
    public void UpdateFrom(PortInfo other)
    {
        Port = other.Port;
        Protocol = other.Protocol;
        State = other.State;
        Process = other.Process;
        LocalAddress = other.LocalAddress;
    }
}
