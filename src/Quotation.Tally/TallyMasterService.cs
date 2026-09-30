using System.Runtime.CompilerServices;

namespace Quotation.Tally;

/// <summary>Which methods (fields) to request. "Selected" keeps responses small; "All" requests every method.</summary>
public enum TallyFetchMode
{
    Selected,
    All,
}

/// <summary>Reads masters (stock items, groups, units, customer ledgers) from Tally.</summary>
public sealed class TallyMasterService(TallyXmlClient client, TallyFetchMode fetchMode = TallyFetchMode.Selected)
{
    internal static readonly string[] StockItemMethods =
    [
        "Name", "GUID", "AlterID", "Parent", "Category", "BaseUnits", "Description", "Narration", "MailingName",
        "LanguageName", "GSTApplicable", "GSTDetails", "HSNDetails", "StandardPriceList", "FullPriceList",
    ];

    internal static readonly string[] StockGroupMethods = ["Name", "GUID", "AlterID", "Parent", "LanguageName", "GSTDetails", "HSNDetails"];

    internal static readonly string[] UnitMethods = ["Name", "GUID", "AlterID", "OriginalName", "DecimalPlaces", "IsSimpleUnit"];

    internal static readonly string[] LedgerMethods =
    [
        "Name", "GUID", "AlterID", "Parent", "LanguageName", "MailingName", "Address", "LedStateName", "StateName",
        "PinCode", "CountryName", "PartyGSTIN", "GSTRegistrationType", "LedMailingDetails", "LedGSTRegDetails",
        "LedMultiAddressList", "IncomeTaxNumber", "LedgerContact", "LedgerPhone", "LedgerMobile", "Email",
    ];

    public IAsyncEnumerable<TallyStockItem> GetStockItemsAsync(long? changedAfterAlterId = null, CancellationToken ct = default) =>
        Stream("QtStockItems", "StockItem", "STOCKITEM", StockItemMethods, AlterFilter(changedAfterAlterId), TallyMasterParser.ParseStockItem, ct);

    public IAsyncEnumerable<TallyStockGroup> GetStockGroupsAsync(long? changedAfterAlterId = null, CancellationToken ct = default) =>
        Stream("QtStockGroups", "StockGroup", "STOCKGROUP", StockGroupMethods, AlterFilter(changedAfterAlterId), TallyMasterParser.ParseStockGroup, ct);

    public IAsyncEnumerable<TallyUnit> GetUnitsAsync(CancellationToken ct = default) =>
        Stream("QtUnits", "Unit", "UNIT", UnitMethods, [], TallyMasterParser.ParseUnit, ct);

    public IAsyncEnumerable<TallyLedger> GetLedgersAsync(IReadOnlyCollection<string> groups, DateOnly asOf,
        long? changedAfterAlterId = null, CancellationToken ct = default)
    {
        var filters = AlterFilter(changedAfterAlterId);
        filters.Add(("QtIsCustomer", GroupFormula(groups)));
        return Stream("QtLedgers", "Ledger", "LEDGER", LedgerMethods, filters, e => TallyMasterParser.ParseLedger(e, asOf), ct);
    }

    /// <summary>Only GUID + name of every stock item: used to detect deletions cheaply.</summary>
    public IAsyncEnumerable<TallyMasterId> GetStockItemIdsAsync(CancellationToken ct = default) =>
        StreamIds("QtStockItemIds", "StockItem", "STOCKITEM", [], ct);

    public IAsyncEnumerable<TallyMasterId> GetLedgerIdsAsync(IReadOnlyCollection<string> groups, CancellationToken ct = default) =>
        StreamIds("QtLedgerIds", "Ledger", "LEDGER", [("QtIsCustomer", GroupFormula(groups))], ct);

    /// <summary>Raw XML of one named master, for the diagnostics screen (field-mapping checks).</summary>
    public async Task<string> GetRawSampleAsync(string type, string name, CancellationToken ct = default)
    {
        var request = TallyRequestBuilder.Collection(new TallyCollectionRequest
        {
            Name = "QtSample",
            Type = type,
            NativeMethods = ["*"],
            Filters = new Dictionary<string, string> { ["QtSampleName"] = "$Name = " + TallyText.TdlString(name) },
        }, client.Options.CompanyName);
        var doc = await client.ExportAsync(request, ct);
        var data = doc.Descendants("COLLECTION").FirstOrDefault();
        return data is null ? TallyText.ToDisplayXml(doc) : TallyText.ToDisplayXml(data);
    }

    internal static string GroupFormula(IReadOnlyCollection<string> groups)
    {
        var list = groups.Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
        if (list.Count == 0) list = ["Sundry Debtors"];
        return string.Join(" OR ", list.Select(g => "$$IsBelongsTo:" + TallyText.TdlString(g.Trim())));
    }

    private static List<(string, string)> AlterFilter(long? after) =>
        after is > 0 ? [("QtChanged", $"$AlterID > {after.Value}")] : [];

    private async IAsyncEnumerable<T> Stream<T>(string name, string type, string element, string[] methods,
        List<(string Name, string Formula)> filters, Func<System.Xml.Linq.XElement, T> parse,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var request = TallyRequestBuilder.Collection(new TallyCollectionRequest
        {
            Name = name,
            Type = type,
            NativeMethods = fetchMode == TallyFetchMode.All ? ["*"] : methods,
            Filters = filters.ToDictionary(f => f.Name, f => f.Formula),
        }, client.Options.CompanyName);
        await foreach (var e in client.StreamObjectsAsync(request, element, ct))
        {
            yield return parse(e);
        }
    }

    private IAsyncEnumerable<TallyMasterId> StreamIds(string name, string type, string element,
        List<(string Name, string Formula)> filters, CancellationToken ct)
    {
        var request = new TallyCollectionRequest
        {
            Name = name,
            Type = type,
            NativeMethods = ["Name", "GUID"],
            Filters = filters.ToDictionary(f => f.Name, f => f.Formula),
        };
        return StreamRequest(request, element, ct);
    }

    private async IAsyncEnumerable<TallyMasterId> StreamRequest(TallyCollectionRequest request, string element,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var xml = TallyRequestBuilder.Collection(request, client.Options.CompanyName);
        await foreach (var e in client.StreamObjectsAsync(xml, element, ct))
        {
            yield return TallyMasterParser.ParseId(e);
        }
    }
}
