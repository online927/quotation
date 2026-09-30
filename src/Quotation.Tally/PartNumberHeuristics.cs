using System.Text.RegularExpressions;

namespace Quotation.Tally;

public static partial class PartNumberHeuristics
{
    // Leading code made of digit-bearing segments joined by - . /, followed by "-" and a word:
    // "187-901-10-UNIVERSAL BEVEL PROTRACTOR" → "187-901-10", "2046S-DIAL INDICATOR" → "2046S".
    [GeneratedRegex(@"^(?<code>[A-Z0-9]*\d[A-Z0-9]*(?:[-./][A-Z0-9]*\d[A-Z0-9]*)*)\s*-\s*(?<rest>[A-Za-z].*)$")]
    private static partial Regex LeadingCode();

    public static string FromName(string name)
    {
        var m = LeadingCode().Match(name.Trim());
        return m.Success && m.Groups["code"].Value.Length >= 3 ? m.Groups["code"].Value : "";
    }
}
