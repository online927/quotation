using System.Globalization;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Tally.Simulator;

/// <summary>
/// Answers TallyPrime XML export requests from a <see cref="SampleDataset"/>, reproducing the
/// response format of TallyPrime (including its invalid &amp;#4; character references, TallyPrime 3+
/// and legacy GST/HSN/address layouts). Used for development, demos and automated tests.
/// </summary>
public sealed partial class TallySimulatorEngine(SampleDataset data)
{
    private readonly Lock _gate = new();

    public SampleDataset Data { get; } = data;

    /// <summary>When false the simulator behaves as if Tally were closed (the HTTP layer refuses).</summary>
    public bool Online { get; set; } = true;

    /// <summary>When false, no company is open (TallyPrime running at the Select Company screen).</summary>
    public bool CompanyOpen { get; set; } = true;

    public int RequestCount { get; private set; }
    public int NonExportRequestCount { get; private set; }
    public List<string> RequestLog { get; } = [];

    private const string Ctl4 = "&#4;";

    public string Handle(string requestXml)
    {
        lock (_gate)
        {
            RequestCount++;
            XDocument request;
            try
            {
                request = XDocument.Parse(requestXml);
            }
            catch (Exception)
            {
                return "<RESPONSE>Unknown Request, cannot be processed</RESPONSE>";
            }

            var header = request.Root?.Element("HEADER");
            var tallyRequest = header?.Element("TALLYREQUEST")?.Value ?? "";
            if (!tallyRequest.Equals("Export", StringComparison.OrdinalIgnoreCase))
            {
                NonExportRequestCount++;
                return "<RESPONSE>Unknown Request, cannot be processed</RESPONSE>";
            }

            var desc = request.Root!.Element("BODY")?.Element("DESC");
            var company = desc?.Element("STATICVARIABLES")?.Element("SVCURRENTCOMPANY")?.Value;
            if (!CompanyOpen)
            {
                return Error("No company is open.");
            }
            if (!string.IsNullOrWhiteSpace(company) && !company.Equals(Data.Company.Name, StringComparison.OrdinalIgnoreCase))
            {
                return Error($"Could not set 'SVCurrentCompany' to '{company}'");
            }

            var collection = desc?.Element("TDL")?.Element("TDLMESSAGE")?.Element("COLLECTION");
            if (collection is null) return Error("Could not find Report");
            var type = collection.Element("TYPE")?.Value ?? "";
            RequestLog.Add(type);
            var formulas = desc!.Element("TDL")!.Element("TDLMESSAGE")!.Elements("SYSTEM")
                .ToDictionary(s => s.Attribute("NAME")?.Value ?? "", s => s.Value);
            var filter = BuildFilter(formulas.Values.ToList());
            var computes = collection.Elements("COMPUTE").Select(c => c.Value.Split(':', 2))
                .Where(p => p.Length == 2).Select(p => (Name: p[0].Trim(), Formula: p[1].Trim())).ToList();

            var sb = new StringBuilder(1024 * 64);
            sb.Append("<ENVELOPE>\r\n <HEADER>\r\n  <VERSION>1</VERSION>\r\n  <STATUS>1</STATUS>\r\n </HEADER>\r\n <BODY>\r\n  <DESC>\r\n  </DESC>\r\n  <DATA>\r\n   <COLLECTION>\r\n");
            switch (type.ToLowerInvariant())
            {
                case "company":
                    WriteCompany(sb, computes);
                    break;
                case "stockitem":
                    foreach (var i in Data.StockItems.Where(i => !i.Deleted && filter(new FilterSubject(i.Name, i.AlterId, i.Parent))))
                        WriteStockItem(sb, i);
                    break;
                case "stockgroup":
                    foreach (var g in Data.StockGroups.Where(g => filter(new FilterSubject(g.Name, g.AlterId, g.Parent))))
                        WriteStockGroup(sb, g);
                    break;
                case "unit":
                    foreach (var u in Data.Units.Where(u => filter(new FilterSubject(u.Name, u.AlterId, ""))))
                        WriteUnit(sb, u);
                    break;
                case "ledger":
                    foreach (var l in Data.Ledgers.Where(l => !l.Deleted && filter(new FilterSubject(l.Name, l.AlterId, l.Parent))))
                        WriteLedger(sb, l);
                    break;
                default:
                    return Error($"Unknown object type '{type}'");
            }
            sb.Append("   </COLLECTION>\r\n  </DATA>\r\n </BODY>\r\n</ENVELOPE>\r\n");
            return sb.ToString();
        }
    }

    // ---------- mutations used by tests / the interactive simulator ----------

    public void ChangeRate(string itemName, decimal newRate, DateOnly? from = null)
    {
        lock (_gate)
        {
            var item = Data.StockItems.First(i => i.Name == itemName);
            item.StandardPrices.Add((from ?? Data.CurrentDate, newRate));
            item.AlterId = Data.NextAlterId();
        }
    }

    public SimStockItem AddStockItem(string name, string group, string unit, string hsn, decimal gst, decimal rate)
    {
        lock (_gate)
        {
            var item = new SimStockItem
            {
                AlterId = Data.NextAlterId(), Name = name, Parent = group, Unit = unit,
                Gst = [new SimGst { Hsn = hsn, Rate = gst }],
                StandardPrices = [(new DateOnly(2025, 4, 1), rate)],
            };
            Data.StockItems.Add(item);
            return item;
        }
    }

    public void DeleteStockItem(string name)
    {
        lock (_gate)
        {
            Data.StockItems.First(i => i.Name == name).Deleted = true;
        }
    }

    public void RenameLedger(string name, string newName)
    {
        lock (_gate)
        {
            var l = Data.Ledgers.First(x => x.Name == name);
            l.Name = newName;
            l.AlterId = Data.NextAlterId();
        }
    }

    // ---------- filters ----------

    private readonly record struct FilterSubject(string Name, long AlterId, string Parent);

    [GeneratedRegex(@"\$AlterID\s*>\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex AlterIdRegex();

    [GeneratedRegex(@"\$\$IsBelongsTo:""((?:[^""]|"""")*)""", RegexOptions.IgnoreCase)]
    private static partial Regex BelongsToRegex();

    [GeneratedRegex(@"\$Name\s*=\s*""((?:[^""]|"""")*)""", RegexOptions.IgnoreCase)]
    private static partial Regex NameRegex();

    private Func<FilterSubject, bool> BuildFilter(List<string> formulas)
    {
        var predicates = new List<Func<FilterSubject, bool>>();
        foreach (var f in formulas)
        {
            var alter = AlterIdRegex().Match(f);
            if (alter.Success)
            {
                var min = long.Parse(alter.Groups[1].Value, CultureInfo.InvariantCulture);
                predicates.Add(s => s.AlterId > min);
                continue;
            }
            var groups = BelongsToRegex().Matches(f).Select(m => m.Groups[1].Value.Replace("\"\"", "\"")).ToList();
            if (groups.Count > 0)
            {
                predicates.Add(s => groups.Any(g => BelongsTo(s.Parent, g)));
                continue;
            }
            var names = NameRegex().Matches(f).Select(m => m.Groups[1].Value.Replace("\"\"", "\"")).ToList();
            if (names.Count > 0)
            {
                predicates.Add(s => names.Any(n => n.Equals(s.Name, StringComparison.OrdinalIgnoreCase)));
            }
        }
        return s => predicates.All(p => p(s));
    }

    private bool BelongsTo(string parent, string group)
    {
        var current = parent;
        for (var depth = 0; depth < 10 && current.Length > 0; depth++)
        {
            if (current.Equals(group, StringComparison.OrdinalIgnoreCase)) return true;
            current = Data.LedgerGroups.FirstOrDefault(g => g.Name.Equals(current, StringComparison.OrdinalIgnoreCase))?.Parent
                      ?? Data.StockGroups.FirstOrDefault(g => g.Name.Equals(current, StringComparison.OrdinalIgnoreCase))?.Parent
                      ?? "";
        }
        return false;
    }

    // ---------- writers ----------

    private static string E(string s) => SecurityElement.Escape(s) ?? "";
    private static string D(DateOnly d) => d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    private static string N(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture);

    private static void Tag(StringBuilder sb, string name, string value, string type = "String") =>
        sb.Append("     <").Append(name).Append(" TYPE=\"").Append(type).Append("\">").Append(E(value)).Append("</").Append(name).Append(">\r\n");

    private void WriteCompany(StringBuilder sb, List<(string Name, string Formula)> computes)
    {
        var c = Data.Company;
        sb.Append("    <COMPANY NAME=\"").Append(E(c.Name)).Append("\" RESERVEDNAME=\"\">\r\n");
        Tag(sb, "NAME", c.Name);
        Tag(sb, "GUID", c.Guid);
        Tag(sb, "STARTINGFROM", D(c.FinancialYearFrom), "Date");
        Tag(sb, "BOOKSFROM", D(c.BooksFrom), "Date");
        Tag(sb, "ALTMSTID", " " + Data.MaxAlterId, "Number");
        Tag(sb, "STATENAME", c.State);
        Tag(sb, "GSTREGISTRATIONNUMBER", c.Gstin);
        foreach (var (name, formula) in computes)
        {
            var value = formula.ToUpperInvariant() switch
            {
                "##SVCURRENTCOMPANY" => c.Name,
                "##SVFROMDATE" => D(Data.PeriodFrom),
                "##SVTODATE" => D(Data.PeriodTo),
                "##SVCURRENTDATE" => D(Data.CurrentDate),
                _ => "",
            };
            Tag(sb, name.ToUpperInvariant(), value, formula.Contains("Date", StringComparison.OrdinalIgnoreCase) ? "Date" : "String");
        }
        sb.Append("    </COMPANY>\r\n");
    }

    private static void WriteNames(StringBuilder sb, string name, List<string> aliases)
    {
        sb.Append("     <LANGUAGENAME.LIST>\r\n      <NAME.LIST TYPE=\"String\">\r\n");
        sb.Append("       <NAME>").Append(E(name)).Append("</NAME>\r\n");
        foreach (var a in aliases) sb.Append("       <NAME>").Append(E(a)).Append("</NAME>\r\n");
        sb.Append("      </NAME.LIST>\r\n      <LANGUAGEID TYPE=\"Number\"> 1033</LANGUAGEID>\r\n     </LANGUAGENAME.LIST>\r\n");
    }

    private static void WriteGst(StringBuilder sb, List<SimGst> gst, bool legacy)
    {
        foreach (var g in gst)
        {
            if (!legacy)
            {
                sb.Append("     <HSNDETAILS.LIST>\r\n");
                Tag(sb, "APPLICABLEFROM", D(g.ApplicableFrom), "Date");
                Tag(sb, "HSNCODE", g.InheritFromGroup ? "" : g.Hsn);
                sb.Append("     <SRCOFHSNDETAILS>").Append(g.InheritFromGroup ? "As per Company/Stock Group" : "Specify Details Here").Append("</SRCOFHSNDETAILS>\r\n");
                sb.Append("     </HSNDETAILS.LIST>\r\n");
            }
            sb.Append("     <GSTDETAILS.LIST>\r\n");
            Tag(sb, "APPLICABLEFROM", D(g.ApplicableFrom), "Date");
            Tag(sb, "CALCULATIONTYPE", "On Value");
            if (legacy) Tag(sb, "HSNCODE", g.InheritFromGroup ? "" : g.Hsn);
            Tag(sb, "TAXABILITY", g.InheritFromGroup ? "" : "Taxable");
            sb.Append("     <SRCOFGSTDETAILS>").Append(g.InheritFromGroup ? "As per Company/Stock Group" : "Specify Details Here").Append("</SRCOFGSTDETAILS>\r\n");
            if (!g.InheritFromGroup)
            {
                sb.Append("     <STATEWISEDETAILS.LIST>\r\n      <STATENAME>").Append(Ctl4).Append(" Any</STATENAME>\r\n");
                var heads = legacy
                    ? new[] { ("Central Tax", g.Rate / 2), ("State Tax", g.Rate / 2), ("Integrated Tax", g.Rate), ("Cess", 0m) }
                    : new[] { ("CGST", g.Rate / 2), ("SGST/UTGST", g.Rate / 2), ("IGST", g.Rate), ("Cess", 0m) };
                foreach (var (head, rate) in heads)
                {
                    sb.Append("      <RATEDETAILS.LIST>\r\n");
                    Tag(sb, "GSTRATEDUTYHEAD", head);
                    Tag(sb, "GSTRATEVALUATIONTYPE", "Based on Value");
                    Tag(sb, "GSTRATE", " " + rate.ToString("0.##", CultureInfo.InvariantCulture), "Number");
                    sb.Append("      </RATEDETAILS.LIST>\r\n");
                }
                sb.Append("     </STATEWISEDETAILS.LIST>\r\n");
            }
            sb.Append("     </GSTDETAILS.LIST>\r\n");
        }
    }

    private static void WriteStockItem(StringBuilder sb, SimStockItem i)
    {
        sb.Append("    <STOCKITEM NAME=\"").Append(E(i.Name)).Append("\" RESERVEDNAME=\"\">\r\n");
        Tag(sb, "GUID", i.Guid);
        Tag(sb, "PARENT", i.Parent);
        Tag(sb, "CATEGORY", i.Category);
        Tag(sb, "BASEUNITS", i.Unit);
        Tag(sb, "DESCRIPTION", i.Description);
        Tag(sb, "NARRATION", i.Notes);
        sb.Append("     <GSTAPPLICABLE>").Append(Ctl4).Append(" Applicable</GSTAPPLICABLE>\r\n");
        Tag(sb, "ALTERID", " " + i.AlterId, "Number");
        if (i.PartNo.Length > 0)
        {
            sb.Append("     <MAILINGNAME.LIST TYPE=\"String\">\r\n      <MAILINGNAME>").Append(E(i.PartNo)).Append("</MAILINGNAME>\r\n     </MAILINGNAME.LIST>\r\n");
        }
        WriteNames(sb, i.Name, i.Aliases);
        WriteGst(sb, i.Gst, i.LegacyFormat);
        foreach (var (date, rate) in i.StandardPrices)
        {
            sb.Append("     <STANDARDPRICELIST.LIST>\r\n");
            Tag(sb, "DATE", D(date), "Date");
            Tag(sb, "RATE", $"{N(rate)}/{i.Unit}", "Rate");
            sb.Append("     </STANDARDPRICELIST.LIST>\r\n");
        }
        foreach (var (date, level, rate) in i.PriceLevels)
        {
            sb.Append("     <FULLPRICELIST.LIST>\r\n");
            Tag(sb, "DATE", D(date), "Date");
            Tag(sb, "PRICELEVEL", level);
            sb.Append("      <PRICELEVELLIST.LIST>\r\n");
            Tag(sb, "RATE", $"{N(rate)}/{i.Unit}", "Rate");
            Tag(sb, "DISCOUNT", "0", "Number");
            sb.Append("      </PRICELEVELLIST.LIST>\r\n     </FULLPRICELIST.LIST>\r\n");
        }
        sb.Append("    </STOCKITEM>\r\n");
    }

    private static void WriteStockGroup(StringBuilder sb, SimStockGroup g)
    {
        sb.Append("    <STOCKGROUP NAME=\"").Append(E(g.Name)).Append("\" RESERVEDNAME=\"\">\r\n");
        Tag(sb, "GUID", g.Guid);
        Tag(sb, "PARENT", g.Parent);
        Tag(sb, "ALTERID", " " + g.AlterId, "Number");
        WriteNames(sb, g.Name, []);
        WriteGst(sb, g.Gst, legacy: false);
        sb.Append("    </STOCKGROUP>\r\n");
    }

    private static void WriteUnit(StringBuilder sb, SimUnit u)
    {
        sb.Append("    <UNIT NAME=\"").Append(E(u.Name)).Append("\" RESERVEDNAME=\"\">\r\n");
        Tag(sb, "NAME", u.Name);
        Tag(sb, "GUID", u.Guid);
        Tag(sb, "ORIGINALNAME", u.FormalName);
        Tag(sb, "DECIMALPLACES", " " + u.Decimals, "Number");
        Tag(sb, "ISSIMPLEUNIT", "Yes", "Logical");
        Tag(sb, "ALTERID", " " + u.AlterId, "Number");
        sb.Append("    </UNIT>\r\n");
    }

    private static void WriteLedger(StringBuilder sb, SimLedger l)
    {
        sb.Append("    <LEDGER NAME=\"").Append(E(l.Name)).Append("\" RESERVEDNAME=\"\">\r\n");
        Tag(sb, "GUID", l.Guid);
        Tag(sb, "PARENT", l.Parent);
        Tag(sb, "ALTERID", " " + l.AlterId, "Number");
        Tag(sb, "EMAIL", l.Email);
        Tag(sb, "LEDGERPHONE", l.Phone);
        Tag(sb, "LEDGERMOBILE", l.Mobile);
        Tag(sb, "LEDGERCONTACT", l.Contact);
        Tag(sb, "INCOMETAXNUMBER", l.Pan);
        WriteNames(sb, l.Name, l.Aliases);
        if (l.LegacyFormat)
        {
            if (l.Address.Count > 0)
            {
                sb.Append("     <ADDRESS.LIST TYPE=\"String\">\r\n");
                foreach (var a in l.Address) sb.Append("      <ADDRESS>").Append(E(a)).Append("</ADDRESS>\r\n");
                sb.Append("     </ADDRESS.LIST>\r\n");
            }
            sb.Append("     <MAILINGNAME.LIST TYPE=\"String\">\r\n      <MAILINGNAME>").Append(E(l.MailingName)).Append("</MAILINGNAME>\r\n     </MAILINGNAME.LIST>\r\n");
            Tag(sb, "LEDSTATENAME", l.State);
            Tag(sb, "COUNTRYNAME", l.Country);
            Tag(sb, "PINCODE", l.Pincode);
            Tag(sb, "PARTYGSTIN", l.Gstin);
            Tag(sb, "GSTREGISTRATIONTYPE", l.RegistrationType);
        }
        else
        {
            sb.Append("     <LEDMAILINGDETAILS.LIST>\r\n");
            if (l.Address.Count > 0)
            {
                sb.Append("      <ADDRESS.LIST TYPE=\"String\">\r\n");
                foreach (var a in l.Address) sb.Append("       <ADDRESS>").Append(E(a)).Append("</ADDRESS>\r\n");
                sb.Append("      </ADDRESS.LIST>\r\n");
            }
            Tag(sb, "APPLICABLEFROM", "20170701", "Date");
            Tag(sb, "PINCODE", l.Pincode);
            Tag(sb, "MAILINGNAME", l.MailingName);
            Tag(sb, "STATE", l.State);
            Tag(sb, "COUNTRY", l.Country);
            sb.Append("     </LEDMAILINGDETAILS.LIST>\r\n");
            sb.Append("     <LEDGSTREGDETAILS.LIST>\r\n");
            Tag(sb, "APPLICABLEFROM", "20170701", "Date");
            Tag(sb, "GSTREGISTRATIONTYPE", l.RegistrationType);
            Tag(sb, "PLACEOFSUPPLY", l.State);
            Tag(sb, "GSTIN", l.Gstin);
            sb.Append("     </LEDGSTREGDETAILS.LIST>\r\n");
        }
        foreach (var (name, address, state, pin) in l.ShipTo)
        {
            sb.Append("     <LEDMULTIADDRESSLIST.LIST>\r\n");
            Tag(sb, "ADDRESSNAME", name);
            Tag(sb, "MAILINGNAME", name);
            Tag(sb, "STATE", state);
            Tag(sb, "PINCODE", pin);
            sb.Append("      <ADDRESS.LIST TYPE=\"String\">\r\n");
            foreach (var a in address) sb.Append("       <ADDRESS>").Append(E(a)).Append("</ADDRESS>\r\n");
            sb.Append("      </ADDRESS.LIST>\r\n     </LEDMULTIADDRESSLIST.LIST>\r\n");
        }
        sb.Append("    </LEDGER>\r\n");
    }

    private static string Error(string message) =>
        "<ENVELOPE>\r\n <HEADER>\r\n  <VERSION>1</VERSION>\r\n  <STATUS>0</STATUS>\r\n </HEADER>\r\n <BODY>\r\n  <DATA>\r\n   <LINEERROR>" +
        E(message) + "</LINEERROR>\r\n  </DATA>\r\n </BODY>\r\n</ENVELOPE>\r\n";
}
