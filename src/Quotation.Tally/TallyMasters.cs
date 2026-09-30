using System.Xml.Linq;

namespace Quotation.Tally;

public sealed record TallyGstEntry(DateOnly? ApplicableFrom, string Hsn, decimal? Rate, bool InheritFromParent);

public sealed record TallyPrice(DateOnly? Date, string PriceLevel, decimal Rate, string Unit);

public sealed record TallyStockItem(
    string Guid,
    long AlterId,
    string Name,
    IReadOnlyList<string> Aliases,
    string PartNumber,
    string Parent,
    string Category,
    string BaseUnit,
    string Description,
    string Notes,
    IReadOnlyList<TallyGstEntry> Gst,
    IReadOnlyList<TallyGstEntry> Hsn,
    IReadOnlyList<TallyPrice> StandardPrices,
    IReadOnlyList<TallyPrice> PriceLevels);

public sealed record TallyStockGroup(
    string Guid,
    long AlterId,
    string Name,
    string Parent,
    IReadOnlyList<TallyGstEntry> Gst,
    IReadOnlyList<TallyGstEntry> Hsn);

public sealed record TallyUnit(string Guid, long AlterId, string Name, string FormalName, int DecimalPlaces);

public sealed record TallyAddress(string Name, IReadOnlyList<string> Lines, string State, string Pincode);

public sealed record TallyLedger(
    string Guid,
    long AlterId,
    string Name,
    IReadOnlyList<string> Aliases,
    string Parent,
    string MailingName,
    IReadOnlyList<string> Address,
    string State,
    string Pincode,
    string Country,
    string Gstin,
    string RegistrationType,
    string Pan,
    string Contact,
    string Phone,
    string Mobile,
    string Email,
    IReadOnlyList<TallyAddress> ShipToAddresses);

/// <summary>A master's identity only; used to detect masters deleted in Tally.</summary>
public sealed record TallyMasterId(string Guid, string Name);

/// <summary>Parses Tally master XML (TallyPrime 3+ and older layouts).</summary>
public static class TallyMasterParser
{
    public static TallyStockItem ParseStockItem(XElement e)
    {
        var (name, aliases) = Names(e);
        return new TallyStockItem(
            TallyText.Text(e, "GUID"),
            TallyText.Long(TallyText.Text(e, "ALTERID")),
            name,
            aliases,
            TallyText.List(e, "MAILINGNAME.LIST", "MAILINGNAME").FirstOrDefault() ?? "",
            TallyText.Text(e, "PARENT"),
            TallyText.Text(e, "CATEGORY"),
            TallyText.Text(e, "BASEUNITS"),
            TallyText.Text(e, "DESCRIPTION"),
            TallyText.Text(e, "NARRATION"),
            ParseGst(e),
            ParseHsn(e),
            e.Elements("STANDARDPRICELIST.LIST").Select(p => ParsePrice(p, "")).OfType<TallyPrice>().ToList(),
            e.Elements("FULLPRICELIST.LIST").SelectMany(ParsePriceLevel).ToList());
    }

    public static TallyStockGroup ParseStockGroup(XElement e)
    {
        var (name, _) = Names(e);
        return new TallyStockGroup(
            TallyText.Text(e, "GUID"),
            TallyText.Long(TallyText.Text(e, "ALTERID")),
            name,
            TallyText.Text(e, "PARENT"),
            ParseGst(e),
            ParseHsn(e));
    }

    public static TallyUnit ParseUnit(XElement e)
    {
        var name = TallyText.Clean(e.Attribute("NAME")?.Value);
        if (name.Length == 0) name = TallyText.Text(e, "NAME");
        return new TallyUnit(
            TallyText.Text(e, "GUID"),
            TallyText.Long(TallyText.Text(e, "ALTERID")),
            name,
            TallyText.Text(e, "ORIGINALNAME"),
            (int)TallyText.Long(TallyText.Text(e, "DECIMALPLACES")));
    }

    public static TallyLedger ParseLedger(XElement e, DateOnly asOf)
    {
        var (name, aliases) = Names(e);

        // TallyPrime 3+: dated mailing details; older releases: flat fields.
        var mailing = Latest(e.Elements("LEDMAILINGDETAILS.LIST"), asOf);
        var address = mailing is not null ? TallyText.List(mailing, "ADDRESS.LIST", "ADDRESS") : TallyText.List(e, "ADDRESS.LIST", "ADDRESS");
        var mailingName = mailing is not null
            ? TallyText.Text(mailing, "MAILINGNAME")
            : TallyText.List(e, "MAILINGNAME.LIST", "MAILINGNAME").FirstOrDefault() ?? "";
        var state = FirstNonEmpty(TallyText.Text(mailing, "STATE"), TallyText.Text(e, "LEDSTATENAME"), TallyText.Text(e, "STATENAME"));
        var pincode = FirstNonEmpty(TallyText.Text(mailing, "PINCODE"), TallyText.Text(e, "PINCODE"));
        var country = FirstNonEmpty(TallyText.Text(mailing, "COUNTRY"), TallyText.Text(e, "COUNTRYNAME"));

        var gstReg = Latest(e.Elements("LEDGSTREGDETAILS.LIST"), asOf);
        var gstin = FirstNonEmpty(TallyText.Text(gstReg, "GSTIN"), TallyText.Text(e, "PARTYGSTIN"));
        var regType = FirstNonEmpty(TallyText.Text(gstReg, "GSTREGISTRATIONTYPE"), TallyText.Text(e, "GSTREGISTRATIONTYPE"));
        if (state.Length == 0) state = TallyText.Text(gstReg, "STATE");
        if (state.Length == 0) state = TallyText.Text(gstReg, "PLACEOFSUPPLY");

        var shipTo = e.Elements("LEDMULTIADDRESSLIST.LIST").Select(a => new TallyAddress(
                FirstNonEmpty(TallyText.Text(a, "MAILINGNAME"), TallyText.Text(a, "ADDRESSNAME")),
                TallyText.List(a, "ADDRESS.LIST", "ADDRESS"),
                TallyText.Text(a, "STATE"),
                TallyText.Text(a, "PINCODE")))
            .Where(a => a.Name.Length > 0 || a.Lines.Count > 0)
            .ToList();

        return new TallyLedger(
            TallyText.Text(e, "GUID"),
            TallyText.Long(TallyText.Text(e, "ALTERID")),
            name,
            aliases,
            TallyText.Text(e, "PARENT"),
            mailingName,
            address,
            state,
            pincode,
            country,
            gstin.ToUpperInvariant(),
            regType,
            TallyText.Text(e, "INCOMETAXNUMBER").ToUpperInvariant(),
            TallyText.Text(e, "LEDGERCONTACT"),
            TallyText.Text(e, "LEDGERPHONE"),
            TallyText.Text(e, "LEDGERMOBILE"),
            TallyText.Text(e, "EMAIL"),
            shipTo);
    }

    public static TallyMasterId ParseId(XElement e)
    {
        var name = TallyText.Clean(e.Attribute("NAME")?.Value);
        return new TallyMasterId(TallyText.Text(e, "GUID"), name.Length > 0 ? name : TallyText.Text(e, "NAME"));
    }

    // ---- helpers ----

    private static (string Name, List<string> Aliases) Names(XElement e)
    {
        var names = e.Elements("LANGUAGENAME.LIST").Elements("NAME.LIST").Elements("NAME")
            .Select(n => TallyText.Clean(n.Value)).Where(n => n.Length > 0).ToList();
        var attr = TallyText.Clean(e.Attribute("NAME")?.Value);
        var name = attr.Length > 0 ? attr : names.FirstOrDefault() ?? TallyText.Text(e, "NAME");
        var aliases = names.Where(n => !n.Equals(name, StringComparison.Ordinal)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return (name, aliases);
    }

    private static XElement? Latest(IEnumerable<XElement> dated, DateOnly asOf) =>
        dated.Select(d => (Date: TallyText.Date(TallyText.Text(d, "APPLICABLEFROM")), El: d))
            .Where(x => x.Date is null || x.Date <= asOf)
            .OrderBy(x => x.Date ?? DateOnly.MinValue)
            .LastOrDefault().El;

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => v.Length > 0) ?? "";

    private static bool Inherits(XElement e, string sourceElement)
    {
        var src = TallyText.Text(e, sourceElement);
        return src.Contains("As per", StringComparison.OrdinalIgnoreCase) || src.Contains("Stock Group", StringComparison.OrdinalIgnoreCase);
    }

    private static List<TallyGstEntry> ParseGst(XElement e) =>
        e.Elements("GSTDETAILS.LIST").Select(g =>
        {
            decimal? integrated = null, central = null, state = null;
            foreach (var rate in g.Elements("STATEWISEDETAILS.LIST").Elements("RATEDETAILS.LIST"))
            {
                var head = TallyText.Text(rate, "GSTRATEDUTYHEAD");
                var value = TallyText.Decimal(TallyText.Text(rate, "GSTRATE"));
                if (value is null) continue;
                if (head.Contains("Integrated", StringComparison.OrdinalIgnoreCase) || head.Equals("IGST", StringComparison.OrdinalIgnoreCase)) integrated = value;
                else if (head.Contains("Central", StringComparison.OrdinalIgnoreCase) || head.Equals("CGST", StringComparison.OrdinalIgnoreCase)) central = value;
                else if (head.Contains("State", StringComparison.OrdinalIgnoreCase) || head.StartsWith("SGST", StringComparison.OrdinalIgnoreCase)) state = value;
            }
            var total = integrated ?? (central is not null || state is not null ? (central ?? 0) + (state ?? 0) : null);
            var taxability = TallyText.Text(g, "TAXABILITY");
            if (total is null && (taxability.Equals("Exempt", StringComparison.OrdinalIgnoreCase) || taxability.Equals("Nil Rated", StringComparison.OrdinalIgnoreCase)))
            {
                total = 0;
            }
            return new TallyGstEntry(
                TallyText.Date(TallyText.Text(g, "APPLICABLEFROM")),
                TallyText.Text(g, "HSNCODE"),
                total,
                Inherits(g, "SRCOFGSTDETAILS") || (total is null && TallyText.Text(g, "HSNCODE").Length == 0 && taxability.Length == 0));
        }).ToList();

    private static List<TallyGstEntry> ParseHsn(XElement e) =>
        e.Elements("HSNDETAILS.LIST").Select(h => new TallyGstEntry(
            TallyText.Date(TallyText.Text(h, "APPLICABLEFROM")),
            TallyText.Text(h, "HSNCODE"),
            null,
            Inherits(h, "SRCOFHSNDETAILS"))).ToList();

    private static TallyPrice? ParsePrice(XElement p, string level)
    {
        var raw = TallyText.Text(p, "RATE");
        var rate = TallyText.Decimal(raw);
        return rate is null ? null : new TallyPrice(TallyText.Date(TallyText.Text(p, "DATE")), level, rate.Value, TallyText.RateUnit(raw));
    }

    private static IEnumerable<TallyPrice> ParsePriceLevel(XElement full)
    {
        var level = TallyText.Text(full, "PRICELEVEL");
        var date = TallyText.Date(TallyText.Text(full, "DATE"));
        // A price level can have quantity slabs; the first slab is the base rate.
        var first = full.Elements("PRICELEVELLIST.LIST").FirstOrDefault();
        if (first is null) yield break;
        var raw = TallyText.Text(first, "RATE");
        var rate = TallyText.Decimal(raw);
        if (rate is not null) yield return new TallyPrice(date, level, rate.Value, TallyText.RateUnit(raw));
    }
}
