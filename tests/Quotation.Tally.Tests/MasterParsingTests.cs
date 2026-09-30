using System.Xml.Linq;

namespace Quotation.Tally.Tests;

public class MasterParsingTests
{
    private static readonly DateOnly Today = new(2026, 9, 30);

    private static async Task<List<T>> All<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var x in source) list.Add(x);
        return list;
    }

    [Fact]
    public async Task Parses_stock_item_in_tallyprime_layout()
    {
        var sim = new Sim();
        var items = await All(new TallyMasterService(sim.Client()).GetStockItemsAsync());
        var bevel = items.Single(i => i.Name == "187-901-10-UNIVERSAL BEVEL PROTRACTOR");
        Assert.Equal("187-901-10", bevel.PartNumber);
        Assert.Equal("MITUTOYO", bevel.Parent);
        Assert.Equal("Mitutoyo", bevel.Category);
        Assert.Equal("NOS", bevel.BaseUnit);
        Assert.Contains("Brand: Mitutoyo", bevel.Description);
        Assert.Equal(2, bevel.StandardPrices.Count);
        Assert.Equal("NOS", bevel.StandardPrices[1].Unit);

        var groups = (await All(new TallyMasterService(sim.Client()).GetStockGroupsAsync()))
            .ToDictionary(g => g.Name, g => new TallyGstResolver.GroupGst(g.Name, g.Parent, g.Gst, g.Hsn));
        var gst = TallyGstResolver.Resolve(bevel.Gst, bevel.Hsn, bevel.Parent, groups, Today);
        Assert.Equal("90172020", gst.Hsn);
        Assert.Equal(18m, gst.Rate);
        Assert.Equal("Item", gst.Source);
        Assert.False(gst.Inherited);

        var rate = TallyRateResolver.Resolve(bevel.StandardPrices, bevel.PriceLevels, TallyRateResolver.StandardPrice, "", Today);
        Assert.Equal(15600m, rate.Rate);
        Assert.Equal(new DateOnly(2025, 4, 1), rate.Date);
    }

    [Fact]
    public async Task Legacy_layout_and_group_inheritance()
    {
        var sim = new Sim();
        var svc = new TallyMasterService(sim.Client());
        var items = await All(svc.GetStockItemsAsync());
        var groups = (await All(svc.GetStockGroupsAsync()))
            .ToDictionary(g => g.Name, g => new TallyGstResolver.GroupGst(g.Name, g.Parent, g.Gst, g.Hsn));

        var legacy = items.Single(i => i.Name == "10MM ALLEN KEY SET");
        var g1 = TallyGstResolver.Resolve(legacy.Gst, legacy.Hsn, legacy.Parent, groups, Today);
        Assert.Equal("82041120", g1.Hsn);
        Assert.Equal(18m, g1.Rate);

        var drill = items.Single(i => i.Name == "HSS DRILL BIT 10MM");
        var g2 = TallyGstResolver.Resolve(drill.Gst, drill.Hsn, drill.Parent, groups, Today);
        Assert.Equal("82075000", g2.Hsn);
        Assert.Equal(18m, g2.Rate);
        Assert.True(g2.Inherited);
        Assert.Equal("Group: Drill Bits", g2.Source);
    }

    [Fact]
    public void Gst_resolution_respects_applicable_from_dates()
    {
        var entries = new List<TallyGstEntry>
        {
            new(new DateOnly(2017, 7, 1), "8467", 28m, false),
            new(new DateOnly(2026, 10, 1), "8467", 18m, false), // future rate change
        };
        var groups = new Dictionary<string, TallyGstResolver.GroupGst>();
        Assert.Equal(28m, TallyGstResolver.Resolve(entries, [], "", groups, Today).Rate);
        Assert.Equal(18m, TallyGstResolver.Resolve(entries, [], "", groups, new DateOnly(2026, 10, 1)).Rate);
    }

    [Fact]
    public void Rate_resolution_by_price_level_and_date()
    {
        var std = new List<TallyPrice> { new(new DateOnly(2024, 4, 1), "", 100, "NOS"), new(new DateOnly(2026, 12, 1), "", 130, "NOS") };
        var levels = new List<TallyPrice> { new(new DateOnly(2025, 4, 1), "Dealer", 90, "NOS") };
        Assert.Equal(100m, TallyRateResolver.Resolve(std, levels, TallyRateResolver.StandardPrice, "", Today).Rate);
        Assert.Equal(130m, TallyRateResolver.Resolve(std, levels, TallyRateResolver.StandardPrice, "", new DateOnly(2026, 12, 1)).Rate);
        Assert.Equal(90m, TallyRateResolver.Resolve(std, levels, TallyRateResolver.PriceLevel, "dealer", Today).Rate);
        Assert.Null(TallyRateResolver.Resolve(std, levels, TallyRateResolver.PriceLevel, "Retail", Today).Rate);
    }

    [Fact]
    public async Task Customers_are_filtered_by_group_including_subgroups()
    {
        var sim = new Sim(customers: 50);
        var ledgers = await All(new TallyMasterService(sim.Client()).GetLedgersAsync(["Sundry Debtors"], Today));
        Assert.Contains(ledgers, l => l.Name == "SONEPAR INDIA PRIVATE LIMITED"); // in sub-group Debtors - Outstation
        Assert.Contains(ledgers, l => l.Name == "CASH CUSTOMER");
        Assert.DoesNotContain(ledgers, l => l.Name == "ABC STEEL SUPPLIERS");
        Assert.DoesNotContain(ledgers, l => l.Name.StartsWith("Sales"));
    }

    [Fact]
    public async Task Parses_ledgers_in_both_layouts()
    {
        var sim = new Sim(customers: 20);
        var ledgers = await All(new TallyMasterService(sim.Client()).GetLedgersAsync(["Sundry Debtors"], Today));

        var sonepar = ledgers.Single(l => l.Name == "SONEPAR INDIA PRIVATE LIMITED");
        Assert.Equal("Sonepar India Private Limited", sonepar.MailingName);
        Assert.Equal(["Plot No. 12, MIDC Industrial Area", "Chakan, Pune"], sonepar.Address);
        Assert.Equal("Maharashtra", sonepar.State);
        Assert.Equal("410501", sonepar.Pincode);
        Assert.Equal("27AAACS1234F1Z3", sonepar.Gstin);
        Assert.Equal("Regular", sonepar.RegistrationType);
        Assert.Equal("purchase.pune@sonepar.example", sonepar.Email);
        Assert.Single(sonepar.ShipToAddresses);
        Assert.Equal("Karnataka", sonepar.ShipToAddresses[0].State);

        var chennai = ledgers.Single(l => l.Name == "SONEPAR INDIA PVT LTD - CHENNAI"); // legacy layout
        Assert.Equal("Tamil Nadu", chennai.State);
        Assert.Equal("33AAACS1234F1Z9", chennai.Gstin);
        Assert.Equal("600032", chennai.Pincode);
        Assert.Equal(2, chennai.Address.Count);
    }

    [Fact]
    public void Latest_mailing_details_are_used()
    {
        var xml = XElement.Parse("""
            <LEDGER NAME="X">
              <LEDMAILINGDETAILS.LIST><APPLICABLEFROM>20170701</APPLICABLEFROM><STATE>Karnataka</STATE><ADDRESS.LIST><ADDRESS>Old</ADDRESS></ADDRESS.LIST></LEDMAILINGDETAILS.LIST>
              <LEDMAILINGDETAILS.LIST><APPLICABLEFROM>20240401</APPLICABLEFROM><STATE>Kerala</STATE><ADDRESS.LIST><ADDRESS>New</ADDRESS></ADDRESS.LIST></LEDMAILINGDETAILS.LIST>
              <LEDMAILINGDETAILS.LIST><APPLICABLEFROM>20300401</APPLICABLEFROM><STATE>Goa</STATE><ADDRESS.LIST><ADDRESS>Future</ADDRESS></ADDRESS.LIST></LEDMAILINGDETAILS.LIST>
            </LEDGER>
            """);
        var l = TallyMasterParser.ParseLedger(xml, Today);
        Assert.Equal("Kerala", l.State);
        Assert.Equal(["New"], l.Address);
    }

    [Theory]
    [InlineData("187-901-10-UNIVERSAL BEVEL PROTRACTOR", "187-901-10")]
    [InlineData("2046S-DIAL INDICATOR 0-10MM", "2046S")]
    [InlineData("530-104-VERNIER CALIPER 0-150MM", "530-104")]
    [InlineData("10MM ALLEN KEY SET", "")]
    [InlineData("POLYCAB 3 CORE 2.5 SQMM FLEXIBLE CABLE", "")]
    [InlineData("HSS DRILL BIT 10MM", "")]
    public void Derives_part_numbers(string name, string expected) => Assert.Equal(expected, PartNumberHeuristics.FromName(name));

    [Fact]
    public async Task Incremental_filter_returns_only_changed_items()
    {
        var sim = new Sim();
        var max = sim.Engine.Data.MaxAlterId;
        sim.Engine.ChangeRate("530-104-VERNIER CALIPER 0-150MM", 3400m);
        var changed = await All(new TallyMasterService(sim.Client()).GetStockItemsAsync(max));
        Assert.Single(changed);
        Assert.Equal("530-104-VERNIER CALIPER 0-150MM", changed[0].Name);
    }

    [Fact]
    public void Customer_group_formula_escapes_names()
    {
        Assert.Equal("$$IsBelongsTo:\"Sundry Debtors\" OR $$IsBelongsTo:\"A \"\"B\"\"\"",
            TallyMasterService.GroupFormula(["Sundry Debtors", "A \"B\""]));
    }
}
