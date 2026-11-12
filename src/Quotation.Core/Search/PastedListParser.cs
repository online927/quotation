using System.Globalization;
using System.Text.RegularExpressions;

namespace Quotation.Core.Search;

/// <summary>One item line of a pasted list: what was pasted, the words to search for and the quantity.</summary>
public sealed record PastedLine(string Raw, string Query, decimal Quantity, bool QuantityGiven);

/// <summary>
/// Reads a list copied from WhatsApp, e-mail or Excel ("187-901 x 2", "530-312 - 5 nos", "qty 3 dial gauge")
/// into item lines. Greetings and empty lines are skipped; a line without a quantity counts as 1.
/// </summary>
public static partial class PastedListParser
{
    public const int MaxLines = 100;

    private const string Units = "nos|no|nos\\.|no\\.|pcs|pc|pieces|piece|units|unit|sets|set|ea|each|numbers|qty|pairs|pair|pkt|pkts|box|boxes";

    // WhatsApp copy: "[11/11, 10:15 am] Ravi: 187-901 x 2"
    [GeneratedRegex(@"^\[[^\]]{3,40}\]\s*[^:]{1,40}:\s*")]
    private static partial Regex WhatsAppPrefix();

    // List numbering and bullets: "1. ", "2) ", "- ", "• ", "* ", "a) "
    [GeneratedRegex(@"^\s*(?:\d{1,3}[.)]\s+|[a-zA-Z][.)]\s+|[-•*·>]+\s*)")]
    private static partial Regex Bullet();

    // "... x 2", "... x2", "... * 2", "... × 2" at the end (optionally followed by a unit); whole numbers only, so a
    // thread size such as "M10 x 1.25" is not read as a quantity
    [GeneratedRegex(@"(?:\s+|(?<=\d))[x×*]\s*(?<q>\d+)\s*(?:" + Units + @")?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TimesAtEnd();

    // "... qty 2", "... qty: 2", "... quantity - 2", "... qty 2 nos" at the end
    [GeneratedRegex(@"[\s,;\-]*\b(?:qty|quantity|qnty|qnt)\b\s*[:=\-]?\s*(?<q>\d+(?:\.\d+)?)\s*(?:" + Units + @")?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex QtyWordAtEnd();

    // "... 2 nos", "... - 5 nos", "... 3pcs", "... : 4 sets" at the end ("187-901-10pcs" keeps its part number)
    [GeneratedRegex(@"(?:\s*[,;:=]\s*|\s+-?\s*|-\s+)(?<q>\d+(?:\.\d+)?)\s*(?:" + Units + @")\.?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex NumberUnitAtEnd();

    // "... - 2", "... : 2", "... = 2", "..., 2" at the end (a dash needs a space next to it: "187-901-10" is a part number)
    [GeneratedRegex(@"(?:\s*[:=,;]\s*|\s+-\s*|-\s+)(?<q>\d+(?:\.\d+)?)\s*$")]
    private static partial Regex SeparatorNumberAtEnd();

    // "... 2" at the end (a plain whole number after a space)
    [GeneratedRegex(@"\s+(?<q>\d{1,4})\s*$")]
    private static partial Regex PlainNumberAtEnd();

    // "2 x ...", "2 nos ...", "qty 2 ...", "2 - ..." at the start
    [GeneratedRegex(@"^(?:(?:qty|quantity)\s*[:=\-]?\s*)?(?<q>\d+(?:\.\d+)?)\s*(?:[x×*]|(?:" + Units + @")\.?|-|:)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex QuantityAtStart();

    // Phone numbers (10 or more digits and nothing else); "187-901-10" is a part number.
    [GeneratedRegex(@"^[+()\d\s\-]{8,}$")]
    private static partial Regex PhoneLike();

    // Lines that are not items (only when they hold no number: "Pls quote 187-901 x 2" is an item).
    [GeneratedRegex(@"^(?:dear|hi|hello|hey|good\s+(?:morning|afternoon|evening)|greetings|please|pls|plz|kindly|request|need\s+(?:quotation|quote|rates?|price)|send\s+(?:quotation|quote)|quotation\s+(?:for|required)|thanks?|thank\s+you|thx|regards|best\s+regards|warm\s+regards|sir|madam|noted)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotAnItem();

    /// <summary>True when the text has two or more item lines (a single line is typed into the box as usual).</summary>
    public static bool LooksLikeAList(string? text) => Parse(text).Count >= 2;

    public static List<PastedLine> Parse(string? text)
    {
        var result = new List<PastedLine>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (result.Count >= MaxLines) break;
            var line = ParseLine(rawLine);
            if (line is not null) result.Add(line);
        }
        return result;
    }

    public static PastedLine? ParseLine(string rawLine)
    {
        var raw = rawLine.Trim();
        if (raw.Length == 0 || !raw.Any(char.IsLetterOrDigit)) return null;

        var s = WhatsAppPrefix().Replace(raw, "");
        // Excel rows: columns separated by tabs; a first column holding only a serial number is dropped.
        if (s.Contains('\t'))
        {
            var cells = s.Split('\t').Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
            if (cells.Count > 1 && int.TryParse(cells[0], out _)) cells.RemoveAt(0);
            s = string.Join(' ', cells);
        }
        s = Bullet().Replace(s, "").Trim();
        if (s.Length == 0 || (PhoneLike().IsMatch(s) && s.Count(char.IsDigit) >= 10)) return null;
        if (!s.Any(char.IsDigit) && NotAnItem().IsMatch(s)) return null;

        decimal? quantity = null;
        foreach (var rule in new[] { TimesAtEnd(), QtyWordAtEnd(), NumberUnitAtEnd(), SeparatorNumberAtEnd() })
        {
            if (TryTake(rule, ref s, out var q)) { quantity = q; break; }
        }
        if (quantity is null && TryTakeAtStart(ref s, out var first)) quantity = first;
        // A plain number at the end, only when the rest still has words of its own ("digital caliper 200mm 1").
        if (quantity is null)
        {
            var m = PlainNumberAtEnd().Match(s);
            if (m.Success && s[..m.Index].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2 &&
                !EndsWithSizeWord(s[..m.Index]))
            {
                quantity = decimal.Parse(m.Groups["q"].Value, CultureInfo.InvariantCulture);
                s = s[..m.Index];
            }
        }

        var query = s.Trim(' ', ',', ';', '-', ':', '.', '=');
        if (!query.Any(char.IsLetterOrDigit)) return null;
        if (quantity is <= 0) quantity = null;
        return new PastedLine(raw, query, quantity ?? 1, quantity is not null);
    }

    private static bool TryTake(Regex rule, ref string s, out decimal quantity)
    {
        quantity = 0;
        var m = rule.Match(s);
        if (!m.Success || m.Index == 0) return false; // nothing left to search for
        quantity = decimal.Parse(m.Groups["q"].Value, CultureInfo.InvariantCulture);
        s = s[..m.Index];
        return true;
    }

    private static bool TryTakeAtStart(ref string s, out decimal quantity)
    {
        quantity = 0;
        var m = QuantityAtStart().Match(s);
        if (!m.Success || m.Length >= s.Length) return false;
        quantity = decimal.Parse(m.Groups["q"].Value, CultureInfo.InvariantCulture);
        s = s[m.Length..];
        return true;
    }

    /// <summary>"... size 150", "... range 300": the number belongs to the item, not the quantity.</summary>
    private static bool EndsWithSizeWord(string before)
    {
        var last = before.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.ToLowerInvariant();
        return last is "x" or "×" or "*" or "size" or "range" or "dia" or "length" or "capacity" or "cap" or "model" or "no" or "no." or "sr" or "series" or "type" or "grade" or "class";
    }
}
