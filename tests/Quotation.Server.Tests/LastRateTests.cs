using Microsoft.Extensions.DependencyInjection;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Server.Services;

namespace Quotation.Server.Tests;

/// <summary>Items without a Tally selling price get the rate they were last quoted/sold at.</summary>
public class LastRateTests : QuotationTestBase
{
    private const string Level = "-Rsk Spirit Level 200Mm";
    private const string VoucherRequest = "Vouchers : VoucherType";

    private LastRateService Service => Server.Services.GetRequiredService<LastRateService>();
    private int VoucherRequests => Server.Tally.RequestLog.Count(t => t == VoucherRequest);

    private async Task<ProductSummaryDto> WaitForRate(string name, decimal? rate)
    {
        ProductSummaryDto p = null!;
        for (var i = 0; i < 100; i++)
        {
            p = await Product(name);
            if (p.Rate == rate) return p;
            await Task.Delay(50); // the search index is rebuilt in the background
        }
        return p;
    }

    [Fact]
    public async Task Normal_syncs_never_read_all_vouchers_and_say_how_to_read_last_rates()
    {
        // Initial full sync (base class) and further syncs read masters only.
        var full = await Api.StartSyncAsync(SyncKind.Full, wait: true);
        Assert.Contains(LastRateService.NotReadYetMessage, full!.Message);
        Server.Tally.Data.AddVoucher("TS Quotation", Today, "TSQ-3420", "SONEPAR INDIA PRIVATE LIMITED", Level, 11500m);
        await Api.StartSyncAsync(SyncKind.Incremental, wait: true);
        Assert.Equal(0, VoucherRequests);
        Assert.Null((await Product(Level)).Rate);
    }

    [Fact]
    public async Task Read_last_rates_fills_items_without_a_tally_price_week_by_week_per_type()
    {
        var run = await Api.StartSyncAsync(SyncKind.LastRates, wait: true);
        Assert.Equal(SyncStatus.Succeeded, run!.Status);
        Assert.Contains("Last rates: 2 item(s)", run.Message);

        // Two base types (Quotation, Sales) × weekly pieces of the 12-month look-back; each through the voucher-type index.
        Assert.InRange(VoucherRequests, 2 * 50, 2 * 62);

        var level = await WaitForRate(Level, 10500m);
        Assert.Equal(10500m, level.Rate);
        Assert.Contains("TS Quotation TSQ-3403", level.RateNote);
        Assert.Contains("BHARAT PRECISION ENGINEERING", level.RateNote);
        Assert.Null(Server.Db(db => db.Products.Single(p => p.Name == Level).Rate)); // master price stays empty

        var detail = await Api.ProductAsync(level.Id);
        Assert.Equal(10500m, detail.Rate);
        Assert.StartsWith("Last rate: TS Quotation TSQ-3403", detail.RateSource);

        // A Tally selling price always wins over an earlier quoted rate.
        var bevel = await Product("187-901-10-UNIVERSAL BEVEL PROTRACTOR");
        Assert.Equal(15600m, bevel.Rate);
        Assert.Equal("", bevel.RateNote);
        // Never quoted or sold: still no rate.
        Assert.Null((await Product("112-201-POINT MICROMETER")).Rate);
    }

    [Fact]
    public async Task After_reading_new_tally_quotations_are_picked_up_by_normal_syncs()
    {
        await Api.StartSyncAsync(SyncKind.LastRates, wait: true);
        Server.Tally.Data.AddVoucher("TS Quotation", Today, "TSQ-3411", "SONEPAR INDIA PRIVATE LIMITED", Level, 11000m);
        Server.Tally.Data.AddVoucher("TS Quotation", Today, "TSQ-3412", "SONEPAR INDIA PRIVATE LIMITED", Level, 99m, unit: "BOX"); // other unit: ignored

        var before = VoucherRequests;
        var run = await Api.StartSyncAsync(SyncKind.Incremental, wait: true);
        Assert.Equal(SyncStatus.Skipped, run!.Status); // no master changed
        Assert.Contains("Last rates: 1 item(s) updated", run.Message);
        Assert.Equal(before + 2, VoucherRequests); // one small request per base type
        Assert.Contains("TSQ-3411", (await WaitForRate(Level, 11000m)).RateNote);

        // Nothing new in Tally: vouchers are not read again.
        before = VoucherRequests;
        await Api.StartSyncAsync(SyncKind.Incremental, wait: true);
        Assert.Equal(before, VoucherRequests);
    }

    [Fact]
    public async Task Slow_tally_stops_reading_keeps_rates_and_pauses_automatic_updates()
    {
        await Api.StartSyncAsync(SyncKind.LastRates, wait: true);
        Service.RequestTimeout = TimeSpan.FromMilliseconds(300);
        Server.Tally.ResponseDelay = TimeSpan.FromSeconds(1);
        try
        {
            var before = VoucherRequests;
            var run = await Api.StartSyncAsync(SyncKind.LastRates, wait: true);
            Assert.Equal(SyncStatus.Failed, run!.Status);
            Assert.Contains("Reading last rates stopped", run.Message);
            Assert.True(VoucherRequests - before <= 1); // stops at the first slow answer
            Assert.Equal(10500m, (await Product(Level)).Rate); // previous rates kept

            // A daytime update that meets a slow Tally pauses automatic reading.
            Server.Tally.Data.AddVoucher("TS Quotation", Today, "TSQ-3413", "SONEPAR INDIA PRIVATE LIMITED", Level, 11100m);
            var sync = await Api.StartSyncAsync(SyncKind.Incremental, wait: true);
            Assert.Contains("Last rate updates paused", sync!.Message);
        }
        finally
        {
            Server.Tally.ResponseDelay = TimeSpan.Zero;
        }
        var paused = VoucherRequests;
        Server.Tally.Data.AddVoucher("TS Quotation", Today, "TSQ-3414", "SONEPAR INDIA PRIVATE LIMITED", Level, 11200m);
        await Api.StartSyncAsync(SyncKind.Incremental, wait: true);
        Assert.Equal(paused, VoucherRequests);
    }

    [Fact]
    public async Task Tally_that_ignores_the_period_is_asked_once_per_type()
    {
        Server.Tally.IgnorePeriod = true;
        var run = await Api.StartSyncAsync(SyncKind.LastRates, wait: true);
        Assert.Equal(SyncStatus.Succeeded, run!.Status);
        Assert.Equal(2, VoucherRequests);
        Assert.Equal(10500m, (await WaitForRate(Level, 10500m)).Rate); // the voucher outside the look-back is still ignored
    }

    [Fact]
    public async Task Reading_can_be_stopped()
    {
        Server.Tally.ResponseDelay = TimeSpan.FromMilliseconds(150);
        try
        {
            var reading = Api.StartSyncAsync(SyncKind.LastRates, wait: true);
            for (var i = 0; i < 200 && VoucherRequests < 2; i++) await Task.Delay(25);
            await Api.StopLastRatesAsync();
            var run = await reading;
            Assert.Equal(SyncStatus.Failed, run!.Status);
            Assert.StartsWith("Stopped", run.Message);
            Assert.True(VoucherRequests < 20);
        }
        finally
        {
            Server.Tally.ResponseDelay = TimeSpan.Zero;
        }
    }

    [Fact]
    public async Task Rate_approved_in_this_app_becomes_the_last_rate_and_lines_start_from_it()
    {
        await Api.StartSyncAsync(SyncKind.LastRates, wait: true);
        var customer = await Customer("SONEPAR INDIA PRIVATE LIMITED");
        var level = await WaitForRate(Level, 10500m);
        var q = await Api.CreateQuotationAsync(new SaveQuotationRequest
        {
            Date = Today,
            CustomerId = customer.Id,
            Lines = [new QuotationLineDto { ProductId = level.Id, Quantity = 1, Rate = 11250m, TallyRate = level.Rate }],
        });
        Assert.Equal(10500m, q.Lines[0].TallyRate); // suggested from the last rate
        var approved = await Api.ApproveQuotationAsync(q.Id);
        Assert.Equal(QuotationStatus.Generated, approved.Status);

        var after = await WaitForRate(Level, 11250m);
        Assert.Contains($"Quotation {approved.Number} (this app)", after.RateNote);

        // A complete re-read keeps it (app quotations are not in Tally).
        await Api.StartSyncAsync(SyncKind.LastRates, wait: true);
        Assert.Equal(11250m, (await WaitForRate(Level, 11250m)).Rate);
    }

    [Fact]
    public async Task Last_rate_can_be_switched_off()
    {
        await Api.StartSyncAsync(SyncKind.LastRates, wait: true);
        var s = await Api.SettingsAsync();
        s.Tally.UseLastRate = false;
        await Api.SaveSettingsAsync(s);
        await Api.StartSyncAsync(SyncKind.Incremental, wait: true);
        Assert.Null((await WaitForRate(Level, null)).Rate);
    }
}
