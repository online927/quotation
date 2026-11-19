using Microsoft.Extensions.DependencyInjection;
using Quotation.Contracts;

namespace Quotation.Server.Tests;

/// <summary>Round 33: starred items and customers (per user, first in the pick lists, administrator switch).</summary>
public class Round33Tests : QuotationTestBase
{
    [Fact]
    public async Task Starred_items_and_customers_come_first_in_that_users_pick_lists_only()
    {
        var ravi = await Server.LoginAsStandardAsync();
        var hits = await ravi.SearchProductsAsync("caliper", 20);
        Assert.True(hits.Count >= 4);
        Assert.All(hits, h => Assert.False(h.IsStarred));
        var low = hits[^1].Product;

        var stars = await ravi.SetStarAsync("product", low.Id, true);
        Assert.Equal([low.Id], stars.ProductIds);
        Assert.True(stars.Enabled);

        // The pick list (starredFirst) puts it on top; the automatic matching keeps the best match first.
        var picked = await ravi.SearchProductsAsync("caliper", 20, starredFirst: true);
        Assert.Equal(low.Id, picked[0].Product.Id);
        Assert.True(picked[0].IsStarred);
        var matched = await ravi.SearchProductsAsync("caliper", 20);
        Assert.Equal(hits[0].Product.Id, matched[0].Product.Id);
        Assert.True(matched.Single(h => h.Product.Id == low.Id).IsStarred);

        // Nothing typed: the starred ones first, each only once.
        var empty = await ravi.SearchProductsAsync("", 20, starredFirst: true);
        Assert.Equal(low.Id, empty[0].Product.Id);
        Assert.Single(empty, h => h.Product.Id == low.Id);

        // Each user has their own stars.
        var admin = await Api.SearchProductsAsync("caliper", 20, starredFirst: true);
        Assert.All(admin, h => Assert.False(h.IsStarred));

        // Customers the same way.
        var customers = await ravi.SearchCustomersAsync("a", 20);
        var lastCustomer = customers[^1].Customer;
        await ravi.SetStarAsync("customer", lastCustomer.Id, true);
        Assert.Equal(lastCustomer.Id, (await ravi.SearchCustomersAsync("a", 20, starredFirst: true))[0].Customer.Id);

        // Unstar.
        stars = await ravi.SetStarAsync("product", low.Id, false);
        Assert.Empty(stars.ProductIds);
        Assert.Equal([lastCustomer.Id], (await ravi.StarsAsync()).CustomerIds);
    }

    [Fact]
    public async Task The_administrator_can_switch_stars_off()
    {
        var all = await Api.SettingsAsync();
        Assert.True(all.Quotation.Stars);
        var ravi = await Server.LoginAsStandardAsync();
        var hits = await ravi.SearchProductsAsync("caliper", 20);
        await ravi.SetStarAsync("product", hits[^1].Product.Id, true);

        all.Quotation.Stars = false;
        await Api.SaveSettingsAsync(all);
        Assert.False((await ravi.SettingsAsync()).Quotation.Stars);
        var off = await ravi.SearchProductsAsync("caliper", 20, starredFirst: true);
        Assert.Equal(hits[0].Product.Id, off[0].Product.Id);
        Assert.All(off, h => Assert.False(h.IsStarred));
        Assert.False((await ravi.StarsAsync()).Enabled);
    }

    [Fact]
    public async Task Backups_and_approved_pdfs_are_copied_to_google_drive_once()
    {
        // Not connected: nothing happens, and the card says what to do.
        var status = await Api.DriveBackupNowAsync();
        Assert.False(status.Connected);
        Assert.Contains("Connect the Google account", status.Error);

        Server.Drive = new TestSupport.FakeDrive();
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        var approved = await Api.ApproveQuotationAsync(q.Id);
        Assert.NotNull(approved.Number);

        status = await Api.DriveBackupAsync();
        Assert.True(status.Connected);
        Assert.Equal(1, status.PdfsWaiting);

        status = await Api.DriveBackupNowAsync();
        Assert.Null(status.Error);
        Assert.NotNull(status.LastCopyLocal);
        Assert.Matches(@"^database \d+(\.\d)? (KB|MB) \+ 1 PDF$", status.LastCopyText);
        Assert.Equal(0, status.PdfsWaiting);
        var paths = Server.Drive.Paths();
        Assert.Equal(2, paths.Count);
        Assert.Matches(@"^TS Quotations/Database backups/quotation-\d{8}-\d{6}\.db$", paths[1]);
        var month = Today.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith($"TS Quotations/Approved PDFs/{month}/", paths[0]);
        Assert.EndsWith(" - SONEPAR INDIA PRIVATE LIMITED.pdf", paths[0], StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(Server.Drive.Items.Values.Single(i => i.Name.EndsWith(".pdf")).Content, 0, 4));

        // Nothing new: nothing is copied again (the automatic copy every 15 minutes does no work).
        var uploads = Server.Drive.Uploads;
        using (var scope = Server.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Services.DriveBackupService>().CopyNowAsync("system", makeBackup: false, default);
        Assert.Equal(uploads, Server.Drive.Uploads);

        // Ticks off: no copies.
        var all = await Api.SettingsAsync();
        all.Backup.DriveBackups = false;
        all.Backup.DrivePdfs = false;
        await Api.SaveSettingsAsync(all);
        await Api.DriveBackupNowAsync();
        Assert.Equal(uploads, Server.Drive.Uploads);

        // Drive refuses (old sign-in without Drive): the card says to connect again.
        all.Backup.DriveBackups = true;
        await Api.SaveSettingsAsync(all);
        await Task.Delay(1100); // a new backup file name (one per second)
        Server.Drive.FailWith = new Google.GoogleApiException("drive", "Request had insufficient authentication scopes.") { HttpStatusCode = System.Net.HttpStatusCode.Forbidden };
        status = await Api.DriveBackupNowAsync();
        Assert.Contains("connect the Google account again", status.Error);
    }
}
