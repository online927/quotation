using System.Net;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;

namespace Quotation.Server.Tests;

/// <summary>Remembered descriptions, won/lost, appearance preferences, backups (health, export, restore) and client updates.</summary>
public class Round15Tests : QuotationTestBase
{
    private async Task<SaveQuotationRequest> BevelAsync(string description)
    {
        var customer = await Customer("SONEPAR INDIA PRIVATE LIMITED");
        var product = await Product("187-901-10-UNIVERSAL BEVEL PROTRACTOR");
        return new SaveQuotationRequest
        {
            Date = Today, CustomerId = customer.Id,
            Lines = [new QuotationLineDto { ProductId = product.Id, Quantity = 1, Rate = product.Rate ?? 1, TallyRate = product.Rate, Description = description }],
        };
    }

    [Fact]
    public async Task The_description_used_last_is_offered_next_time()
    {
        var before = (await Api.SearchProductsAsync("universal bevel")).First().Product;
        Assert.DoesNotContain("Calibrated", before.Description);
        await Api.CreateQuotationAsync(await BevelAsync("Brand: Mitutoyo\\nCalibrated, with certificate"));
        var after = (await Api.SearchProductsAsync("universal bevel")).First().Product;
        Assert.Equal("Brand: Mitutoyo\\nCalibrated, with certificate", after.Description);

        // An empty description does not erase it.
        await Api.CreateQuotationAsync(await BevelAsync(""));
        Assert.Equal("Brand: Mitutoyo\\nCalibrated, with certificate", (await Api.SearchProductsAsync("universal bevel")).First().Product.Description);
    }

    [Fact]
    public async Task Won_lost_and_follow_up_when_switched_on()
    {
        await TestServer.EnableProformasAsync(Api);
        var q = await Api.CreateQuotationAsync(await BevelAsync(""));
        var approved = await Api.ApproveQuotationAsync(q.Id);
        Assert.Null(approved.FollowUpDate); // tracking off
        var off = await Assert.ThrowsAsync<ApiException>(() => Api.SetOutcomeAsync(q.Id, new SetOutcomeRequest(QuotationOutcome.Won)));
        Assert.Equal(HttpStatusCode.Conflict, off.Status);

        var settings = await Api.SettingsAsync();
        settings.Quotation.TrackOutcomes = true;
        settings.Quotation.FollowUpDays = 0; // due today, so it shows in "To follow up"
        await Api.SaveSettingsAsync(settings);
        var second = await Api.ApproveQuotationAsync((await Api.CreateQuotationAsync(await BevelAsync(""))).Id);
        Assert.Equal(Today, second.FollowUpDate);
        Assert.Equal(QuotationOutcome.Open, second.Outcome);
        Assert.Equal(1, (await Api.DashboardAsync()).FollowUpDue);
        Assert.Single(await Api.QuotationsAsync(followUpDue: true));

        var lost = await Api.SetOutcomeAsync(second.Id, new SetOutcomeRequest(QuotationOutcome.Lost, "Price", second.FollowUpDate));
        Assert.Equal(QuotationOutcome.Lost, lost.Outcome);
        Assert.Equal("Price", lost.LostReason);
        Assert.Equal(0, (await Api.DashboardAsync()).FollowUpDue);
        Assert.Single(await Api.QuotationsAsync(outcome: QuotationOutcome.Lost));

        // Converting to a proforma invoice wins an open quotation.
        var third = await Api.ApproveQuotationAsync((await Api.CreateQuotationAsync(await BevelAsync(""))).Id);
        await Api.ConvertToProformaAsync(third.Id);
        Assert.Equal(QuotationOutcome.Won, (await Api.QuotationAsync(third.Id)).Outcome);
    }

    [Fact]
    public async Task Appearance_is_saved_with_the_login()
    {
        var me = await Api.SetMyPreferencesAsync(new MyPreferencesDto(Theme: AppTheme.GlassMaroon, TextSize: AppDisplay.Large, Spacing: AppDisplay.Compact, ReduceMotion: true));
        Assert.Equal((AppTheme.GlassMaroon, AppDisplay.Large, AppDisplay.Compact, true), (me.Theme, me.TextSize, me.Spacing, me.ReduceMotion));
        var again = await Server.LoginAsAdminAsync("OTHER-PC");
        Assert.Equal(AppTheme.GlassMaroon, again.CurrentUser!.Theme);
        await Assert.ThrowsAsync<ApiException>(() => Api.SetMyPreferencesAsync(new MyPreferencesDto(Theme: "Rainbow")));
    }

    [Fact]
    public async Task Health_warnings_second_copy_export_and_restore()
    {
        var health = await Api.HealthWarningsAsync();
        Assert.Contains(health, h => h.Text.StartsWith("No database backup"));
        Assert.Contains(health, h => h.Text.StartsWith("No second backup copy"));

        var copies = Path.Combine(Server.DataDirectory, "usb");
        var settings = await Api.SettingsAsync();
        settings.Backup.CopyFolder = copies;
        await Api.SaveSettingsAsync(settings);
        await Api.BackupNowAsync();
        Assert.Single(Directory.GetFiles(copies, "quotation-*.db"));
        health = await Api.HealthWarningsAsync();
        Assert.DoesNotContain(health, h => h.Text.Contains("backup"));

        // Export with one quotation, then change things, then restore: the quotation list is as it was.
        var kept = await Api.CreateQuotationAsync(await BevelAsync("kept"));
        using var export = new MemoryStream();
        var name = await Api.ExportEverythingAsync(export);
        Assert.EndsWith(".tsq", name);
        await Api.CreateQuotationAsync(await BevelAsync("made after the export"));
        Assert.Equal(2, (await Api.QuotationsAsync()).Count);

        export.Position = 0;
        var summary = await Api.RestoreFromBackupAsync(export);
        Assert.Equal(1, summary.Quotations);
        var fresh = await Server.LoginAsAdminAsync("AFTER-RESTORE");
        var list = await fresh.QuotationsAsync();
        Assert.Single(list);
        Assert.Equal(kept.Id, list[0].Id);
        Assert.Contains(Directory.GetFiles(Path.Combine(Server.DataDirectory, "backups")), f => Path.GetFileName(f).StartsWith("quotation-before-restore"));

        var wrong = await Assert.ThrowsAsync<ApiException>(() => fresh.RestoreFromBackupAsync(new MemoryStream("not a backup"u8.ToArray())));
        Assert.Contains("not a Quotations backup", wrong.Message);
    }

    [Fact]
    public async Task Server_hands_out_the_client_installer()
    {
        Assert.Null((await Api.ClientUpdateAsync()).Version); // no installer on this test server yet
        Directory.CreateDirectory(Server.ClientUpdateFolder);
        File.WriteAllBytes(Path.Combine(Server.ClientUpdateFolder, "TSQuotation-Setup.exe"), [1, 2, 3, 4]);
        File.WriteAllText(Path.Combine(Server.ClientUpdateFolder, "version.txt"), "0.9.77\r\n");
        var update = await Api.ClientUpdateAsync();
        Assert.Equal("0.9.77", update.Version);
        Assert.Equal(4, update.Size);
        Assert.False(update.Required);
        using var copy = new MemoryStream();
        await Api.DownloadClientAsync(copy);
        Assert.Equal([1, 2, 3, 4], copy.ToArray());
    }
}
