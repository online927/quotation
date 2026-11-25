using System.Text.RegularExpressions;

namespace Quotation.Core.Domain;

/// <summary>
/// Round 38 (DM-1): delivery is never remembered with an item. A delivery line typed inside a description
/// ("Delivery: 2-3 weeks", "Delivery time - 1 week", "Del. ex stock", or just "Ex-stock") is left out before the
/// description is remembered for the next quotation.
/// </summary>
public static partial class DeliveryText
{
    [GeneratedRegex(@"^\s*(delivery(\s*time|\s*period)?|del\.?)(\s*[:\-–]\s*|\s+)\S", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeliveryLine();

    [GeneratedRegex(@"^\s*ex[\s\-]?stock\s*\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExStockLine();

    /// <summary>True for a line that only gives the delivery time.</summary>
    public static bool IsDeliveryLine(string line) => DeliveryLine().IsMatch(line) || ExStockLine().IsMatch(line);

    /// <summary>The description without its delivery lines (trimmed; "" when nothing else is left).</summary>
    public static string WithoutDeliveryLines(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "";
        var lines = description.Replace("\r\n", "\n").Split('\n').Where(l => !IsDeliveryLine(l));
        return string.Join("\n", lines).Trim();
    }
}
