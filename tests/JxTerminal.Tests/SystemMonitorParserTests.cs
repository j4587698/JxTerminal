using System;
using System.Linq;
using JxTerminal.Services.Monitoring;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
public class SystemMonitorParserTests
{
    [TestMethod]
    public void FormatSpeed_FormatsCorrectUnits()
    {
        Assert.AreEqual("500 B/s", LinuxProcMonitorProvider.FormatSpeed(500));
        Assert.AreEqual("1.5 KB/s", LinuxProcMonitorProvider.FormatSpeed(1536));
        Assert.AreEqual("2.0 MB/s", LinuxProcMonitorProvider.FormatSpeed(2 * 1024 * 1024));
        Assert.AreEqual("1.0 GB/s", LinuxProcMonitorProvider.FormatSpeed(1024L * 1024 * 1024));
    }

    [TestMethod]
    public void LinuxMonitorProvider_ProcessesDynamicSnapshot()
    {
        var provider = new LinuxProcMonitorProvider();

        var dynamicOutput = @">>>STAT
cpu  1000 200 300 5000 100 0 50 0 0 0
>>>UPTIME
 12:34:56 up 10 days,  2:30,  1 user,  load average: 0.45, 0.52, 0.60
>>>MEM
MemTotal:       16384000 kB
MemFree:         4096000 kB
MemAvailable:    8192000 kB
Cached:          2048000 kB
SwapTotal:       2048000 kB
SwapFree:        1024000 kB
>>>PROC
root 1 0.1 0.2 /sbin/init
ubuntu 1234 15.4 4.5 /usr/bin/node server.js
>>>PORTS
LISTEN 0 128 0.0.0.0:22 0.0.0.0:* users:((""sshd"",pid=800,fd=3))
LISTEN 0 511 *:80 *:* users:((""nginx"",pid=900,fd=6))
>>>NET
eth0: 1000000 500 0 0 0 0 0 0 2000000 600 0 0 0 0 0 0
>>>DISKIO
sda 100 50 2000 500 200 100 4000 600 0 1000 1100
";

        var snapshot = provider.ParseOutput(dynamicOutput, DateTime.Now);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(0.45, snapshot.Load1);
        Assert.AreEqual(0.52, snapshot.Load5);
        Assert.AreEqual(0.60, snapshot.Load15);
        Assert.IsTrue(snapshot.MemTotal > 0);
        Assert.IsTrue(snapshot.MemUsed > 0);

        Assert.AreEqual(2, snapshot.TopProcesses.Count);
        Assert.AreEqual("init", snapshot.TopProcesses[0].Name);
        Assert.AreEqual("node", snapshot.TopProcesses[1].Name);
        Assert.AreEqual("15.4%", snapshot.TopProcesses[1].DisplayCpu);

        Assert.IsTrue(snapshot.ListeningPorts.Count > 0);
        Assert.IsTrue(snapshot.ListeningPorts.Any(p => p.Display.Contains("22")));
        Assert.IsTrue(snapshot.ListeningPorts.Any(p => p.Display.Contains("80")));
    }

    [TestMethod]
    public void LinuxMonitorProvider_ProcessesTopOutputFormat()
    {
        var provider = new LinuxProcMonitorProvider();

        var dynamicOutput = @">>>PROC
 1234 root      20   0  718104  45124  28116 S   8.5   2.2   0:14.21 dockerd
 5678 www-data  20   0  350000  12000   8000 S   3.2   1.1   0:05.10 nginx
";

        var snapshot = provider.ParseOutput(dynamicOutput, DateTime.Now);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(2, snapshot.TopProcesses.Count);
        Assert.AreEqual("dockerd", snapshot.TopProcesses[0].Name);
        Assert.AreEqual("root", snapshot.TopProcesses[0].User);
        Assert.AreEqual(8.5, snapshot.TopProcesses[0].CpuPercent);
        Assert.AreEqual("nginx", snapshot.TopProcesses[1].Name);
        Assert.AreEqual(3.2, snapshot.TopProcesses[1].CpuPercent);
    }

    [TestMethod]
    public void LinuxMonitorProvider_ProcessesDiskB_Directly()
    {
        var provider = new LinuxProcMonitorProvider();

        var output = @">>>DISK_B
/dev/sda1 107374182400 42949672960 64424509440 40% /
";
        var snapshot = provider.ParseOutput(output, DateTime.Now);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(100.0, snapshot.DiskTotal, 0.01);
        Assert.AreEqual(40.0, snapshot.DiskUsedBytes, 0.01);
        Assert.AreEqual(40.0, snapshot.DiskUsage, 0.01);
        Assert.AreEqual("/", snapshot.DiskPath);
    }

    [TestMethod]
    public void LinuxMonitorProvider_TopProcessesMultiCoreIrixMode_PreservesRealtimeValues()
    {
        var provider = new LinuxProcMonitorProvider
        {
            CoreCount = 16 // 16-core system
        };

        var dynamicOutput = @">>>PROC
    PID USER      PR  NI    VIRT    RES    SHR S  %CPU  %MEM     TIME+ COMMAND
  10234 appuser   20   0 1524300 325400  54300 R 145.2  12.5   2:15.30 /usr/bin/python3 app.py
  10567 www-data  20   0  350000  12000   8000 S  18,5   3,2   0:05.10 /usr/sbin/nginx -g daemon off;
    999 root      20   0   12000   1500   1000 S   5.0   0.1   0:00.12 (sd-pam)
    100 root      20   0       0      0      0 I   0.0   0.0   0:00.00 [kworker/0:0]
  99999 root      20   0   15000   2000   1200 R   2.5   0.1   0:00.01 top -b -n 2
";

        var snapshot = provider.ParseOutput(dynamicOutput, DateTime.Now);

        Assert.IsNotNull(snapshot);
        // Header line (PID USER...), kernel thread ([kworker...]), and monitor command (top) must be filtered out
        Assert.AreEqual(3, snapshot.TopProcesses.Count);

        // 1. Multithreaded real-time spike: 145.2% CPU in Irix mode must not be divided down to 9.0%
        Assert.AreEqual("python3", snapshot.TopProcesses[0].Name);
        Assert.AreEqual("appuser", snapshot.TopProcesses[0].User);
        Assert.AreEqual(145.2, snapshot.TopProcesses[0].CpuPercent);
        Assert.AreEqual("145.2%", snapshot.TopProcesses[0].DisplayCpu);
        Assert.AreEqual(12.5, snapshot.TopProcesses[0].MemPercent);

        // 2. Comma decimal separator (18,5% and 3,2%) parsed correctly regardless of host locale
        Assert.AreEqual("nginx", snapshot.TopProcesses[1].Name);
        Assert.AreEqual(18.5, snapshot.TopProcesses[1].CpuPercent);
        Assert.AreEqual(3.2, snapshot.TopProcesses[1].MemPercent);

        // 3. Parentheses stripped: (sd-pam) -> sd-pam
        Assert.AreEqual("sd-pam", snapshot.TopProcesses[2].Name);
        Assert.AreEqual(5.0, snapshot.TopProcesses[2].CpuPercent);
    }

    [TestMethod]
    public void LinuxMonitorProvider_LongProcessName_And_TrailingPlus_Cleaned()
    {
        var provider = new LinuxProcMonitorProvider();

        var dynamicOutput = @">>>PROC
    PID USER      PR  NI    VIRT    RES    SHR S  %CPU  %MEM     TIME+ COMMAND
    101 systemd+  20   0   24000   4000   3000 S   1.2   0.2   0:01.00 /lib/systemd/systemd-resolved
    102 root      20   0   50000   8000   4000 S   0.8   0.5   0:00.50 unattended-upg+
";

        var snapshot = provider.ParseOutput(dynamicOutput, DateTime.Now);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(2, snapshot.TopProcesses.Count);

        // Full un-truncated command from top -c -w 512
        Assert.AreEqual("systemd-resolved", snapshot.TopProcesses[0].Name);

        // Fallback truncated command with trailing + cleaned
        Assert.AreEqual("unattended-upg", snapshot.TopProcesses[1].Name);
    }

    [TestMethod]
    public void LinuxMonitorProvider_ListeningPorts_ExtractsCleanProcessNames()
    {
        var provider = new LinuxProcMonitorProvider();

        var dynamicOutput = @">>>PORTS
LISTEN 0 128 0.0.0.0:22 0.0.0.0:* users:((""sshd"",pid=800,fd=3))
LISTEN 0 511 *:80 *:* users:((""nginx"",pid=901,fd=6),(""nginx"",pid=900,fd=6))
LISTEN 0 4096 127.0.0.1:6379 0.0.0.0:* users:((""/usr/bin/redis-server"",pid=555,fd=4))
tcp 0 0 0.0.0.0:3306 0.0.0.0:* LISTEN 1234/mysqld
LISTEN 0 128 *:53 *:* -
";

        var snapshot = provider.ParseOutput(dynamicOutput, DateTime.Now);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(5, snapshot.ListeningPorts.Count);

        // 1. ss single: users:(("sshd",pid=800,fd=3)) -> sshd
        Assert.AreEqual("sshd", snapshot.ListeningPorts[0].Process);
        Assert.AreEqual(22, snapshot.ListeningPorts[0].Port);

        // 2. ss multiple: users:(("nginx",...)) -> nginx
        Assert.AreEqual("nginx", snapshot.ListeningPorts[1].Process);
        Assert.AreEqual(80, snapshot.ListeningPorts[1].Port);

        // 3. ss with path: users:(("/usr/bin/redis-server",...)) -> redis-server
        Assert.AreEqual("redis-server", snapshot.ListeningPorts[2].Process);
        Assert.AreEqual(6379, snapshot.ListeningPorts[2].Port);

        // 4. netstat fallback: 1234/mysqld -> mysqld
        Assert.AreEqual("mysqld", snapshot.ListeningPorts[3].Process);
        Assert.AreEqual(3306, snapshot.ListeningPorts[3].Port);

        // 5. No permission / dash
        Assert.AreEqual("-", snapshot.ListeningPorts[4].Process);
        Assert.AreEqual(53, snapshot.ListeningPorts[4].Port);
    }
}
