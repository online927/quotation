using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Quotation.Contracts;

namespace Quotation.Server.Services;

/// <summary>
/// Answers "is the PC with this name here?" from phones and Macs on the office network (they cannot look up a Windows
/// PC name by themselves), so they can connect with the server PC's name. A tiny UDP listener on the server's port
/// (5080); it never touches Tally or the database.
/// </summary>
public sealed class ServerFinderService(IOptions<ServerOptions> options, IConfiguration config, ILogger<ServerFinderService> log) : BackgroundService
{
    /// <summary>The port of the server's web address ("http://0.0.0.0:5080" → 5080).</summary>
    public static int HttpPort(string? urls)
    {
        foreach (var part in (urls ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var url = part.Replace("://*", "://localhost").Replace("://+", "://localhost");
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) return uri.Port;
        }
        return 5080;
    }

    /// <summary>The PC name it answers to.</summary>
    public static string PcName => Environment.MachineName;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.EnableBackgroundWorkers) return;
        var httpPort = HttpPort(config["Urls"]);
        var port = options.Value.FinderPort > 0 ? options.Value.FinderPort : httpPort;
        UdpClient udp;
        try
        {
            udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            if (OperatingSystem.IsWindows()) udp.Client.IOControl(unchecked((int)0x9800000C), [0, 0, 0, 0], null); // ignore "port unreachable"
        }
        catch (SocketException ex)
        {
            log.LogWarning(ex, "Server finder not started (UDP port {Port} in use): phones and Macs can still connect with the IP address", port);
            return;
        }
        using (udp)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                UdpReceiveResult request;
                try
                {
                    request = await udp.ReceiveAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException)
                {
                    continue;
                }
                if (!FinderMessages.TryReadFind(request.Buffer, out var name)) continue;
                if (name.Length > 0 && !name.Equals(PcName, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    await udp.SendAsync(FinderMessages.HereReply(PcName, httpPort), request.RemoteEndPoint, stoppingToken);
                }
                catch (SocketException)
                {
                }
            }
        }
    }
}
