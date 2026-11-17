using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Unicode;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Quotation.Tally;

/// <summary>
/// Low-level XML-over-HTTP client for TallyPrime. Sends export requests and parses the
/// responses tolerantly (Tally emits character references that are invalid in XML 1.0).
/// </summary>
public sealed class TallyXmlClient(HttpClient http, TallyConnectionOptions options, ILogger? logger = null,
    TallyLoadGuard? guard = null, bool probe = false)
{
    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        CheckCharacters = false,
        DtdProcessing = DtdProcessing.Prohibit,
        IgnoreComments = true,
        IgnoreWhitespace = true,
        Async = true,
    };

    public TallyConnectionOptions Options => options;

    static TallyXmlClient() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // Windows-1252 (Tally's ANSI)

    /// <summary>
    /// Tally addresses that did not accept a Unicode (UTF-16) request; they are asked in UTF-8 as before. Remembered for the
    /// life of the server, so an older Tally is tried in Unicode only once.
    /// </summary>
    private static readonly ConcurrentDictionary<string, bool> NoUnicode = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Forgets which Tally addresses did not accept Unicode (tests).</summary>
    public static void ResetEncodingMemory() => NoUnicode.Clear();

    /// <summary>Posts a request and returns the full response document.</summary>
    public async Task<XDocument> ExportAsync(string requestXml, CancellationToken ct = default)
    {
        var unicode = !NoUnicode.ContainsKey(options.Url);
        try
        {
            return await ExportOnceAsync(requestXml, unicode, ct);
        }
        catch (TallyException ex) when (unicode && IsEncodingRefusal(ex))
        {
            RememberNoUnicode(ex);
            return await ExportOnceAsync(requestXml, false, ct);
        }
    }

    private async Task<XDocument> ExportOnceAsync(string requestXml, bool unicode, CancellationToken ct)
    {
        using var response = await SendAsync(requestXml, unicode, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var (text, head) = await CreateTextReaderAsync(stream, response, ct);
        if (unicode) ThrowIfUnicodeRefused(head);
        using var reader = XmlReader.Create(text, ReaderSettings);
        XDocument doc;
        try
        {
            doc = await XDocument.LoadAsync(reader, LoadOptions.None, ct);
        }
        catch (XmlException ex)
        {
            throw new TallyUnreadableException($"Tally returned an unreadable response: {ex.Message}", ex);
        }
        ThrowIfError(doc.Root);
        return doc;
    }

    /// <summary>
    /// A Unicode request that Tally did not understand: an HTTP error, an unreadable or empty answer. Not a refused
    /// connection or a timeout (asking again would only load a busy Tally twice), and not an error Tally reported itself.
    /// </summary>
    private static bool IsEncodingRefusal(TallyException ex) => ex is TallyUnreadableException or TallyHttpException;

    /// <summary>An older Tally that reads a UTF-16 request as bytes answers "Unknown Request" (or nothing).</summary>
    private static void ThrowIfUnicodeRefused(string head)
    {
        if (head.Trim().Length == 0) throw new TallyUnreadableException("Tally returned an empty response.");
        if (head.Contains("Unknown Request", StringComparison.OrdinalIgnoreCase))
            throw new TallyUnreadableException("Tally did not understand the request (Unknown Request).");
    }

    private void RememberNoUnicode(Exception ex)
    {
        NoUnicode[options.Url] = true;
        logger?.LogWarning("Tally at {Url} did not accept a Unicode request ({Reason}); asking in UTF-8 from now on.", options.Url, ex.Message);
    }

    /// <summary>
    /// Streams the objects of a collection response one by one (e.g. every STOCKITEM element),
    /// so that 20,000 items never have to be held in memory as one document.
    /// </summary>
    public async IAsyncEnumerable<XElement> StreamObjectsAsync(string requestXml, string objectElementName,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var unicode = !NoUnicode.ContainsKey(options.Url);
        HttpResponseMessage response;
        TextReader text;
        try
        {
            response = await SendAsync(requestXml, unicode, ct);
            string head;
            (text, head) = await CreateTextReaderAsync(await response.Content.ReadAsStreamAsync(ct), response, ct);
            if (unicode)
            {
                try { ThrowIfUnicodeRefused(head); }
                catch { response.Dispose(); throw; }
            }
        }
        catch (TallyException ex) when (unicode && IsEncodingRefusal(ex))
        {
            RememberNoUnicode(ex);
            unicode = false;
            response = await SendAsync(requestXml, false, ct);
            (text, var _discard) = await CreateTextReaderAsync(await response.Content.ReadAsStreamAsync(ct), response, ct);
        }
        using var answer = response;
        using var reader = XmlReader.Create(text, ReaderSettings);

        while (true)
        {
            bool more;
            try
            {
                more = await reader.ReadAsync();
            }
            catch (XmlException ex)
            {
                throw new TallyUnreadableException($"Tally returned an unreadable response: {ex.Message}", ex);
            }
            if (!more) yield break;
            if (reader.NodeType != XmlNodeType.Element) continue;

            if (reader.Name == objectElementName)
            {
                var element = (XElement)await XNode.ReadFromAsync(reader, ct);
                yield return element;
                // ReadFrom positions the reader after the element; handle a directly following sibling.
                while (reader.NodeType == XmlNodeType.Element && reader.Name == objectElementName)
                {
                    yield return (XElement)await XNode.ReadFromAsync(reader, ct);
                }
                if (reader.NodeType == XmlNodeType.Element) CheckErrorElement(reader);
            }
            else
            {
                CheckErrorElement(reader);
            }
        }
    }

    private static void CheckErrorElement(XmlReader reader)
    {
        if (reader.Name is "LINEERROR" or "RESPONSE")
        {
            var text = TallyText.Clean(reader.ReadElementContentAsString());
            if (text.Length > 0) throw new TallyException("Tally reported an error: " + text);
        }
    }

    /// <summary>
    /// Posts the request. Unicode: UTF-16 with "charset=utf-16", as Tally documents for Unicode data, so that signs in names
    /// (°, ₹, ×, ±, µ) come back as they are; Tally then answers in UTF-16 too.
    /// </summary>
    private async Task<HttpResponseMessage> PostAsync(string requestXml, bool unicode, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.Timeout);
        using var content = new StringContent(requestXml, unicode ? new UnicodeEncoding(false, false) : new UTF8Encoding(false), "text/xml");
        return await http.PostAsync(options.Url, content, timeout.Token);
    }

    private async Task<HttpResponseMessage> SendAsync(string requestXml, bool unicode, CancellationToken ct)
    {
        // Defence in depth: this application must never write to Tally.
        TallyReadOnlyGuard.EnsureReadOnly(requestXml);

        HttpResponseMessage response;
        try
        {
            // Waiting for an earlier request (guard) does not count against the timeout. The whole response is
            // buffered by PostAsync, so the guard is held until Tally has finished answering.
            response = guard is null ? await PostAsync(requestXml, unicode, ct) : await guard.RunAsync(() => PostAsync(requestXml, unicode, ct), probe, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            guard?.RecordTimeout();
            throw new TallyTimeoutException($"Tally at {options.Url} did not respond within {options.Timeout.TotalSeconds:0.#} s.");
        }
        catch (HttpRequestException ex)
        {
            var reason = ex.InnerException is SocketException se ? se.SocketErrorCode.ToString() : ex.Message;
            logger?.LogWarning("Tally connection failed: {Reason}", reason);
            throw new TallyUnavailableException(
                $"Cannot connect to Tally at {options.Url} ({reason}). Check that TallyPrime is running with the company open " +
                "and that F1 > Settings > Connectivity > 'TallyPrime acts as' is Server/Both on this port.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new TallyHttpException($"Tally returned HTTP {(int)response.StatusCode}.");
        }
        return response;
    }

    /// <summary>
    /// Reads Tally's answer in the encoding it really used: a byte-order mark, UTF-16 without a mark (Tally's Unicode
    /// answer), the header's charset for UTF-16, valid UTF-8, the header's other charset, else Windows-1252 (Tally's ANSI,
    /// where ° is one byte). The answer is already in memory (PostAsync buffers it).
    /// </summary>
    private static async Task<(TextReader Reader, string Head)> CreateTextReaderAsync(Stream stream, HttpResponseMessage response, CancellationToken ct)
    {
        MemoryStream buffer;
        if (stream is MemoryStream ms && ms.TryGetBuffer(out _)) buffer = ms;
        else
        {
            buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            buffer.Position = 0;
        }
        buffer.TryGetBuffer(out var all);
        var bytes = all.AsSpan((int)buffer.Position);
        var encoding = DetectEncoding(bytes, response.Content.Headers.ContentType?.CharSet);
        var head = encoding.GetString(bytes[..Math.Min(bytes.Length, 2048)]);
        return (new StreamReader(buffer, encoding, detectEncodingFromByteOrderMarks: true), head);
    }

    public static Encoding DetectEncoding(ReadOnlySpan<byte> bytes, string? charset)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE])) return Encoding.Unicode;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF])) return Encoding.BigEndianUnicode;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) return new UTF8Encoding(false);
        if (bytes.Length >= 2 && bytes[0] != 0 && bytes[1] == 0) return Encoding.Unicode;
        if (bytes.Length >= 2 && bytes[0] == 0 && bytes[1] != 0) return Encoding.BigEndianUnicode;
        Encoding? declared = null;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { declared = Encoding.GetEncoding(charset.Trim().Trim('"')); }
            catch (ArgumentException) { /* unknown charset */ }
        }
        if (declared is UnicodeEncoding) return declared;
        if (Utf8.IsValid(bytes)) return new UTF8Encoding(false);
        if (declared is not null && declared is not UTF8Encoding) return declared;
        return Encoding.GetEncoding(1252);
    }

    private static void ThrowIfError(XElement? root)
    {
        if (root is null) throw new TallyException("Tally returned an empty response.");
        if (root.Name == "RESPONSE")
        {
            throw new TallyException("Tally reported an error: " + TallyText.Clean(root.Value));
        }
        var lineError = root.Descendants("LINEERROR").FirstOrDefault();
        if (lineError is not null) throw new TallyException("Tally reported an error: " + TallyText.Clean(lineError.Value));
        var status = root.Element("HEADER")?.Element("STATUS")?.Value;
        if (status is not null && TallyText.Clean(status) == "0")
        {
            var message = TallyText.Clean(root.Descendants("DATA").FirstOrDefault()?.Value);
            throw new TallyException("Tally reported an error" + (message.Length > 0 ? ": " + message : "."));
        }
    }
}
