using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Quotation.ApiClient;

namespace Quotation.Server.Tests;

/// <summary>Client PCs reach the server by its normal (IPv4) address first and do not hang on one that never answers.</summary>
public class ServerConnectionTests
{
    [Fact]
    public void Normal_addresses_are_tried_first()
    {
        var order = ServerConnection.Order([IPAddress.Parse("fe80::1"), IPAddress.Parse("2001:db8::5"), IPAddress.Parse("192.168.1.10")]);
        Assert.Equal("192.168.1.10", order[0].ToString());
        Assert.Equal("fe80::1", order[^1].ToString());
    }

    [Fact]
    public async Task Connects_by_pc_name_quickly_and_reuses_the_connection()
    {
        // A tiny server on the IPv4 loopback only: "localhost" also resolves to ::1, which has nothing listening.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = 0;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); } catch { return; }
                Interlocked.Increment(ref accepted);
                _ = Task.Run(async () =>
                {
                    using var c = client;
                    var stream = c.GetStream();
                    var buffer = new byte[4096];
                    while (true)
                    {
                        var request = new StringBuilder();
                        while (!request.ToString().Contains("\r\n\r\n"))
                        {
                            var n = await stream.ReadAsync(buffer);
                            if (n == 0) return;
                            request.Append(Encoding.ASCII.GetString(buffer, 0, n));
                        }
                        var reply = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nContent-Type: text/plain\r\n\r\nok");
                        await stream.WriteAsync(reply);
                    }
                });
            }
        });

        using var http = new HttpClient(ServerConnection.CreateHandler()) { BaseAddress = new Uri($"http://localhost:{port}/") };
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 3; i++) Assert.Equal("ok", await http.GetStringAsync("api/status"));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"took {sw.Elapsed}");
        Assert.Equal(1, accepted); // one connection, kept open between requests
        listener.Stop();
    }
}
