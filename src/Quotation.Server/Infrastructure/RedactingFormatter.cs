using System.Text.RegularExpressions;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace Quotation.Server.Infrastructure;

/// <summary>
/// Log formatter that masks anything resembling a credential before it reaches disk.
/// Secrets should never be logged in the first place; this is defence in depth.
/// </summary>
public sealed partial class RedactingFormatter(string outputTemplate) : ITextFormatter
{
    private readonly MessageTemplateTextFormatter _inner = new(outputTemplate);

    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var buffer = new StringWriter();
        _inner.Format(logEvent, buffer);
        output.Write(Redact(buffer.ToString()));
    }

    public static string Redact(string text)
    {
        text = AnthropicKey().Replace(text, "sk-ant-***");
        text = BearerToken().Replace(text, "Bearer ***");
        text = GoogleToken().Replace(text, "ya29.***");
        text = JsonSecret().Replace(text, m => $"{m.Groups[1].Value}\"***\"");
        return text;
    }

    [GeneratedRegex(@"sk-ant-[A-Za-z0-9_\-]{8,}")]
    private static partial Regex AnthropicKey();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9_\-\.=+/]{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"ya29\.[A-Za-z0-9_\-\.]+")]
    private static partial Regex GoogleToken();

    [GeneratedRegex(@"(""(?:access_token|refresh_token|client_secret|api_?key|password|token)""\s*:\s*)""[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex JsonSecret();
}
