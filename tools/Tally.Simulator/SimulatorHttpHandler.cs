using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Tally.Simulator;

/// <summary>In-process HTTP handler so tests can talk to the simulator without opening a port.</summary>
public sealed class SimulatorHttpHandler(TallySimulatorEngine engine) : HttpMessageHandler
{
    public TimeSpan Latency { get; set; } = TimeSpan.Zero;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!engine.Online)
        {
            throw new HttpRequestException("Connection refused", new SocketException((int)SocketError.ConnectionRefused));
        }
        if (Latency > TimeSpan.Zero) await Task.Delay(Latency, ct);
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var xml = engine.Handle(body);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(xml, new UTF8Encoding(false), "text/xml"),
        };
    }
}
