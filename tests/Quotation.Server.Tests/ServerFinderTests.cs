using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Server.Services;

namespace Quotation.Server.Tests;

/// <summary>Phones and Macs connect with the server PC's name ("TSSERVER"), not only its IP address.</summary>
public class ServerFinderTests
{
    private static int FreeUdpPort()
    {
        using var u = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)u.Client.LocalEndPoint!).Port;
    }

    [Fact]
    public void Pc_names_are_told_apart_from_addresses()
    {
        Assert.True(ServerFinder.IsPcName("TSSERVER"));
        Assert.True(ServerFinder.IsPcName("ts-server"));
        Assert.True(ServerFinder.IsPcName("tsserver.local"));
        Assert.False(ServerFinder.IsPcName("192.168.1.20"));
        Assert.False(ServerFinder.IsPcName("localhost"));
        Assert.False(ServerFinder.IsPcName("quotes.example.com"));
        Assert.Equal("tsserver", ServerFinder.BareName("tsserver.local"));
    }

    [Fact]
    public void Windows_name_question_and_answer_are_read()
    {
        // RFC 1002's example name: "FRED" padded with spaces, then the workstation mark (00)
        Assert.Equal("EGFCEFEE" + string.Concat(Enumerable.Repeat("CA", 11)) + "AA", Encoding.ASCII.GetString(ServerFinder.EncodedName("fred"), 1, 32));
        var query = ServerFinder.NetBiosQuery("TSSERVER", 0x1234);
        Assert.Equal(50, query.Length);
        Assert.Equal([0x12, 0x34, 0x01, 0x10], query[..4]);

        // The server PC's answer: same question number, "answer, no error", its address 192.168.1.20
        var answer = new List<byte> { 0x12, 0x34, 0x85, 0x00, 0, 0, 0, 1, 0, 0, 0, 0 };
        answer.AddRange(ServerFinder.EncodedName("TSSERVER"));
        answer.AddRange(new byte[] { 0, 0x20, 0, 1, 0, 0, 0x0E, 0x10, 0, 6, 0, 0, 192, 168, 1, 20 });
        Assert.True(ServerFinder.TryReadNetBiosAnswer(answer.ToArray(), 0x1234, out var address));
        Assert.Equal("192.168.1.20", address.ToString());
        Assert.False(ServerFinder.TryReadNetBiosAnswer(answer.ToArray(), 0x9999, out _)); // someone else's question
        var noSuchName = answer.ToArray();
        noSuchName[3] = 0x03;
        Assert.False(ServerFinder.TryReadNetBiosAnswer(noSuchName, 0x1234, out _));
    }

    [Fact]
    public async Task The_server_answers_to_its_own_pc_name_only()
    {
        var finderPort = FreeUdpPort();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Urls"] = "http://0.0.0.0:5080" }).Build();
        using var service = new ServerFinderService(Options.Create(new ServerOptions { FinderPort = finderPort }), config, NullLogger<ServerFinderService>.Instance);
        await service.StartAsync(CancellationToken.None);
        try
        {
            using var phone = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var server = new IPEndPoint(IPAddress.Loopback, finderPort);

            // The finder starts a moment after the server (like the phone, ask again until it answers)
            UdpReceiveResult? reply = null;
            for (var i = 0; i < 25 && reply is null; i++)
            {
                Assert.False(service.ExecuteTask?.IsFaulted ?? false, service.ExecuteTask?.Exception?.ToString());
                await phone.SendAsync(FinderMessages.FindRequest(Environment.MachineName.ToLowerInvariant()), server);
                using var wait = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
                try { reply = await phone.ReceiveAsync(wait.Token); } catch (OperationCanceledException) { }
            }
            Assert.NotNull(reply);
            Assert.True(FinderMessages.TryReadHere(reply.Value.Buffer, Environment.MachineName, out var port));
            Assert.Equal(5080, port);

            // Another PC's name, or anything else: no answer (a fresh socket, so late answers above do not count)
            using var other = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            await other.SendAsync(FinderMessages.FindRequest("SOMEOTHERPC"), server);
            await other.SendAsync(Encoding.ASCII.GetBytes("hello"), server);
            using var quiet = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await other.ReceiveAsync(quiet.Token));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void The_finder_answers_with_the_web_port()
    {
        Assert.Equal(5080, ServerFinderService.HttpPort("http://0.0.0.0:5080"));
        Assert.Equal(6000, ServerFinderService.HttpPort("http://*:6000;https://*:6001"));
        Assert.Equal(5080, ServerFinderService.HttpPort(null));
    }

    [Fact]
    public async Task A_pc_name_the_device_cannot_look_up_is_found_on_the_network_and_connected_to()
    {
        // The "server PC" ZZQTESTPC1: a web answer on a TCP port and a finder answer on a UDP port of this machine.
        using var web = new TcpListener(IPAddress.Loopback, 0);
        web.Start();
        var webPort = ((IPEndPoint)web.LocalEndpoint).Port;
        _ = Task.Run(async () =>
        {
            try
            {
                using var c = await web.AcceptTcpClientAsync();
                var s = c.GetStream();
                var buffer = new byte[4096];
                var request = new StringBuilder();
                while (!request.ToString().Contains("\r\n\r\n"))
                {
                    var n = await s.ReadAsync(buffer);
                    if (n == 0) return;
                    request.Append(Encoding.ASCII.GetString(buffer, 0, n));
                }
                await s.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"));
            }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        });
        using var finder = new UdpClient(new IPEndPoint(IPAddress.Loopback, webPort)); // the finder listens on the web port number
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var q = await finder.ReceiveAsync(stop.Token);
                    if (FinderMessages.TryReadFind(q.Buffer, out var name) && name.Equals("ZZQTESTPC1", StringComparison.OrdinalIgnoreCase))
                        await finder.SendAsync(FinderMessages.HereReply("ZZQTESTPC1", webPort), q.RemoteEndPoint, stop.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        });

        ServerFinder.AskAt = [IPAddress.Loopback];
        ServerFinder.Forget("ZZQTESTPC1");
        try
        {
            using var http = new HttpClient(ServerConnection.CreateHandler()) { BaseAddress = new Uri($"http://zzqtestpc1:{webPort}/") };
            Assert.Equal("ok", await http.GetStringAsync("api/status"));
            Assert.Equal(IPAddress.Loopback, ServerFinder.Known("ZZQTESTPC1")); // kept for the next connections
            Assert.Null(await ServerFinder.FindAsync("NOSUCHPC99", webPort)); // nobody answers: not found, no hang
        }
        finally
        {
            ServerFinder.AskAt = null;
            ServerFinder.Forget("ZZQTESTPC1");
            await stop.CancelAsync();
        }
    }
}
