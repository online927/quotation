using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quotation.ApiClient;

namespace Quotation.TestSupport;

/// <summary>In-process Quotation Server with its own temporary data directory and database.</summary>
public sealed class TestServer : WebApplicationFactory<Program>
{
    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "tsq-tests", Guid.NewGuid().ToString("N"));

    public Action<IServiceCollection>? ConfigureServicesHook { get; set; }

    public TestServer() => Directory.CreateDirectory(DataDirectory);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(DataDirectory);
        builder.UseSetting("Server:DataDirectory", DataDirectory);
        builder.UseSetting("Server:EnableBackgroundWorkers", "false");
        builder.UseSetting("Server:BootstrapAdminPassword", "admin123");
        builder.UseEnvironment("Testing");
        if (ConfigureServicesHook is not null) builder.ConfigureServices(ConfigureServicesHook);
    }

    public QuotationApiClient CreateApi(string machine = "TEST-PC") => new(CreateClient(), machine);

    public async Task<QuotationApiClient> LoginAsAdminAsync(string machine = "TEST-PC")
    {
        var api = CreateApi(machine);
        await api.LoginAsync("admin", "admin123");
        return api;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(DataDirectory, recursive: true); } catch { /* best effort */ }
    }
}
