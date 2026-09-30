using System.Globalization;
using System.Text;

namespace Quotation.Core.Search;

/// <summary>
/// Normalizes product/customer text into search tokens so that the way a user types
/// ("10 mm", "2.5 sq.mm", "187901") matches the way Tally stores it ("10MM", "2.5 SQMM", "187-901-10").
/// </summary>
public static class TextNormalizer
{
    /// <summary>Unit words that are joined to a preceding number: "10 mm" → "10mm".</summary>
    private static readonly HashSet<string> Units = new(StringComparer.Ordinal)
    {
        "mm", "cm", "m", "mtr", "km", "inch", "in", "ft", "sqmm", "core", "c", "kg", "g", "gm", "gms", "ltr", "l", "ml",
        "w", "kw", "v", "kv", "a", "amp", "hp", "rpm", "mpa", "bar", "psi", "micron", "um", "deg", "pcs", "nos", "x",
    };

    /// <summary>Canonical spellings applied to every token.</summary>
    private static readonly Dictionary<string, string> Canonical = new(StringComparer.Ordinal)
    {
        ["meter"] = "mtr", ["metre"] = "mtr", ["meters"] = "mtr", ["metres"] = "mtr", ["mtrs"] = "mtr", ["mts"] = "mtr",
        ["mm2"] = "sqmm", ["sqmm2"] = "sqmm",
        ["inches"] = "inch",
        ["amps"] = "amp",
        ["cores"] = "core",
        ["nos."] = "nos", ["no"] = "nos",
        ["piece"] = "pcs", ["pieces"] = "pcs", ["pc"] = "pcs",
        ["kgs"] = "kg",
        ["degree"] = "deg", ["degrees"] = "deg",
    };

    /// <summary>Words ignored in queries (they carry no product meaning).</summary>
    public static readonly HashSet<string> QueryStopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "of", "for", "to", "and", "with", "please", "pls", "quote", "qty", "quantity", "rate", "price", "need", "required",
    };

    /// <summary>Lower-cases, unifies symbols and joins split units: "2.5 Sq.mm" → "2.5sqmm".</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var s = text.ToLowerInvariant()
            .Replace("sq. mm", "sqmm").Replace("sq.mm", "sqmm").Replace("sq mm", "sqmm").Replace("sq.m.m", "sqmm")
            .Replace("mm²", "sqmm").Replace('°', ' ').Replace('×', 'x').Replace('″', '"');
        s = RemoveDiacritics(s);

        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            // Keep decimal points inside numbers: "2.5", "0.75".
            else if (c == '.' && i > 0 && i + 1 < s.Length && char.IsDigit(s[i - 1]) && char.IsDigit(s[i + 1])) sb.Append(c);
            else if (c == '"' && i > 0 && char.IsDigit(s[i - 1])) sb.Append(" inch ");
            else sb.Append(' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Tokens for indexing: canonical words, number+unit joined ("10 mm" → "10mm"), and the parts of
    /// alphanumeric tokens ("10mm" → also "10" and "mm") so partial typing still matches.
    /// </summary>
    public static List<string> IndexTokens(string? text)
    {
        var baseTokens = BaseTokens(text);
        var result = new List<string>(baseTokens.Count * 2);
        for (var i = 0; i < baseTokens.Count; i++)
        {
            var t = baseTokens[i];
            result.Add(t);
            if (i + 1 < baseTokens.Count && IsNumber(t) && Units.Contains(baseTokens[i + 1]))
            {
                result.Add(t + baseTokens[i + 1]);
            }
            foreach (var part in SplitAlphaNumeric(t))
            {
                if (part != t) result.Add(part);
            }
        }
        return result;
    }

    /// <summary>
    /// Tokens for a query: stop words removed, number+unit joined into one token ("10 mm" → "10mm")
    /// so the query demands the combined value, not just any "10" and any "mm".
    /// </summary>
    public static List<string> QueryTokens(string? text)
    {
        var baseTokens = BaseTokens(text);
        var result = new List<string>(baseTokens.Count);
        for (var i = 0; i < baseTokens.Count; i++)
        {
            var t = baseTokens[i];
            if (i + 1 < baseTokens.Count && IsNumber(t) && Units.Contains(baseTokens[i + 1]) && baseTokens[i + 1] != "x")
            {
                result.Add(t + baseTokens[i + 1]);
                i++;
                continue;
            }
            if (baseTokens.Count > 1 && QueryStopWords.Contains(t)) continue;
            result.Add(t);
        }
        return result.Distinct().ToList();
    }

    /// <summary>All letters and digits only: "187-901-10" → "18790110". Used for part-number matching.</summary>
    public static string Compact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static List<string> BaseTokens(string? text)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0) return [];
        var list = new List<string>();
        foreach (var raw in normalized.Split(' '))
        {
            list.Add(Canonical.TryGetValue(raw, out var c) ? c : raw);
        }
        return list;
    }

    public static bool IsNumber(string t)
    {
        if (t.Length == 0) return false;
        foreach (var c in t)
        {
            if (!char.IsDigit(c) && c != '.') return false;
        }
        return char.IsDigit(t[0]);
    }

    /// <summary>"10mm" → ["10", "mm"]; "m10x1.5" → ["m", "10", "x", "1.5"]; "2046s" → ["2046", "s"].</summary>
    public static IEnumerable<string> SplitAlphaNumeric(string token)
    {
        if (token.Length < 2) yield break;
        var start = 0;
        for (var i = 1; i <= token.Length; i++)
        {
            var boundary = i == token.Length || Kind(token[i]) != Kind(token[i - 1]);
            if (!boundary) continue;
            var part = token[start..i];
            if (part.Length > 0 && part.Length < token.Length && part != ".") yield return Canonical.TryGetValue(part, out var c) ? c : part;
            start = i;
        }

        static int Kind(char c) => char.IsDigit(c) || c == '.' ? 1 : 2;
    }

    private static string RemoveDiacritics(string s)
    {
        var formD = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
