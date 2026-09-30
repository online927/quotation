namespace Quotation.Tally;

public sealed record ResolvedGst(string Hsn, decimal? Rate, string Source, bool Inherited);

public sealed record ResolvedRate(decimal? Rate, DateOnly? Date, string Source);

/// <summary>
/// Resolves the HSN/SAC and GST rate applicable on a date, following Tally's rules:
/// item details → stock group (and its parents) when the item is set to "As per Company/Stock Group".
/// </summary>
public static class TallyGstResolver
{
    public sealed record GroupGst(string Name, string Parent, IReadOnlyList<TallyGstEntry> Gst, IReadOnlyList<TallyGstEntry> Hsn);

    public static ResolvedGst Resolve(IReadOnlyList<TallyGstEntry> itemGst, IReadOnlyList<TallyGstEntry> itemHsn,
        string parentGroup, IReadOnlyDictionary<string, GroupGst> groups, DateOnly asOf)
    {
        var (hsn, hsnSource) = ResolveHsn(itemGst, itemHsn, "Item");
        var (rate, rateSource) = ResolveRate(itemGst, "Item");
        var inherited = false;

        var group = parentGroup;
        for (var depth = 0; depth < 20 && (hsn.Length == 0 || rate is null) && group.Length > 0; depth++)
        {
            if (!groups.TryGetValue(group, out var g)) break;
            inherited = true;
            if (hsn.Length == 0) (hsn, hsnSource) = ResolveHsn(g.Gst, g.Hsn, "Group: " + g.Name);
            if (rate is null) (rate, rateSource) = ResolveRate(g.Gst, "Group: " + g.Name);
            group = g.Parent;
        }

        var source = hsnSource == rateSource || rateSource.Length == 0 ? hsnSource : rateSource;
        return new ResolvedGst(hsn, rate, rate is null && hsn.Length == 0 ? "" : source, inherited);

        (string, string) ResolveHsn(IReadOnlyList<TallyGstEntry> gst, IReadOnlyList<TallyGstEntry> hsnList, string src)
        {
            var h = Latest(hsnList, asOf);
            if (h is not null && !h.InheritFromParent && h.Hsn.Length > 0) return (h.Hsn, src);
            var g = Latest(gst, asOf);
            if (g is not null && !g.InheritFromParent && g.Hsn.Length > 0) return (g.Hsn, src);
            return ("", "");
        }

        (decimal?, string) ResolveRate(IReadOnlyList<TallyGstEntry> gst, string src)
        {
            var g = Latest(gst, asOf);
            return g is not null && !g.InheritFromParent && g.Rate is not null ? (g.Rate, src) : (null, "");
        }
    }

    private static TallyGstEntry? Latest(IReadOnlyList<TallyGstEntry> entries, DateOnly asOf) =>
        entries.Where(e => e.ApplicableFrom is null || e.ApplicableFrom <= asOf)
            .OrderBy(e => e.ApplicableFrom ?? DateOnly.MinValue)
            .LastOrDefault();
}

/// <summary>Picks the selling rate applicable on a date from standard prices or a price level.</summary>
public static class TallyRateResolver
{
    public const string StandardPrice = "StandardPrice";
    public const string PriceLevel = "PriceLevel";

    public static ResolvedRate Resolve(IReadOnlyList<TallyPrice> standardPrices, IReadOnlyList<TallyPrice> priceLevels,
        string source, string priceLevelName, DateOnly asOf)
    {
        if (source == PriceLevel && !string.IsNullOrWhiteSpace(priceLevelName))
        {
            var p = LatestOn(priceLevels.Where(x => x.PriceLevel.Equals(priceLevelName.Trim(), StringComparison.OrdinalIgnoreCase)), asOf);
            return p is null ? new ResolvedRate(null, null, "") : new ResolvedRate(p.Rate, p.Date, "Price level: " + p.PriceLevel);
        }
        var s = LatestOn(standardPrices, asOf);
        return s is null ? new ResolvedRate(null, null, "") : new ResolvedRate(s.Rate, s.Date, "Standard selling price");
    }

    private static TallyPrice? LatestOn(IEnumerable<TallyPrice> prices, DateOnly asOf) =>
        prices.Where(p => p.Date is null || p.Date <= asOf).OrderBy(p => p.Date ?? DateOnly.MinValue).LastOrDefault();
}
