using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JxTerminal.Models;

namespace JxTerminal.Services.Monitoring;

public class SystemStaticInfo
{
    public string Hostname { get; set; } = string.Empty;
    public string SystemInfo { get; set; } = string.Empty;
    public string CpuModel { get; set; } = string.Empty;
    public int CpuCoreCount { get; set; }
    public int CpuThreadCount { get; set; }
}

public class SystemSnapshotData
{
    public double CpuUsage { get; set; }
    public double CpuTemp { get; set; }
    public double Load1 { get; set; }
    public double Load5 { get; set; }
    public double Load15 { get; set; }

    public double MemTotal { get; set; }
    public double MemUsed { get; set; }
    public double MemFree { get; set; }
    public double MemBuffers { get; set; }
    public double MemCached { get; set; }
    public double SwapTotal { get; set; }
    public double SwapUsed { get; set; }
    public double RamUsage { get; set; }

    public string DiskPath { get; set; } = "/";
    public double DiskUsage { get; set; }
    public double DiskTotal { get; set; }
    public double DiskUsedBytes { get; set; }
    public string DiskReadSpeed { get; set; } = "0 B/s";
    public string DiskWriteSpeed { get; set; } = "0 B/s";
    public long DiskReadIops { get; set; }
    public long DiskWriteIops { get; set; }

    public string NetworkDown { get; set; } = "0 KB/s";
    public string NetworkUp { get; set; } = "0 KB/s";
    public string NetTotalRecv { get; set; } = "0 B";
    public string NetTotalSent { get; set; } = "0 B";
    public long TotalPacketsRecv { get; set; }
    public long TotalPacketsSent { get; set; }
    public int TcpConnections { get; set; }

    public bool GpuDetected { get; set; }
    public string GpuModel { get; set; } = "N/A";
    public double GpuUsage { get; set; }
    public double GpuTemp { get; set; }
    public double GpuPower { get; set; }
    public double GpuMemoryUsed { get; set; }
    public double GpuMemoryTotal { get; set; }
    public int GpuFanSpeed { get; set; }

    public string Uptime { get; set; } = string.Empty;

    public List<ProcessInfo> TopProcesses { get; set; } = new();
    public List<PortInfo> ListeningPorts { get; set; } = new();
}

public interface ISystemMonitorProvider
{
    string PlatformName { get; }
    Task<SystemStaticInfo> ProbeStaticInfoAsync(SshSession session, CancellationToken ct);
    Task<SystemSnapshotData?> SampleMetricsAsync(SshSession session, CancellationToken ct);
}
