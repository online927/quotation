using Quotation.Contracts;
using Quotation.Core.Domain;

namespace Quotation.Server.Tests;

/// <summary>Items without a Tally selling price get the rate they were last quoted/sold at.</summary>
public class LastRateTests : QuotationTestBase
{
    private const string Level = "-Rsk Spirit Level 200Mm";

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
    public async Task Item_without_tally_price_gets_its_last_quoted_rate()
    {
        var level = await Product(Level);
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
    public async Task New_tally_quotation_updates_the_last_rate_even_when_masters_did_not_change()
    {
        Server.Tally.Data.AddVoucher("TS Quotation", Today, "TSQ-3411", "SONEPAR INDIA PRIVATE LIMITED", Level, 11000m);
        Server.Tally.Data.AddVoucher("TS Quotation", Today, "TSQ-3412", "SONEPAR INDIA PRIVATE LIMITED", Level, 99m, unit: "BOX"); // other unit: ignored

        var run = await Api.StartSyncAsync(SyncKind.Incremental, wait: true);
        Assert.Equal(SyncStatus.Skipped, run!.Status); // no master changed
        Assert.Contains("Last rates: 1 item(s) updated", run.Message);

        var level = await WaitForRate(Level, 11000m);
        Assert.Equal(11000m, level.Rate);
        Assert.Contains("TSQ-3411", level.RateNote);

        // Nothing new in Tally: vouchers are not read again.
        var before = Server.Tally.RequestLog.Count(t => t == "Voucher");
        await Api.StartSyncAsync(SyncKind.Incremental, wait: true);
        Assert.Equal(before, Server.Tally.RequestLog.Count(t => t == "Voucher"));
    }

    [Fact]
    public async Task Rate_approved_in_this_app_becomes_the_last_rate_and_lines_start_from_it()
    {
        var customer = await Customer("SONEPAR INDIA PRIVATE LIMITED");
        var level = await Product(Level);
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
        Assert.Equal(11250m, after.Rate);
        Assert.Contains($"Quotation {approved.Number} (this app)", after.RateNote);

        // A full sync keeps it (app quotations are not in Tally).
        await Api.StartSyncAsync(SyncKind.Full, wait: true);
        Assert.Equal(11250m, (await WaitForRate(Level, 11250m)).Rate);
    }

    [Fact]
    public async Task Last_rate_can_be_switched_off()
    {
        var s = await Api.SettingsAsync();
        s.Tally.UseLastRate = false;
        await Api.SaveSettingsAsync(s);
        await Api.StartSyncAsync(SyncKind.Incremental, wait: true);
        Assert.Null((await WaitForRate(Level, null)).Rate);
    }
}
