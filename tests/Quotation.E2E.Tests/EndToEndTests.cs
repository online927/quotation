using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;
using UglyToad.PdfPig;
using Xunit.Abstractions;

namespace Quotation.E2E.Tests;

/// <summary>
/// Full system test with real processes and real HTTP: Tally simulator process + Quotation Server process
/// (background workers enabled) + API client acting as a desktop PC.
/// </summary>
public class EndToEndTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "tsq-e2e", Guid.NewGuid().ToString("N"));
    private readonly List<ProcessHost> _processes = [];

    public void Dispose()
    {
        foreach (var p in _processes) p.Dispose();
        try { Directory.Delete(_dataDir, true); } catch { }
    }

    private ProcessHost StartTally(int port)
    {
        var p = new ProcessHost(ProcessHost.BuiltDll("tools/Tally.Simulator", "TallySimulator.dll"), $"--port {port} --products 3000 --customers 400");
        _processes.Add(p);
        return p;
    }

    private ProcessHost StartServer(int port)
    {
        var p = new ProcessHost(ProcessHost.BuiltDll("src/Quotation.Server", "TSQuotation.Server.dll"), "", new Dictionary<string, string>
        {
            ["Urls"] = $"http://127.0.0.1:{port}",
            ["Server__DataDirectory"] = _dataDir,
            ["Server__BootstrapAdminPassword"] = "first-admin",
            ["DOTNET_ENVIRONMENT"] = "Production",
        });
        _processes.Add(p);
        return p;
    }

    [Fact]
    public async Task Complete_workflow_with_restart_and_outage()
    {
        Directory.CreateDirectory(_dataDir);
        var tallyPort = ProcessHost.FreePort();
        var serverPort = ProcessHost.FreePort();
        var tally = StartTally(tallyPort);
        var server = StartServer(serverPort);
        var baseUrl = $"http://127.0.0.1:{serverPort}";
        await ProcessHost.WaitForHttpAsync($"http://127.0.0.1:{tallyPort}/", TimeSpan.FromSeconds(60), () => tally.Output);
        await ProcessHost.WaitForHttpAsync($"{baseUrl}/api/health", TimeSpan.FromSeconds(60), () => server.Output);

        // --- first run: admin sets password and configures company + Tally
        var api = QuotationApiClient.Create(baseUrl);
        var login = await api.LoginAsync("admin", "first-admin");
        Assert.True(login.User.MustChangePassword);
        await api.ChangePasswordAsync("first-admin", "Str0ng-pass");
        var s = await api.SettingsAsync();
        s.Company.CompanyName = "T.SAIFUDDIN & CO.";
        s.Company.AddressLines = ["No. 72, N.R.Road, Bangalore", "INDIA"];
        s.Company.StateName = "Karnataka";
        s.Company.StateCode = "29";
        s.Company.Gstin = "29AAAFT0000A1Z0";
        s.Company.Pan = "AAAFT0000A";
        s.Tally.Url = $"http://127.0.0.1:{tallyPort}";
        s.Ai.ApiKey = "sk-ant-api03-E2E-SECRET-VALUE-0000000000";
        await api.SaveSettingsAsync(s);

        var test = await api.TestTallyAsync();
        Assert.True(test.Success, test.Message);
        Assert.Equal("T.SAIFUDDIN & CO.", test.Company);

        var sync = await api.StartSyncAsync(SyncKind.Full, wait: true);
        Assert.Equal(SyncStatus.Succeeded, sync!.Status);
        output.WriteLine(sync.Message);

        // --- quotation from a second PC
        var pc2 = QuotationApiClient.Create(baseUrl);
        await pc2.LoginAsync("admin", "Str0ng-pass");
        var customer = (await pc2.SearchCustomersAsync("sonepar india private")).First().Customer;
        var product = (await pc2.SearchProductsAsync("universal bevel")).First().Product;
        Assert.Equal(15600m, product.Rate);
        var q = await pc2.CreateQuotationAsync(new SaveQuotationRequest
        {
            Date = DateOnly.FromDateTime(DateTime.Today),
            CustomerId = customer.Id,
            PackingForwarding = 500,
            Lines = [new QuotationLineDto { ProductId = product.Id, Quantity = 2, Rate = -1, Description = "Brand: Mitutoyo" }],
        });
        Assert.EndsWith("-1", q.Number);
        var approved = await pc2.ApproveQuotationAsync(q.Id);
        Assert.Equal(QuotationStatus.Generated, approved.Status);
        var pdf = await pc2.QuotationPdfAsync(q.Id);
        using (var doc = PdfDocument.Open(pdf))
        {
            var text = string.Join(" ", doc.GetPage(1).GetWords().Select(w => w.Text));
            Assert.Contains(q.Number!, text);
            Assert.Contains("31,700.00", text);
            Assert.Contains("PROTRACTOR", text);
        }

        // --- crash and restart: data survives, numbering continues, no duplicate
        server.Kill();
        server = StartServer(serverPort);
        await ProcessHost.WaitForHttpAsync($"{baseUrl}/api/health", TimeSpan.FromSeconds(60), () => server.Output);
        await pc2.LoginAsync("admin", "Str0ng-pass");
        Assert.Equal(QuotationStatus.Generated, (await pc2.QuotationAsync(q.Id)).Status);
        var q2 = await pc2.DuplicateQuotationAsync(q.Id);
        Assert.EndsWith("-2", q2.Number);
        Assert.Equal(15600m, q2.Lines[0].Rate);

        // --- Tally goes down: search keeps working, status and approval warn
        tally.Kill();
        var status = (await pc2.TestTallyAsync());
        Assert.False(status.Success);
        Assert.NotEmpty(await pc2.SearchProductsAsync("universal bevel"));
        var ex = await Assert.ThrowsAsync<ApiException>(() => pc2.ApproveQuotationAsync(q2.Id));
        Assert.Equal(ApiErrorCodes.StaleData, ex.Code);
        Assert.Equal(QuotationStatus.Generated, (await pc2.ApproveQuotationAsync(q2.Id, overrideStaleData: true)).Status);

        // --- files on disk: database, PDFs, logs; secrets never logged
        Assert.True(File.Exists(Path.Combine(_dataDir, "quotation.db")));
        Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(_dataDir, "pdf"), "*.pdf", SearchOption.AllDirectories).Count());
        var logs = Directory.EnumerateFiles(Path.Combine(_dataDir, "logs")).Select(f =>
        {
            using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return new StreamReader(fs).ReadToEnd();
        }).ToList();
        Assert.NotEmpty(logs);
        Assert.All(logs, l => Assert.DoesNotContain("E2E-SECRET", l));
        Assert.All(logs, l => Assert.DoesNotContain("Str0ng-pass", l));
        Assert.Contains(logs, l => l.Contains("sync completed", StringComparison.OrdinalIgnoreCase));
    }
}
