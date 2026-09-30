using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Quotation.Core.Domain;
using Quotation.Tally;
using Xunit.Abstractions;

namespace Quotation.Server.Tests;

public class SyncTests(ITestOutputHelper output) : IDisposable
{
    private readonly TestServer _server = new(products: 400, customers: 150, today: new DateOnly(2026, 9, 30));

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task Full_sync_imports_products_and_customers_with_tally_values()
    {
        var api = await _server.LoginAsAdminAsync();
        var run = await api.StartSyncAsync(SyncKind.Full, wait: true);
        Assert.Equal(SyncStatus.Succeeded, run!.Status);

        var sim = _server.Tally.Data;
        Assert.Equal(sim.StockItems.Count, _server.Db(db => db.Products.Count(p => !p.IsDeleted)));
        var expectedCustomers = sim.Ledgers.Count(l => l.Parent.Contains("Debtors"));
        Assert.Equal(expectedCustomers, _server.Db(db => db.Customers.Count()));

        var bevel = _server.Db(db => db.Products.Single(p => p.Name == "187-901-10-UNIVERSAL BEVEL PROTRACTOR"));
        Assert.Equal("90172020", bevel.Hsn);
        Assert.Equal(18m, bevel.GstRate);
        Assert.Equal(15600m, bevel.Rate);
        Assert.Equal("NOS", bevel.Unit);
        Assert.Equal("187-901-10", bevel.PartNumber);
        Assert.Equal("Mitutoyo", bevel.Brand);
        Assert.Equal("Standard selling price", bevel.RateSource);

        var drill = _server.Db(db => db.Products.Single(p => p.Name == "HSS DRILL BIT 10MM"));
        Assert.Equal("82075000", drill.Hsn);
        Assert.True(drill.GstInherited);

        var endMill = _server.Db(db => db.Products.Single(p => p.Name == "CARBIDE END MILL 10MM 4 FLUTE"));
        Assert.Null(endMill.Rate); // no standard price in Tally → user must enter the rate

        var sonepar = _server.Db(db => db.Customers.Single(c => c.Name == "SONEPAR INDIA PRIVATE LIMITED"));
        Assert.Equal("27", sonepar.StateCode);
        Assert.Equal("Maharashtra", sonepar.StateName);
        Assert.Equal("AAACS1234F", sonepar.Pan);
        Assert.Contains("Chakan, Pune", sonepar.Address);
        Assert.Contains("Bangalore Warehouse", sonepar.ShipToJson);

        var status = await api.StatusAsync();
        Assert.NotNull(status.LastSuccessfulSyncUtc);
        Assert.False(status.DataIsStale);
        Assert.Equal(sim.StockItems.Count, status.ProductCount);
    }

    [Fact]
    public async Task Incremental_sync_only_fetches_changes()
    {
        var api = await _server.LoginAsAdminAsync();
        await api.StartSyncAsync(SyncKind.Full, wait: true);

        var noChange = await api.StartSyncAsync(SyncKind.Incremental, wait: true);
        Assert.Equal(SyncStatus.Skipped, noChange!.Status);

        _server.Tally.ChangeRate("187-901-10-UNIVERSAL BEVEL PROTRACTOR", 16200m, new DateOnly(2026, 9, 1));
        _server.Tally.AddStockItem("NEW ITEM 123", "Hand Tools", "NOS", "82055190", 18, 999m);
        _server.Tally.DeleteStockItem("530-104-VERNIER CALIPER 0-150MM");
        _server.Tally.RenameLedger("BHARAT PRECISION ENGINEERING", "BHARAT PRECISION ENGINEERING PVT LTD");

        var run = await api.StartSyncAsync(SyncKind.Incremental, wait: true);
        Assert.Equal(SyncStatus.Succeeded, run!.Status);
        Assert.Equal(SyncKind.Incremental, run.Kind);
        Assert.Equal(2, run.ProductsChanged);
        Assert.Equal(1, run.ProductsDeleted);
        Assert.Equal(1, run.CustomersChanged);

        Assert.Equal(16200m, _server.Db(db => db.Products.Single(p => p.Name == "187-901-10-UNIVERSAL BEVEL PROTRACTOR").Rate));
        Assert.True(_server.Db(db => db.Products.Single(p => p.Name == "530-104-VERNIER CALIPER 0-150MM").IsDeleted));
        Assert.Equal(999m, _server.Db(db => db.Products.Single(p => p.Name == "NEW ITEM 123").Rate));
        Assert.True(_server.Db(db => db.Customers.Any(c => c.Name == "BHARAT PRECISION ENGINEERING PVT LTD")));
        Assert.Equal(1, _server.Db(db => db.Customers.Count(c => c.Name.StartsWith("BHARAT PRECISION"))));
    }

    [Fact]
    public async Task Changing_customer_groups_forces_full_sync()
    {
        var api = await _server.LoginAsAdminAsync();
        await api.StartSyncAsync(SyncKind.Full, wait: true);
        var s = await api.SettingsAsync();
        s.Tally.CustomerGroups = ["Debtors - Bangalore"];
        await api.SaveSettingsAsync(s);

        var run = await api.StartSyncAsync(SyncKind.Incremental, wait: true);
        Assert.Equal(SyncKind.Full, run!.Kind);
        Assert.True(_server.Db(db => db.Customers.Single(c => c.Name == "SONEPAR INDIA PRIVATE LIMITED").IsDeleted));
        Assert.False(_server.Db(db => db.Customers.Single(c => c.Name == "BHARAT PRECISION ENGINEERING").IsDeleted));
    }

    [Fact]
    public async Task Failed_sync_keeps_existing_data()
    {
        var api = await _server.LoginAsAdminAsync();
        await api.StartSyncAsync(SyncKind.Full, wait: true);
        var before = _server.Db(db => db.Products.Count(p => !p.IsDeleted));

        _server.Tally.Online = false;
        var run = await api.StartSyncAsync(SyncKind.Full, wait: true);
        Assert.Equal(SyncStatus.Failed, run!.Status);
        Assert.Contains("Cannot connect", run.Message);
        Assert.Equal(before, _server.Db(db => db.Products.Count(p => !p.IsDeleted)));

        var status = await api.StatusAsync();
        Assert.False(status.TallyConnected);
        Assert.NotNull(status.LastSuccessfulSyncUtc); // previous good sync still reported
    }

    [Fact]
    public async Task Interrupted_sync_is_recovered()
    {
        _server.Db(db =>
        {
            db.SyncRuns.Add(new Quotation.Data.Entities.SyncRun { Kind = SyncKind.Full, Status = SyncStatus.Running, StartedUtc = DateTime.UtcNow });
            return db.SaveChanges();
        });
        await _server.Db(db => Quotation.Server.Services.TallySyncService.RecoverInterruptedAsync(db, DateTime.UtcNow, CancellationToken.None));
        Assert.Equal(SyncStatus.Failed, _server.Db(db => db.SyncRuns.Single().Status));
    }

    [Fact]
    public async Task Concurrent_sync_requests_are_rejected()
    {
        var api = await _server.LoginAsAdminAsync();
        var sync = (Quotation.Server.Services.TallySyncService)_server.Services.GetService(typeof(Quotation.Server.Services.TallySyncService))!;
        _server.Tally.ResponseDelay = TimeSpan.FromMilliseconds(300);
        var first = sync.RunAsync(SyncKind.Full, "t1", CancellationToken.None);
        var second = await Record.ExceptionAsync(() => sync.RunAsync(SyncKind.Full, "t2", CancellationToken.None));
        await first;
        Assert.IsType<Quotation.Server.Services.SyncAlreadyRunningException>(second);
    }

    [Fact]
    public async Task Raw_tally_data_is_kept_for_offline_resolution()
    {
        var api = await _server.LoginAsAdminAsync();
        await api.StartSyncAsync(SyncKind.Full, wait: true);
        var json = _server.Db(db => db.Products.Single(p => p.Name == "187-901-10-UNIVERSAL BEVEL PROTRACTOR").TallyDataJson);
        var data = JsonSerializer.Deserialize<Quotation.Server.Services.ProductTallyData>(json, Quotation.Server.Services.TallySyncService.Json)!;
        Assert.Equal(2, data.StandardPrices.Count);
        Assert.Equal(15600m, TallyRateResolver.Resolve(data.StandardPrices, data.PriceLevels, "StandardPrice", "", new DateOnly(2026, 9, 30)).Rate);
    }

    [Fact]
    public async Task Sample_endpoint_returns_raw_xml()
    {
        var api = await _server.LoginAsAdminAsync();
        var xml = await api.TallySampleAsync("StockItem", "187-901-10-UNIVERSAL BEVEL PROTRACTOR");
        Assert.Contains("90172020", xml);
        Assert.DoesNotContain("530-104", xml);
    }
}

public class SyncPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Full_sync_of_20000_products_and_3000_customers()
    {
        using var server = new TestServer(products: 20000, customers: 3000, today: new DateOnly(2026, 9, 30));
        var api = await server.LoginAsAdminAsync();
        var sw = Stopwatch.StartNew();
        var run = await api.StartSyncAsync(SyncKind.Full, wait: true);
        var full = sw.Elapsed;
        Assert.Equal(SyncStatus.Succeeded, run!.Status);
        Assert.Equal(20000, server.Db(db => db.Products.Count()));

        server.Tally.ChangeRate("187-901-10-UNIVERSAL BEVEL PROTRACTOR", 16000m);
        sw.Restart();
        var inc = await api.StartSyncAsync(SyncKind.Incremental, wait: true);
        var incremental = sw.Elapsed;
        Assert.Equal(1, inc!.ProductsChanged);

        output.WriteLine($"Full sync: {full.TotalSeconds:0.0}s, incremental: {incremental.TotalSeconds:0.0}s");
        Assert.True(full < TimeSpan.FromSeconds(120), $"Full sync took {full}");
        Assert.True(incremental < full, "Incremental should be faster than full");
    }
}
