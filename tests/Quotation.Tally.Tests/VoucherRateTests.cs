using System.Xml.Linq;

namespace Quotation.Tally.Tests;

public class VoucherRateTests
{
    private static readonly DateOnly Today = new(2026, 9, 30);
    private const string Level = "-Rsk Spirit Level 200Mm";

    [Fact]
    public async Task Sales_and_quotation_types_including_derived_ones_are_used()
    {
        var sim = new Sim(today: Today);
        var all = await new TallyVoucherService(sim.Client()).GetVoucherTypesAsync();
        var types = TallyRateVoucherTypes.Select(all, []);
        Assert.Equal(["GST Sales", "Quotation", "Sales", "TS Quotation"], types);
        // Derived types come with their base type: one request per base type.
        Assert.Equal(["Quotation", "Sales"], TallyRateVoucherTypes.RequestRoots(all, types));

        Assert.Equal(["TS Quotation"], TallyRateVoucherTypes.Select(all, ["ts quotation"])); // configured list wins
        Assert.Equal(["TS Quotation"], TallyRateVoucherTypes.RequestRoots(all, ["TS Quotation"]));
    }

    [Fact]
    public async Task Vouchers_are_read_per_type_through_tallys_index_for_the_requested_period()
    {
        var sim = new Sim(today: Today);
        var svc = new TallyVoucherService(sim.Client());
        var from = Today.AddMonths(-24);
        var to = Today.AddMonths(1);
        var quotations = await svc.GetRateLinesAsync("Quotation", from, to);
        var sales = await svc.GetRateLinesAsync("Sales", from, to);

        // Only vouchers of the requested type (and derived types) are examined - not every voucher in the company.
        Assert.Equal(sim.Engine.Data.Vouchers.Count(v => v.Type is "TS Quotation" or "Quotation" or "GST Sales" or "Sales"), sim.Engine.VoucherScanCount);

        var level = quotations.Concat(sales).Where(l => l.ItemName == Level).OrderBy(l => l.Date).ToList();
        // Cancelled quotation, purchase, sales order and the voucher outside the period are not used.
        Assert.Equal([9500m, 9800m, 10500m], level.Select(l => l.Rate));
        var latest = level[^1];
        Assert.Equal(("TS Quotation", "TSQ-3403", "BHARAT PRECISION ENGINEERING", "NOS"), (latest.VoucherType, latest.VoucherNumber, latest.Party, latest.Unit));
        Assert.Equal(Today.AddDays(-5), latest.Date);

        // One week only.
        var week = await svc.GetRateLinesAsync("Quotation", Today.AddDays(-7), Today);
        Assert.Equal(["TSQ-3403"], week.Where(l => l.ItemName == Level).Select(l => l.VoucherNumber));

        // Only vouchers created/altered after a given AlterID.
        var maxAlter = quotations.Max(l => l.AlterId);
        Assert.Empty(await svc.GetRateLinesAsync("Quotation", from, to, maxAlter));
        sim.Engine.Data.AddVoucher("TS Quotation", Today, "TSQ-3410", "SONEPAR INDIA PRIVATE LIMITED", Level, 10800m);
        Assert.Equal(10800m, Assert.Single(await svc.GetRateLinesAsync("Quotation", from, to, maxAlter)).Rate);
        Assert.Equal(0, sim.Engine.NonExportRequestCount); // read-only
    }

    [Fact]
    public void Rate_is_derived_from_amount_when_only_an_amount_was_entered()
    {
        var v = XElement.Parse("""
            <VOUCHER VCHTYPE="Sales">
              <DATE>20260915</DATE><VOUCHERNUMBER>12</VOUCHERNUMBER><PARTYLEDGERNAME>ABC</PARTYLEDGERNAME><ALTERID> 9</ALTERID>
              <ALLINVENTORYENTRIES.LIST>
                <STOCKITEMNAME>ITEM A</STOCKITEMNAME><RATE></RATE><AMOUNT>-2500.00</AMOUNT><BILLEDQTY> 2 NOS</BILLEDQTY>
              </ALLINVENTORYENTRIES.LIST>
              <ALLINVENTORYENTRIES.LIST>
                <STOCKITEMNAME>ITEM B</STOCKITEMNAME><RATE>1,450.00/Nos</RATE><AMOUNT>1450.00</AMOUNT><BILLEDQTY> 1 Nos</BILLEDQTY>
              </ALLINVENTORYENTRIES.LIST>
            </VOUCHER>
            """);
        var lines = TallyVoucherParser.ParseRateLines(v);
        Assert.Equal(2, lines.Count);
        Assert.Equal(("ITEM A", 1250m, "NOS", "Sales"), (lines[0].ItemName, lines[0].Rate, lines[0].Unit, lines[0].VoucherType));
        Assert.Equal(("ITEM B", 1450m, "Nos"), (lines[1].ItemName, lines[1].Rate, lines[1].Unit));
    }

    [Fact]
    public void Voucher_request_passes_the_read_only_guard()
    {
        var xml = TallyRequestBuilder.Collection(new TallyCollectionRequest
        {
            Name = "QtRateVouchers", Type = "Vouchers : VoucherType", ChildOf = "Import Sales", BelongsTo = true,
            NativeMethods = TallyVoucherService.VoucherMethods,
        }, "T.SAIFUDDIN & CO.", new Dictionary<string, string> { ["SVFROMDATE"] = "20260901", ["SVTODATE"] = "20260907" });
        TallyReadOnlyGuard.EnsureReadOnly(xml);
        var doc = XDocument.Parse(xml);
        Assert.Equal("\"Import Sales\"", doc.Descendants("CHILDOF").Single().Value);
        Assert.Equal("Yes", doc.Descendants("BELONGSTO").Single().Value);
    }
}
