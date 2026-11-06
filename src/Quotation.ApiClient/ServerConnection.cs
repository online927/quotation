using System.Net;
using System.Net.Sockets;

namespace Quotation.ApiClient;

/// <summary>
/// How a client PC connects to the Quotation Server on the office network.
/// Windows tries the addresses of a PC name one after another and waits about 21 seconds for one that never answers
/// (e.g. an IPv6 address the server's firewall drops, or an old IP); a PC then waits 21, 42 or 60 seconds for answers
/// the server gave in a few milliseconds. Here the normal (IPv4) addresses are tried first, an address that does not
/// answer is given up after a few seconds, and an open connection is kept and reused between requests.
/// </summary>
public static class ServerConnection
{
    /// <summary>How long one address may take to accept the connection before the next one is tried.</summary>
    public static TimeSpan AddressTimeout { get; set; } = TimeSpan.FromSeconds(3);

    public static SocketsHttpHandler CreateHandler() => new()
    {
        ConnectCallback = ConnectAsync,
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(30),
        KeepAlivePingDelay = TimeSpan.FromSeconds(30),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
        UseProxy = false, // the server is on the office network: never through an internet proxy
    };

    /// <summary>The server's addresses, normal (IPv4) ones first.</summary>
    public static IReadOnlyList<IPAddress> Order(IEnumerable<IPAddress> addresses) =>
        addresses.Distinct()
            .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
            .ThenBy(a => a.IsIPv6LinkLocal ? 1 : 0)
            .ToList();

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : Order(await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false));
        if (addresses.Count == 0) throw new SocketException((int)SocketError.HostNotFound);

        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attempt.CancelAfter(AddressTimeout);
                await socket.ConnectAsync(new IPEndPoint(address, port), attempt.Token).ConfigureAwait(false);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                last = ex; // this address did not answer in time: try the next one
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        throw last is SocketException se ? se : new SocketException((int)SocketError.TimedOut);
    }
}
