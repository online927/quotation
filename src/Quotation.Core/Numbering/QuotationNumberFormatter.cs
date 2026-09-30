using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Quotation.Core.Domain;

namespace Quotation.Core.Numbering;

/// <summary>
/// Formats quotation numbers from a configurable pattern.
/// Tokens: {FY} → 2526, {FY_LABEL} → 2025-26, {FYS2}/{FYE2} → 25/26, {FYS4}/{FYE4} → 2025/2026,
/// {SEQ} → 3247, {SEQ:5} → 03247. Everything else is copied literally.
/// Example: "TSQ{FY}-{SEQ}" → "TSQ2526-3247".
/// </summary>
public static partial class QuotationNumberFormatter
{
    [GeneratedRegex(@"\{([A-Z0-9_]+)(?::(\d{1,2}))?\}")]
    private static partial Regex TokenRegex();

    public static string Format(string pattern, FinancialYear fy, int sequence)
    {
        Validate(pattern);
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence must be positive.");
        return TokenRegex().Replace(pattern, m => Resolve(m.Groups[1].Value, m.Groups[2].Value, fy, sequence));
    }

    /// <summary>Throws <see cref="FormatException"/> when the pattern is unusable.</summary>
    public static void Validate(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) throw new FormatException("Number pattern is empty.");
        var seqCount = 0;
        foreach (Match m in TokenRegex().Matches(pattern))
        {
            var token = m.Groups[1].Value;
            if (token == "SEQ") seqCount++;
            else if (token is not ("FY" or "FY_LABEL" or "FYS2" or "FYE2" or "FYS4" or "FYE4"))
                throw new FormatException($"Unknown token {{{token}}} in number pattern.");
        }
        if (seqCount != 1) throw new FormatException("Number pattern must contain {SEQ} exactly once.");
        var stripped = TokenRegex().Replace(pattern, "");
        if (stripped.Contains('{') || stripped.Contains('}')) throw new FormatException("Unbalanced braces in number pattern.");
        if (stripped.Any(char.IsControl)) throw new FormatException("Number pattern contains control characters.");
    }

    /// <summary>Makes a quotation number safe for use as a file name (e.g. QT/2025-26/7 → QT-2025-26-7).</summary>
    public static string ToFileName(string number)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };
        return new string(number.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
    }

    /// <summary>Extracts the sequence from a number produced with the same pattern and FY, or null.</summary>
    public static int? TryParseSequence(string pattern, FinancialYear fy, string number)
    {
        Validate(pattern);
        var regex = new StringBuilder("^");
        var last = 0;
        foreach (Match m in TokenRegex().Matches(pattern))
        {
            regex.Append(Regex.Escape(pattern[last..m.Index]));
            regex.Append(m.Groups[1].Value == "SEQ"
                ? "(?<seq>\\d+)"
                : Regex.Escape(Resolve(m.Groups[1].Value, m.Groups[2].Value, fy, 1)));
            last = m.Index + m.Length;
        }
        regex.Append(Regex.Escape(pattern[last..])).Append('$');
        var match = Regex.Match(number.Trim(), regex.ToString(), RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["seq"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seq)
            ? seq
            : null;
    }

    private static string Resolve(string token, string width, FinancialYear fy, int sequence) => token switch
    {
        "FY" => fy.ShortCode,
        "FY_LABEL" => fy.Label,
        "FYS2" => (fy.StartYear % 100).ToString("00", CultureInfo.InvariantCulture),
        "FYE2" => (fy.EndYear % 100).ToString("00", CultureInfo.InvariantCulture),
        "FYS4" => fy.StartYear.ToString(CultureInfo.InvariantCulture),
        "FYE4" => fy.EndYear.ToString(CultureInfo.InvariantCulture),
        "SEQ" => width.Length > 0
            ? sequence.ToString(CultureInfo.InvariantCulture).PadLeft(int.Parse(width, CultureInfo.InvariantCulture), '0')
            : sequence.ToString(CultureInfo.InvariantCulture),
        _ => throw new FormatException($"Unknown token {{{token}}}."),
    };
}
