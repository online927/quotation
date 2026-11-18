using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Quotation.Contracts;

namespace Quotation.ApiClient;

/// <summary>
/// Finds the server PC by its name on the office network when the phone or the Mac cannot (they do not know Windows PC
/// names the way Windows does). It asks the whole network at once, in two ways, and takes the first answer:
/// the Quotation Server's own finder (it answers with its PC name), and Windows' own name service (NetBIOS), which
/// also works with a server that has not been updated. Answers are kept for a few minutes.
/// </summary>
public static class ServerFinder
{
    private const int NetBiosPort = 137;

    /// <summary>Where the question is sent; null = everyone on the office network (tests use one address).</summary>
    public static IReadOnlyList<IPAddress>? AskAt { get; set; }

    /// <summary>How long to wait for an answer.</summary>
    public static TimeSpan Wait { get; set; } = TimeSpan.FromSeconds(2);

    private static readonly ConcurrentDictionary<string, (IPAddress Address, DateTime At)> Found = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(10);

    /// <summary>A PC name such as "TSSERVER" or "tsserver.local" (not an IP address, not a web address with dots, not this device).</summary>
    public static bool IsPcName(string host)
    {
        if (string.IsNullOrWhiteSpace(host) || IPAddress.TryParse(host, out _)) return false;
        var name = BareName(host);
        return name.Length is > 0 and <= 15 && !name.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
               !name.Contains('.') && name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');
    }

    /// <summary>"tsserver.local" → "tsserver".</summary>
    public static string BareName(string host) => FinderMessages.BareName(host);

    /// <summary>The address last found for this name (kept a few minutes), or null.</summary>
    public static IPAddress? Known(string host) =>
        Found.TryGetValue(BareName(host), out var f) && DateTime.UtcNow - f.At < KeepFor ? f.Address : null;

    /// <summary>The kept address did not answer: ask again next time.</summary>
    public static void Forget(string host) => Found.TryRemove(BareName(host), out _);

    /// <summary>Asks the office network for the PC with this name; null when nobody answers in time.</summary>
    public static async Task<IPAddress?> FindAsync(string host, int serverPort, CancellationToken ct = default)
    {
        if (!IsPcName(host)) return null;
        var name = BareName(host);
        if (Known(name) is { } known) return known;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Wait);
        var tasks = new List<Task<IPAddress?>>
        {
            AskAsync(name, serverPort, cts.Token),
            LocalNameAsync(name, cts.Token), // Mac (and some phones): "tsserver.local"
        };
        IPAddress? address = null;
        while (tasks.Count > 0 && address is null)
        {
            var done = await Task.WhenAny(tasks).ConfigureAwait(false);
            tasks.Remove(done);
            address = await done.ConfigureAwait(false);
        }
        await cts.CancelAsync().ConfigureAwait(false);
        if (address is not null) Found[name] = (address, DateTime.UtcNow);
        return address;
    }

    private static async Task<IPAddress?> LocalNameAsync(string name, CancellationToken ct)
    {
        try
        {
            var all = await Dns.GetHostAddressesAsync(name + ".local", AddressFamily.InterNetwork, ct).ConfigureAwait(false);
            return all.FirstOrDefault();
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>One broadcast socket: the finder question to the server's port and the NetBIOS question to port 137.</summary>
    private static async Task<IPAddress?> AskAsync(string name, int serverPort, CancellationToken ct)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        if (OperatingSystem.IsWindows())
        {
            try { udp.Client.IOControl(unchecked((int)0x9800000C), [0, 0, 0, 0], null); } // ignore "port unreachable" from other PCs
            catch (SocketException) { }
        }
        var id = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        var finder = FinderMessages.FindRequest(name);
        var netBios = NetBiosQuery(name, id);
        var targets = AskAt?.ToList() ?? BroadcastAddresses();
        try
        {
            for (var round = 0; round < 2 && !ct.IsCancellationRequested; round++)
            {
                foreach (var target in targets)
                {
                    await TrySend(udp, finder, new IPEndPoint(target, serverPort), ct).ConfigureAwait(false);
                    await TrySend(udp, netBios, new IPEndPoint(target, NetBiosPort), ct).ConfigureAwait(false);
                }
                var until = DateTime.UtcNow.AddMilliseconds(round == 0 ? 600 : 2000);
                while (DateTime.UtcNow < until && !ct.IsCancellationRequested)
                {
                    using var slice = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    slice.CancelAfter(until - DateTime.UtcNow);
                    UdpReceiveResult reply;
                    try
                    {
                        reply = await udp.ReceiveAsync(slice.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (SocketException)
                    {
                        continue; // e.g. "port unreachable" from a PC that is not the server
                    }
                    if (FinderMessages.TryReadHere(reply.Buffer, name, out _)) return reply.RemoteEndPoint.Address;
                    if (TryReadNetBiosAnswer(reply.Buffer, id, out var address)) return address;
                }
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
        return null;
    }

    private static async Task TrySend(UdpClient udp, byte[] data, IPEndPoint to, CancellationToken ct)
    {
        try
        {
            await udp.SendAsync(data, to, ct).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            // this network does not allow it (e.g. no route): the other ways may still answer
        }
    }

    /// <summary>"Everyone on this network" for each network the device is on, and the general one.</summary>
    public static List<IPAddress> BroadcastAddresses()
    {
        var list = new List<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask is null) continue;
                    var ip = ua.Address.GetAddressBytes();
                    var mask = ua.IPv4Mask.GetAddressBytes();
                    if (mask.All(b => b == 0)) continue;
                    var b = new byte[4];
                    for (var i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~mask[i]);
                    var directed = new IPAddress(b);
                    if (!list.Contains(directed)) list.Add(directed);
                }
            }
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException or NotImplementedException)
        {
            // some phones do not list their networks: the general broadcast is used
        }
        return list;
    }

    // ------------------------------------------------------------ Windows' name service (NetBIOS, RFC 1002)

    /// <summary>A broadcast "who is &lt;NAME&gt;?" for the PC's workstation name.</summary>
    public static byte[] NetBiosQuery(string name, ushort id)
    {
        var packet = new List<byte>
        {
            (byte)(id >> 8), (byte)id,
            0x01, 0x10, // a question, asked of everyone
            0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // one question
        };
        packet.AddRange(EncodedName(name));
        packet.AddRange(new byte[] { 0x00, 0x20, 0x00, 0x01 }); // NetBIOS name, Internet class
        return packet.ToArray();
    }

    /// <summary>The name in NetBIOS form: 15 letters (upper case, padded with spaces) and the workstation mark, each half byte as a letter.</summary>
    public static byte[] EncodedName(string name)
    {
        var raw = Encoding.ASCII.GetBytes(BareName(name).ToUpperInvariant().PadRight(15)[..15] + "\0");
        var encoded = new byte[34];
        encoded[0] = 0x20;
        for (var i = 0; i < 16; i++)
        {
            encoded[1 + i * 2] = (byte)('A' + (raw[i] >> 4));
            encoded[2 + i * 2] = (byte)('A' + (raw[i] & 0x0F));
        }
        encoded[33] = 0x00;
        return encoded;
    }

    /// <summary>The PC's answer to our question: its IPv4 address.</summary>
    public static bool TryReadNetBiosAnswer(byte[] data, ushort id, out IPAddress address)
    {
        address = IPAddress.None;
        if (data.Length < 12 + 2 + 10 + 6) return false;
        if (data[0] != (byte)(id >> 8) || data[1] != (byte)id) return false;
        if ((data[2] & 0x80) == 0 || (data[3] & 0x0F) != 0) return false; // not an answer, or "no such name"
        if (((data[6] << 8) | data[7]) == 0) return false; // no answers
        var at = 12;
        if ((data[at] & 0xC0) == 0xC0) at += 2; // the name as a pointer
        else
        {
            while (at < data.Length && data[at] != 0) at += data[at] + 1;
            at++;
        }
        at += 2 + 2 + 4; // type, class, time to live
        if (at + 2 > data.Length) return false;
        var length = (data[at] << 8) | data[at + 1];
        at += 2;
        for (var end = Math.Min(data.Length, at + length); at + 6 <= end; at += 6)
        {
            var ip = new IPAddress(data.AsSpan(at + 2, 4));
            if (!ip.Equals(IPAddress.Any)) { address = ip; return true; }
        }
        return false;
    }
}
