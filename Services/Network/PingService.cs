using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace JxTerminal.Services.Network;

public static class PingService
{
    /// <summary>
    /// Measures network roundtrip latency in milliseconds.
    /// Prefers standard ICMP Echo ping (pure network RTT matching OS ping and terminal monitor),
    /// falling back to TCP connect handshake when ICMP is blocked by firewalls or cloud security groups.
    /// </summary>
    public static async Task<int?> MeasureLatencyAsync(string host, int port = 0, int timeoutMs = 2000, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;

        host = host.Trim();

        // 1. Primary: Standard ICMP Echo Ping (matches command-line ping and internal monitor)
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs);
            if (reply.Status == IPStatus.Success)
            {
                return Math.Max(1, (int)reply.RoundtripTime);
            }
        }
        catch
        {
            // ICMP not supported, blocked by firewall/security group, or host unreachable
        }

        // 2. Fallback: TCP connect handshake if port is specified (bypasses ICMP restrictions)
        if (port > 0)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                using var tcp = new TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeoutMs);

                if (IPAddress.TryParse(host, out var ip))
                {
                    await tcp.ConnectAsync(ip, port, cts.Token);
                }
                else
                {
                    await tcp.ConnectAsync(host, port, cts.Token);
                }
                sw.Stop();
                return Math.Max(1, (int)sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                // Timed out on TCP connect
            }
            catch
            {
                // Connection rejected or failed
            }
        }

        return null;
    }
}

