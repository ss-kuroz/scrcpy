using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace KuroScrcpy.Services;

/// <summary>Do tim adb server trong LAN: quet TCP 5037 + handshake protocol cua adb.</summary>
public static class LanScan
{
    public const int AdbPort = 5037;

    /// <summary>IPv4 cua may nay (Wi-Fi, LAN, Tailscale...).</summary>
    public static List<string> LocalIPv4Addresses()
    {
        var result = new List<string>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(ua.Address)) continue;

                var text = ua.Address.ToString();
                if (!result.Contains(text)) result.Add(text);
            }
        }

        return result;
    }

    /// <summary>IP nen dung de may khac ket noi: bo APIPA va adapter ao, nhung giu Tailscale.</summary>
    public static List<string> ShareIPv4Addresses()
    {
        var result = new List<string>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            var text = (ni.Name + " " + ni.Description).ToLowerInvariant();
            var isTailscale = text.Contains("tailscale");
            if (!isTailscale && IsVirtualOrSpecial(ni)) continue;

            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                var bytes = ua.Address.GetAddressBytes();
                if (bytes[0] == 169 && bytes[1] == 254) continue;

                var ip = ua.Address.ToString();
                if (!result.Contains(ip)) result.Add(ip);
            }
        }

        // Uu tien IP LAN that (192.168/10/172.16-31) truoc IP Tailscale 100.x
        return result.OrderBy(ip => ip.StartsWith("100.", StringComparison.Ordinal) ? 1 : 0)
                     .ThenBy(ip => ip, StringComparer.OrdinalIgnoreCase)
                     .ToList();
    }

    /// <summary>Cac host /24 quanh IP cua may (bo network, broadcast va chinh may).</summary>
    public static List<string> LocalSubnetHosts()
    {
        var own = LocalIPv4Addresses().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hosts = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            if (IsVirtualOrSpecial(ni)) continue;

            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                var bytes = ua.Address.GetAddressBytes();

                // 169.254.x.x = APIPA (khong co DHCP) -> khong quet
                if (bytes[0] == 169 && bytes[1] == 254) continue;

                var prefix = ua.PrefixLength;

                // Mang lon hon /24 -> chi quet /24 quanh may cho nhanh; /31 /32 khong co host
                if (prefix < 24) prefix = 24;
                if (prefix > 30) continue;

                var mask = uint.MaxValue << (32 - prefix);
                var ip = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
                var network = ip & mask;
                var broadcast = network | ~mask;

                for (var host = network + 1; host < broadcast; host++)
                {
                    var address = new IPAddress(new[]
                    {
                        (byte)(host >> 24), (byte)(host >> 16), (byte)(host >> 8), (byte)host,
                    }).ToString();

                    if (own.Contains(address) || !seen.Add(address)) continue;
                    hosts.Add(address);
                }
            }
        }

        return hosts;
    }

    /// <summary>Bo adapter ao (Hyper-V, WSL, VMware, VirtualBox, VPN) va Bluetooth cho quet nhanh, dung subnet that.</summary>
    private static bool IsVirtualOrSpecial(NetworkInterface ni)
    {
        var text = (ni.Name + " " + ni.Description).ToLowerInvariant();

        return text.Contains("vethernet") || text.Contains("hyper-v") || text.Contains("virtual")
            || text.Contains("vmware") || text.Contains("virtualbox") || text.Contains("tailscale")
            || text.Contains("zerotier") || text.Contains("wireguard") || text.Contains("tap-")
            || text.Contains("bluetooth") || text.Contains("loopback");
    }

    /// <summary>Handshake adb ("host:version") de chac chan cong 5037 la adb server, khong phai dich vu khac.</summary>
    public static async Task<bool> IsAdbServerAsync(string host, int port = AdbPort, int timeoutMs = 350)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(host, port, cts.Token);

            using var stream = client.GetStream();

            // adb smart socket: 4 ky tu hex do dai + payload
            var request = Encoding.ASCII.GetBytes("000Chost:version");
            await stream.WriteAsync(request, cts.Token);

            var buf = new byte[4];
            var read = 0;
            while (read < 4)
            {
                var n = await stream.ReadAsync(buf.AsMemory(read, 4 - read), cts.Token);
                if (n <= 0) return false;
                read += n;
            }

            return buf[0] == (byte)'O' && buf[1] == (byte)'K'
                && buf[2] == (byte)'A' && buf[3] == (byte)'Y';
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Quet song song danh sach host, tra ve cac IP dang mo adb server.</summary>
    public static async Task<List<string>> FindAdbServersAsync(IEnumerable<string> hosts, int timeoutMs = 350, int concurrency = 64)
    {
        var results = new List<string>();
        using var gate = new SemaphoreSlim(concurrency);

        var tasks = hosts.Select(async host =>
        {
            await gate.WaitAsync();
            try
            {
                if (await IsAdbServerAsync(host, AdbPort, timeoutMs))
                {
                    lock (results) results.Add(host);
                }
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);

        return results.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
