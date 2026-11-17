using System.Text.RegularExpressions;

namespace Quotation.Desktop.Services;

/// <summary>The letters in the badge (top-left corner and sign-in screen): the first letters of the first two words of the company name.</summary>
public static partial class CompanyLetters
{
    /// <summary>"T.SAIFUDDIN &amp; CO." → "TS"; no name → "Q" (Quotations).</summary>
    public static string Of(string? companyName)
    {
        var words = Words().Matches(companyName ?? "").Select(m => m.Value).ToList();
        var letters = string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
        return letters.Length > 0 ? letters : "Q";
    }

    [GeneratedRegex("[A-Za-z0-9]+")]
    private static partial Regex Words();
}
