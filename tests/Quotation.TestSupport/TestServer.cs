using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quotation.ApiClient;
using Quotation.Server.Services;
using Tally.Simulator;

namespace Quotation.TestSupport;

/// <summary>In-process Quotation Server with its own temporary data directory and database.</summary>
public sealed class TestServer : WebApplicationFactory<Program>
{
    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "tsq-tests", Guid.NewGuid().ToString("N"));

    public Action<IServiceCollection>? ConfigureServicesHook { get; set; }

    /// <summary>When set, replaces the Claude provider (scripted model for tests).</summary>
    public Quotation.AI.IAIProvider? AiProvider { get; set; }

    private sealed class FixedFactory(TestServer server) : IAIProviderFactory
    {
        public Quotation.AI.IAIProvider? Create() => server.AiProvider;
    }

    /// <summary>Fake mailbox the server's Gmail ingestion reads from (null = not connected).</summary>
    public FakeGmail? Gmail { get; set; }

    private sealed class GmailFactory(TestServer server) : IGmailClientFactory
    {
        public Quotation.Gmail.IGmailClient? Create() => server.Gmail;
    }

    /// <summary>Simulated TallyPrime that the server's "tally" HTTP client talks to.</summary>
    public TallySimulatorEngine Tally { get; }

    public TestServer(int products = 300, int customers = 200, DateOnly? today = null)
    {
        Directory.CreateDirectory(DataDirectory);
        Tally = new TallySimulatorEngine(SampleDataset.Create(products, customers, today: today ?? DateOnly.FromDateTime(DateTime.Today)));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(DataDirectory);
        builder.UseSetting("Server:DataDirectory", DataDirectory);
        builder.UseSetting("Server:EnableBackgroundWorkers", "false");
        builder.UseSetting("Server:BootstrapAdminPassword", "admin123");
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.AddHttpClient(TallyGateway.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new SimulatorHttpHandler(Tally));
            services.AddSingleton<IAIProviderFactory>(new FixedFactory(this));
            services.AddSingleton<IGmailClientFactory>(new GmailFactory(this));
        });
        if (ConfigureServicesHook is not null) builder.ConfigureServices(ConfigureServicesHook);
    }

    /// <summary>Direct database access for assertions.</summary>
    public T Db<T>(Func<Quotation.Data.QuotationDbContext, T> query)
    {
        using var scope = Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<Quotation.Data.QuotationDbContext>());
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
