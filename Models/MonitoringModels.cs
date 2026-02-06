namespace JxTerminal.Models;

/// <summary>
/// 进程信息（用于显示Top CPU进程）
/// </summary>
public class ProcessInfo
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    public double CpuPercent { get; set; }
    public double MemPercent { get; set; }
    public string User { get; set; } = "";
    
    public string DisplayCpu => $"{CpuPercent:0.0}%";
    public string DisplayMem => $"{MemPercent:0.0}%";
}

/// <summary>
/// 网络端口信息
/// </summary>
public class PortInfo
{
    public int Port { get; set; }
    public string Protocol { get; set; } = "TCP";
    public string State { get; set; } = "LISTEN";
    public string Process { get; set; } = "";
    public string LocalAddress { get; set; } = "";
    
    public string Display => $"{Protocol}:{Port}";
}
