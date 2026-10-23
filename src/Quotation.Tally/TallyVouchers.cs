using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Quotation.Tally;

/// <summary>A voucher type and the type it is derived from (e.g. "TS Quotation" → "Quotation").</summary>
public sealed record TallyVoucherTypeInfo(string Name, string Parent, string ReservedName);

/// <summary>One item line of a sales/quotation voucher: the rate at which an item was sold or quoted.</summary>
public sealed record TallyRateLine(
    DateOnly Date,
    string VoucherType,
    string VoucherNumber,
    string Party,
    long AlterId,
    string ItemName,
    decimal Rate,
    string Unit);

/// <summary>Parses voucher types and the item lines of vouchers.</summary>
public static class TallyVoucherParser
{
    public static TallyVoucherTypeInfo ParseVoucherType(XElement e) => new(
        TallyCompanyService.NameOf(e),
        TallyText.Text(e, "PARENT"),
        TallyText.Clean(e.Attribute("RESERVEDNAME")?.Value));

    /// <summary>
    /// Item lines with a usable rate. Cancelled/deleted vouchers give none. Invoice-mode vouchers list items in
    /// ALLINVENTORYENTRIES (or INVENTORYENTRIES); accounting-mode vouchers under the ledger's INVENTORYALLOCATIONS.
    /// </summary>
    public static IReadOnlyList<TallyRateLine> ParseRateLines(XElement v)
    {
        if (TallyText.Bool(TallyText.Text(v, "ISCANCELLED")) || TallyText.Bool(TallyText.Text(v, "ISDELETED"))) return [];
        var date = TallyText.Date(TallyText.Text(v, "DATE"));
        if (date is null) return [];
        var type = TallyText.Text(v, "VOUCHERTYPENAME");
        if (type.Length == 0) type = TallyText.Clean(v.Attribute("VCHTYPE")?.Value);
        var number = TallyText.Text(v, "VOUCHERNUMBER");
        var party = TallyText.Text(v, "PARTYLEDGERNAME");
        if (party.Length == 0) party = TallyText.Text(v, "PARTYNAME");
        var alterId = TallyText.Long(TallyText.Text(v, "ALTERID"));

        var entries = v.Elements("ALLINVENTORYENTRIES.LIST").ToList();
        if (entries.Count == 0) entries = v.Elements("INVENTORYENTRIES.LIST").ToList();
        if (entries.Count == 0)
        {
            entries = v.Elements("LEDGERENTRIES.LIST").Concat(v.Elements("ALLLEDGERENTRIES.LIST"))
                .SelectMany(l => l.Elements("INVENTORYALLOCATIONS.LIST")).ToList();
        }

        var lines = new List<TallyRateLine>();
        foreach (var entry in entries)
        {
            var item = TallyText.Text(entry, "STOCKITEMNAME");
            if (item.Length == 0) continue;
            var rawRate = TallyText.Text(entry, "RATE");
            var rate = TallyText.Decimal(rawRate) is { } r ? Math.Abs(r) : 0m;
            var unit = TallyText.RateUnit(rawRate);
            if (rate == 0)
            {
                // Only an amount was entered: derive the rate from amount and billed quantity.
                var qty = Quantity(TallyText.Text(entry, "BILLEDQTY")) ?? Quantity(TallyText.Text(entry, "ACTUALQTY"));
                var amount = TallyText.Decimal(TallyText.Text(entry, "AMOUNT"));
                if (qty is { } q && q != 0 && amount is { } a) rate = Math.Round(Math.Abs(a / q), 2);
                if (unit.Length == 0) unit = QuantityUnit(TallyText.Text(entry, "BILLEDQTY"));
            }
            if (rate <= 0) continue;
            lines.Add(new TallyRateLine(date.Value, type, number, party, alterId, item, rate, unit));
        }
        return lines;
    }

    /// <summary>" 2 NOS" → 2.</summary>
    private static decimal? Quantity(string text)
    {
        var parts = TallyText.Clean(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var q) ? q : null;
    }

    /// <summary>" 2 NOS" → "NOS".</summary>
    private static string QuantityUnit(string text)
    {
        var parts = TallyText.Clean(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1] : "";
    }
}

/// <summary>Decides which voucher types give "last rates".</summary>
public static class TallyRateVoucherTypes
{
    /// <summary>Base types whose vouchers (and all types derived from them) carry selling rates.</summary>
    public static readonly string[] DefaultBaseTypes = ["Sales", "Quotation"];

    /// <summary>
    /// The configured voucher types if any; otherwise every type that is, or is derived from, Sales or Quotation
    /// (e.g. "GST Sales", "TS Quotation").
    /// </summary>
    public static IReadOnlyList<string> Select(IEnumerable<TallyVoucherTypeInfo> all, IReadOnlyCollection<string> configured)
    {
        var types = all.Where(t => t.Name.Length > 0).GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var wanted = configured.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList();
        if (wanted.Count > 0)
        {
            return wanted.Select(w => types.TryGetValue(w, out var t) ? t.Name : w).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        return types.Values.Where(t => DefaultBaseTypes.Contains(BaseType(t, types), StringComparer.OrdinalIgnoreCase))
            .Select(t => t.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string BaseType(TallyVoucherTypeInfo type, Dictionary<string, TallyVoucherTypeInfo> all)
    {
        var current = type;
        for (var depth = 0; depth < 20; depth++)
        {
            if (current.ReservedName.Length > 0) return current.ReservedName;
            if (current.Parent.Length == 0 || current.Parent.Equals(current.Name, StringComparison.OrdinalIgnoreCase) ||
                !all.TryGetValue(current.Parent, out var parent))
            {
                return current.Parent.Length > 0 ? current.Parent : current.Name;
            }
            current = parent;
        }
        return current.Name;
    }
}

/// <summary>Reads voucher types and sales/quotation item rates from Tally (export only).</summary>
public sealed class TallyVoucherService(TallyXmlClient client)
{
    internal static readonly string[] VoucherMethods =
    [
        "Date", "GUID", "AlterID", "VoucherTypeName", "VoucherNumber", "PartyLedgerName", "IsCancelled", "IsDeleted",
        "AllInventoryEntries",
    ];

    public async Task<IReadOnlyList<TallyVoucherTypeInfo>> GetVoucherTypesAsync(CancellationToken ct = default)
    {
        var request = TallyRequestBuilder.Collection(new TallyCollectionRequest
        {
            Name = "QtVoucherTypes",
            Type = "VoucherType",
            NativeMethods = ["Name", "Parent"],
        }, client.Options.CompanyName);
        var list = new List<TallyVoucherTypeInfo>();
        await foreach (var e in client.StreamObjectsAsync(request, "VOUCHERTYPE", ct)) list.Add(TallyVoucherParser.ParseVoucherType(e));
        return list;
    }

    /// <summary>Item lines of the given voucher types dated <paramref name="from"/>..<paramref name="to"/>.</summary>
    public async IAsyncEnumerable<TallyRateLine> GetRateLinesAsync(IReadOnlyCollection<string> voucherTypes, DateOnly from, DateOnly to,
        long? changedAfterAlterId = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (voucherTypes.Count == 0) yield break;
        var filters = new Dictionary<string, string>
        {
            ["QtRateVchType"] = string.Join(" OR ", voucherTypes.Select(t => "$VoucherTypeName = " + TallyText.TdlString(t))),
        };
        if (changedAfterAlterId is > 0) filters["QtRateChanged"] = $"$AlterID > {changedAfterAlterId.Value}";
        var request = TallyRequestBuilder.Collection(new TallyCollectionRequest
        {
            Name = "QtRateVouchers",
            Type = "Voucher",
            NativeMethods = VoucherMethods,
            Filters = filters,
        }, client.Options.CompanyName, new Dictionary<string, string>
        {
            ["SVFROMDATE"] = from.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            ["SVTODATE"] = to.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
        });
        await foreach (var v in client.StreamObjectsAsync(request, "VOUCHER", ct))
        {
            foreach (var line in TallyVoucherParser.ParseRateLines(v)) yield return line;
        }
    }
}
