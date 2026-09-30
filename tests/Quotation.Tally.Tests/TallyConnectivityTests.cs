using System.Xml.Linq;

namespace Quotation.Tally.Tests;

public class TallyConnectivityTests
{
    [Fact]
    public void Request_builder_only_produces_export_requests()
    {
        var xml = TallyRequestBuilder.Collection(new TallyCollectionRequest
        {
            Name = "X",
            Type = "StockItem",
            NativeMethods = ["Name", "Parent"],
            Filters = new Dictionary<string, string> { ["F1"] = "$AlterID > 5" },
            Computes = new Dictionary<string, string> { ["C1"] = "##SVFromDate" },
        }, "T.SAIFUDDIN & CO.");
        var doc = XDocument.Parse(xml);
        Assert.Equal("Export", doc.Root!.Element("HEADER")!.Element("TALLYREQUEST")!.Value);
        Assert.Equal("T.SAIFUDDIN & CO.", doc.Descendants("SVCURRENTCOMPANY").Single().Value);
        Assert.Equal("$AlterID > 5", doc.Descendants("SYSTEM").Single().Value);
        Assert.Equal("F1", doc.Descendants("FILTER").Single().Value);
        Assert.Equal("C1 : ##SVFromDate", doc.Descendants("COMPUTE").Single().Value);
        Assert.DoesNotContain("Import", xml);
    }

    [Fact]
    public async Task Client_refuses_non_export_requests()
    {
        var sim = new Sim();
        var client = sim.Client();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ExportAsync("<ENVELOPE><HEADER><TALLYREQUEST>Import</TALLYREQUEST></HEADER></ENVELOPE>"));
        Assert.Equal(0, sim.Engine.RequestCount);
    }

    [Fact]
    public async Task Detects_company_and_active_financial_year()
    {
        var sim = new Sim(today: new DateOnly(2026, 9, 30));
        var info = await new TallyCompanyService(sim.Client()).GetCompanyInfoAsync();
        Assert.Equal("T.SAIFUDDIN & CO.", info.Company.Name);
        Assert.Equal(new DateOnly(2026, 4, 1), info.PeriodFrom);
        Assert.Equal(2026, info.ActiveFinancialYearStart);
        Assert.Contains("T.SAIFUDDIN & CO.", info.LoadedCompanies);
        Assert.True(info.Company.MaxMasterAlterId > 0);
    }

    [Fact]
    public async Task Active_financial_year_follows_tally_period_not_system_date()
    {
        var sim = new Sim(today: new DateOnly(2026, 9, 30));
        // User has selected the previous year's period in Tally (Alt+F2).
        sim.Engine.Data.PeriodFrom = new DateOnly(2025, 4, 1);
        sim.Engine.Data.PeriodTo = new DateOnly(2026, 3, 31);
        var info = await new TallyCompanyService(sim.Client()).GetCompanyInfoAsync();
        Assert.Equal(2025, info.ActiveFinancialYearStart);
    }

    [Fact]
    public async Task Named_company_that_is_not_open_gives_clear_error()
    {
        var sim = new Sim();
        var ex = await Assert.ThrowsAsync<TallyException>(() =>
            new TallyCompanyService(sim.Client("OTHER COMPANY")).GetCompanyInfoAsync());
        Assert.Contains("OTHER COMPANY", ex.Message);
    }

    [Fact]
    public async Task No_company_open_gives_clear_error()
    {
        var sim = new Sim();
        sim.Engine.CompanyOpen = false;
        var ex = await Assert.ThrowsAsync<TallyException>(() => new TallyCompanyService(sim.Client()).GetCompanyInfoAsync());
        Assert.Contains("company", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Offline_tally_raises_unavailable()
    {
        var sim = new Sim();
        sim.Engine.Online = false;
        var ex = await Assert.ThrowsAsync<TallyUnavailableException>(() => new TallyCompanyService(sim.Client()).GetCompanyInfoAsync());
        Assert.Contains("ConnectionRefused", ex.Message);
    }

    [Fact]
    public async Task Slow_tally_times_out()
    {
        var sim = new Sim();
        sim.Handler.Latency = TimeSpan.FromSeconds(5);
        var client = new TallyXmlClient(new HttpClient(sim.Handler, false),
            new TallyConnectionOptions("http://tally-sim:9000", "", TimeSpan.FromMilliseconds(200)));
        await Assert.ThrowsAsync<TallyUnavailableException>(() => new TallyCompanyService(client).GetLoadedCompaniesAsync());
    }

    [Fact]
    public async Task Streams_objects_with_invalid_xml_character_references()
    {
        var sim = new Sim(products: 50);
        var request = TallyRequestBuilder.Collection(new TallyCollectionRequest { Name = "Items", Type = "StockItem" }, null);
        var count = 0;
        await foreach (var item in sim.Client().StreamObjectsAsync(request, "STOCKITEM"))
        {
            Assert.Equal("Applicable", TallyText.Text(item, "GSTAPPLICABLE"));
            count++;
        }
        Assert.Equal(sim.Engine.Data.StockItems.Count, count);
    }

    [Fact]
    public void Financial_year_detection_rules()
    {
        Assert.Equal(2025, TallyCompanyService.DetectFinancialYear(new DateOnly(2025, 4, 1), null, null, 4));
        Assert.Equal(2025, TallyCompanyService.DetectFinancialYear(null, new DateOnly(2026, 3, 31), null, 4));
        Assert.Equal(2026, TallyCompanyService.DetectFinancialYear(null, new DateOnly(2026, 4, 1), null, 4));
        Assert.Null(TallyCompanyService.DetectFinancialYear(null, null, null, 4));
        // Company FY start month overrides the configured month.
        Assert.Equal(2026, TallyCompanyService.DetectFinancialYear(new DateOnly(2026, 1, 15), null, new DateOnly(2010, 1, 1), 4));
    }
}
