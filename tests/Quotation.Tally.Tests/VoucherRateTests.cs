using System.Xml.Linq;

namespace Quotation.Tally.Tests;

public class VoucherRateTests
{
    private static readonly DateOnly Today = new(2026, 9, 30);
    private const string Level = "-Rsk Spirit Level 200Mm";

    private static async Task<List<T>> All<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var x in source) list.Add(x);
        return list;
    }

    [Fact]
    public async Task Sales_and_quotation_types_including_derived_ones_are_used()
    {
        var sim = new Sim(today: Today);
        var all = await new TallyVoucherService(sim.Client()).GetVoucherTypesAsync();
        var types = TallyRateVoucherTypes.Select(all, []);
        Assert.Equal(["GST Sales", "Quotation", "Sales", "TS Quotation"], types);

        Assert.Equal(["TS Quotation"], TallyRateVoucherTypes.Select(all, ["ts quotation"])); // configured list wins
    }

    [Fact]
    public async Task Rate_lines_come_from_invoice_and_accounting_vouchers_in_the_window_only()
    {
        var sim = new Sim(today: Today);
        var svc = new TallyVoucherService(sim.Client());
        var types = TallyRateVoucherTypes.Select(await svc.GetVoucherTypesAsync(), []);
        var lines = await All(svc.GetRateLinesAsync(types, Today.AddMonths(-24), Today.AddMonths(1)));
        var level = lines.Where(l => l.ItemName == Level).OrderBy(l => l.Date).ToList();

        // Cancelled quotation, purchase, sales order and the voucher older than the window are not used.
        Assert.Equal([9500m, 9800m, 10500m], level.Select(l => l.Rate));
        var latest = level[^1];
        Assert.Equal("TS Quotation", latest.VoucherType);
        Assert.Equal("TSQ-3403", latest.VoucherNumber);
        Assert.Equal("BHARAT PRECISION ENGINEERING", latest.Party);
        Assert.Equal("NOS", latest.Unit);
        Assert.Equal(Today.AddDays(-5), latest.Date);

        // Incremental read: only vouchers created/altered after the given AlterID.
        var newer = await All(svc.GetRateLinesAsync(types, Today.AddMonths(-24), Today.AddMonths(1), lines.Max(l => l.AlterId)));
        Assert.Empty(newer);
        sim.Engine.Data.AddVoucher("TS Quotation", Today, "TSQ-3410", "SONEPAR INDIA PRIVATE LIMITED", Level, 10800m);
        newer = await All(svc.GetRateLinesAsync(types, Today.AddMonths(-24), Today.AddMonths(1), lines.Max(l => l.AlterId)));
        Assert.Equal(10800m, Assert.Single(newer).Rate);
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
}
