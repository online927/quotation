namespace Quotation.Core.Gst;

/// <summary>GST state codes (first two digits of a GSTIN).</summary>
public static class IndianStates
{
    private static readonly (string Code, string Name, string[] Aliases)[] States =
    [
        ("01", "Jammu and Kashmir", ["Jammu & Kashmir", "J&K"]),
        ("02", "Himachal Pradesh", []),
        ("03", "Punjab", []),
        ("04", "Chandigarh", []),
        ("05", "Uttarakhand", ["Uttaranchal"]),
        ("06", "Haryana", []),
        ("07", "Delhi", ["New Delhi", "NCT of Delhi"]),
        ("08", "Rajasthan", []),
        ("09", "Uttar Pradesh", ["UP"]),
        ("10", "Bihar", []),
        ("11", "Sikkim", []),
        ("12", "Arunachal Pradesh", []),
        ("13", "Nagaland", []),
        ("14", "Manipur", []),
        ("15", "Mizoram", []),
        ("16", "Tripura", []),
        ("17", "Meghalaya", []),
        ("18", "Assam", []),
        ("19", "West Bengal", []),
        ("20", "Jharkhand", []),
        ("21", "Odisha", ["Orissa"]),
        ("22", "Chhattisgarh", ["Chattisgarh"]),
        ("23", "Madhya Pradesh", ["MP"]),
        ("24", "Gujarat", []),
        ("26", "Dadra and Nagar Haveli and Daman and Diu", ["Daman & Diu", "Daman and Diu", "Dadra & Nagar Haveli", "Dadra and Nagar Haveli"]),
        ("27", "Maharashtra", []),
        ("29", "Karnataka", []),
        ("30", "Goa", []),
        ("31", "Lakshadweep", ["Lakshadweep Islands"]),
        ("32", "Kerala", []),
        ("33", "Tamil Nadu", ["Tamilnadu"]),
        ("34", "Puducherry", ["Pondicherry"]),
        ("35", "Andaman and Nicobar Islands", ["Andaman & Nicobar Islands"]),
        ("36", "Telangana", []),
        ("37", "Andhra Pradesh", ["Andhra Pradesh (New)"]),
        ("38", "Ladakh", []),
        ("97", "Other Territory", []),
    ];

    private static readonly Dictionary<string, (string Code, string Name)> ByName = BuildByName();

    private static Dictionary<string, (string, string)> BuildByName()
    {
        var d = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, name, aliases) in States)
        {
            d[Normalize(name)] = (code, name);
            foreach (var a in aliases) d[Normalize(a)] = (code, name);
        }
        return d;
    }

    private static string Normalize(string s) => string.Join(' ', s.Replace("&", " and ").Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    public static string? CodeForName(string? stateName) =>
        string.IsNullOrWhiteSpace(stateName) ? null : ByName.TryGetValue(Normalize(stateName), out var v) ? v.Code : null;

    public static string? NameForCode(string? code) => States.FirstOrDefault(s => s.Code == code).Name;

    /// <summary>State code from GSTIN (preferred) or state name.</summary>
    public static string ResolveCode(string? gstin, string? stateName)
    {
        if (Gstin.IsWellFormed(gstin) && NameForCode(gstin![..2]) is not null) return gstin[..2];
        return CodeForName(stateName) ?? "";
    }
}
