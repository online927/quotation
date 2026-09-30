using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Quotation.Tally;

/// <summary>Helpers for the peculiarities of Tally's XML (control characters, date and amount formats).</summary>
public static class TallyText
{
    private static readonly string[] DateFormats =
    [
        "yyyyMMdd", "d-MMM-yyyy", "d-MMM-yy", "dd-MMM-yyyy", "dd-MMM-yy", "d-M-yyyy", "dd-MM-yyyy", "yyyy-MM-dd",
    ];

    /// <summary>Removes control characters (Tally emits e.g. &amp;#4; before "Applicable") and trims.</summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c == '\r' || c == '\n' || c == '\t') sb.Append(c);
            else if (!char.IsControl(c)) sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    public static string Text(XElement? parent, string name) => Clean(parent?.Element(name)?.Value);

    public static DateOnly? Date(string? value)
    {
        var v = Clean(value);
        if (v.Length == 0) return null;
        return DateOnly.TryParseExact(v, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d)
            ? d
            : null;
    }

    public static long Long(string? value) =>
        long.TryParse(Clean(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    public static bool Bool(string? value) => Clean(value).Equals("Yes", StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses amounts/rates such as "15600.00", "-1,234.50", "₹ 15,600.00/NOS", "18 %".</summary>
    public static decimal? Decimal(string? value)
    {
        var v = Clean(value);
        if (v.Length == 0) return null;
        var slash = v.IndexOf('/');
        if (slash >= 0) v = v[..slash];
        var sb = new StringBuilder();
        foreach (var c in v)
        {
            if (char.IsDigit(c) || c == '.' || c == '-') sb.Append(c);
        }
        return decimal.TryParse(sb.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>Unit part of a rate such as "15600.00/NOS" → "NOS".</summary>
    public static string RateUnit(string? value)
    {
        var v = Clean(value);
        var slash = v.IndexOf('/');
        return slash < 0 ? "" : v[(slash + 1)..].Trim();
    }

    /// <summary>Escapes a string for use inside a TDL formula string literal.</summary>
    public static string TdlString(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    /// <summary>All values of a Tally list, e.g. ADDRESS.LIST/ADDRESS.</summary>
    public static List<string> List(XElement? parent, string listName, string itemName) =>
        parent?.Elements(listName).SelectMany(l => l.Elements(itemName)).Select(e => Clean(e.Value)).Where(s => s.Length > 0).ToList()
        ?? [];
}
