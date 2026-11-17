using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Tally.Simulator;

/// <summary>How the simulator answers, to copy different TallyPrime versions.</summary>
public enum SimulatorEncoding
{
    /// <summary>Like TallyPrime: a UTF-16 request gets a UTF-16 answer (no byte-order mark), anything else UTF-8.</summary>
    SameAsRequest,
    /// <summary>An older Tally: always answers in ANSI (Windows-1252, ° is one byte) and says it is UTF-8.</summary>
    AnsiSaysUtf8,
    /// <summary>An older Tally that cannot read a UTF-16 request: "Unknown Request"; UTF-8 requests get ANSI answers.</summary>
    NoUnicode,
}

/// <summary>In-process HTTP handler so tests can talk to the simulator without opening a port.</summary>
public sealed class SimulatorHttpHandler(TallySimulatorEngine engine) : HttpMessageHandler
{
    static SimulatorHttpHandler() => System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public TimeSpan Latency { get; set; } = TimeSpan.Zero;

    public SimulatorEncoding AnswerEncoding { get; set; } = SimulatorEncoding.SameAsRequest;

    /// <summary>Requests received, with the charset each was sent in (tests).</summary>
    public List<string> RequestCharsets { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!engine.Online)
        {
            throw new HttpRequestException("Connection refused", new SocketException((int)SocketError.ConnectionRefused));
        }
        var delay = Latency > TimeSpan.Zero ? Latency : engine.ResponseDelay;
        if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
        var charset = request.Content?.Headers.ContentType?.CharSet ?? "";
        lock (RequestCharsets) RequestCharsets.Add(charset);
        var unicodeRequest = charset.StartsWith("utf-16", StringComparison.OrdinalIgnoreCase);
        if (unicodeRequest && AnswerEncoding == SimulatorEncoding.NoUnicode)
        {
            return Answer("<RESPONSE>Unknown Request, cannot be processed</RESPONSE>", System.Text.Encoding.ASCII, "utf-8");
        }
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var xml = engine.Handle(body);
        return AnswerEncoding switch
        {
            SimulatorEncoding.SameAsRequest when unicodeRequest => Answer(xml, new UnicodeEncoding(false, false), "utf-16"),
            SimulatorEncoding.SameAsRequest => Answer(xml, new UTF8Encoding(false), "utf-8"),
            _ => Answer(xml, System.Text.Encoding.GetEncoding(1252), "utf-8"),
        };
    }

    private static HttpResponseMessage Answer(string xml, System.Text.Encoding encoding, string declared)
    {
        var content = new ByteArrayContent(encoding.GetBytes(xml));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/xml") { CharSet = declared };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
