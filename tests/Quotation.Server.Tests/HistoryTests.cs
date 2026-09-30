using System.Net;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;

namespace Quotation.Server.Tests;

public class HistoryTests : QuotationTestBase
{
    [Fact]
    public async Task Global_search_by_number_customer_product_gstin_reference_and_date()
    {
        var r = await BevelRequest();
        r.BuyersReference = "ENQ-7781";
        var q = await Api.CreateQuotationAsync(r);
        var today = Today;

        foreach (var term in new[] { q.Number!, "sonepar", "bevel protractor", "27AAACS1234F1Z3", "ENQ-7781", today.ToString("dd-MM-yyyy"), today.ToString("d-MMM-yyyy") })
        {
            Assert.Contains(await Api.QuotationsAsync(term), s => s.Id == q.Id);
        }
        Assert.DoesNotContain(await Api.QuotationsAsync(today.AddDays(-3).ToString("dd-MM-yyyy")), s => s.Id == q.Id);
        Assert.DoesNotContain(await Api.QuotationsAsync("sonepar havells"), s => s.Id == q.Id);
    }

    [Fact]
    public async Task Recent_customers_and_products_for_the_user()
    {
        await Api.CreateQuotationAsync(await BevelRequest());
        var recent = await Api.RecentItemsAsync();
        Assert.Equal("Sonepar India Private Limited", Assert.Single(recent.Customers).Name);
        Assert.Equal("187-901-10-UNIVERSAL BEVEL PROTRACTOR", Assert.Single(recent.Products).Name);

        var admin = await Api.MeAsync();
        await Api.CreateUserAsync(new CreateUserRequest("other", "Other", "other123", UserRole.User));
        var other = Server.CreateApi();
        await other.LoginAsync("other", "other123");
        Assert.Empty((await other.RecentItemsAsync()).Customers); // per user
    }

    [Fact]
    public async Task Quotation_audit_trail_is_visible_to_users()
    {
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        await Api.ApproveQuotationAsync(q.Id);
        await Api.CreateUserAsync(new CreateUserRequest("clerk2", "Clerk", "clerk123", UserRole.User));
        var clerk = Server.CreateApi();
        await clerk.LoginAsync("clerk2", "clerk123");
        var trail = await clerk.QuotationAuditAsync(q.Id);
        Assert.Contains(trail, a => a.Action == "QuotationCreated");
        Assert.Contains(trail, a => a.Action == "PdfGenerated");
    }

    [Fact]
    public async Task Diagnostics_are_admin_only_and_logs_are_redacted()
    {
        var info = await Api.DiagnosticsAsync();
        Assert.True(info.Products > 0);
        Assert.Equal(Server.DataDirectory, info.DataDirectory);
        Assert.True(info.DatabaseBytes > 0);

        var log = Path.Combine(Server.DataDirectory, "logs", "server-99990101.log");
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        await File.WriteAllTextAsync(log, "line1\nkey sk-ant-api03-SECRETSECRETSECRET\n");
        var text = await Api.ServerLogAsync();
        Assert.DoesNotContain("SECRETSECRET", text);

        await Api.CreateUserAsync(new CreateUserRequest("clerk3", "Clerk", "clerk123", UserRole.User));
        var clerk = Server.CreateApi();
        await clerk.LoginAsync("clerk3", "clerk123");
        var ex = await Assert.ThrowsAsync<ApiException>(() => clerk.DiagnosticsAsync());
        Assert.Equal(HttpStatusCode.Forbidden, ex.Status);
    }
}

public class BackupTests : QuotationTestBase
{
    [Fact]
    public async Task Online_backup_contains_the_quotations()
    {
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        var path = await Api.BackupNowAsync();
        Assert.True(File.Exists(path));
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Number FROM Quotations";
        Assert.Equal(q.Number, cmd.ExecuteScalar());
    }
}
