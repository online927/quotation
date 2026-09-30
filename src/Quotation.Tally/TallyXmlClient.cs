using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Quotation.Tally;

/// <summary>
/// Low-level XML-over-HTTP client for TallyPrime. Sends export requests and parses the
/// responses tolerantly (Tally emits character references that are invalid in XML 1.0).
/// </summary>
public sealed class TallyXmlClient(HttpClient http, TallyConnectionOptions options, ILogger? logger = null)
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

    /// <summary>Posts a request and returns the full response document.</summary>
    public async Task<XDocument> ExportAsync(string requestXml, CancellationToken ct = default)
    {
        using var response = await SendAsync(requestXml, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = XmlReader.Create(CreateTextReader(stream, response), ReaderSettings);
        XDocument doc;
        try
        {
            doc = await XDocument.LoadAsync(reader, LoadOptions.None, ct);
        }
        catch (XmlException ex)
        {
            throw new TallyException($"Tally returned an unreadable response: {ex.Message}", ex);
        }
        ThrowIfError(doc.Root);
        return doc;
    }

    /// <summary>
    /// Streams the objects of a collection response one by one (e.g. every STOCKITEM element),
    /// so that 20,000 items never have to be held in memory as one document.
    /// </summary>
    public async IAsyncEnumerable<XElement> StreamObjectsAsync(string requestXml, string objectElementName,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var response = await SendAsync(requestXml, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = XmlReader.Create(CreateTextReader(stream, response), ReaderSettings);

        while (true)
        {
            bool more;
            try
            {
                more = await reader.ReadAsync();
            }
            catch (XmlException ex)
            {
                throw new TallyException($"Tally returned an unreadable response: {ex.Message}", ex);
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

    private async Task<HttpResponseMessage> SendAsync(string requestXml, CancellationToken ct)
    {
        if (!requestXml.Contains("<TALLYREQUEST>Export</TALLYREQUEST>", StringComparison.Ordinal))
        {
            // Defence in depth: this application must never write to Tally.
            throw new InvalidOperationException("Only Export requests may be sent to Tally.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.Timeout);
        var content = new StringContent(requestXml, new UTF8Encoding(false), "text/xml");
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(options.Url, content, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TallyUnavailableException($"Tally at {options.Url} did not respond within {options.Timeout.TotalSeconds:0} s.");
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
            throw new TallyException($"Tally returned HTTP {(int)response.StatusCode}.");
        }
        return response;
    }

    private static TextReader CreateTextReader(Stream stream, HttpResponseMessage response)
    {
        var charset = response.Content.Headers.ContentType?.CharSet;
        Encoding encoding = Encoding.UTF8;
        if (!string.IsNullOrEmpty(charset))
        {
            try { encoding = Encoding.GetEncoding(charset.Trim('"')); }
            catch (ArgumentException) { /* unknown charset: keep UTF-8 */ }
        }
        // detectEncodingFromByteOrderMarks handles UTF-16 responses from some Tally versions.
        return new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
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
