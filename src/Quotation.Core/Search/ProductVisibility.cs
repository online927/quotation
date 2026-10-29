namespace Quotation.Core.Search;

/// <summary>
/// Stock items marked "Do Not Use" in Tally: the description (its first line) is a marker word such as "DNU", in any
/// capitals and with or without dots ("D.N.U."), or starts with it followed by other text ("DNU - use 187-901").
/// A description that merely contains the letters inside a word ("DNUT…") is not a marker.
/// </summary>
public static class ProductVisibility
{
    public static bool IsHidden(string? description, IEnumerable<string> markers)
    {
        if (string.IsNullOrWhiteSpace(description)) return false;
        var firstLine = description.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        var text = Normalize(firstLine);
        foreach (var raw in markers)
        {
            var marker = Normalize(raw);
            if (marker.Length == 0) continue;
            if (text == marker) return true;
            if (text.StartsWith(marker, StringComparison.Ordinal) && !char.IsLetterOrDigit(text[marker.Length])) return true;
        }
        return false;
    }

    // Upper case, dots removed, spaces inside the marker words kept as one: "d.n.u. -" → "DNU -".
    private static string Normalize(string value) => value.Replace(".", "").Trim().ToUpperInvariant();
}
