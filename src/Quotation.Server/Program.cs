using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Quotation.Data;
using Quotation.Server;
using Quotation.Server.Auth;
using Quotation.Server.Endpoints;
using Quotation.Server.Infrastructure;
using Quotation.Server.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Host.UseWindowsService(o => o.ServiceName = "TSQuotationServer");

var serverOptions = builder.Configuration.GetSection("Server").Get<ServerOptions>() ?? new ServerOptions();
var dataDir = serverOptions.ResolveDataDirectory();
builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection("Server"));

const string logTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RedactingFormatter(logTemplate))
    .WriteTo.File(new RedactingFormatter(logTemplate), Path.Combine(dataDir, "logs", "server-.log"),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 60));

var dbPath = builder.Configuration["Server:DatabasePath"] is { Length: > 0 } p ? p : Path.Combine(dataDir, "quotation.db");
builder.Services.AddDbContext<QuotationDbContext>(o => o.UseSqlite(DatabaseInitializer.BuildConnectionString(dbPath)));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISecretProtector>(new SecretProtector(dataDir));
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<RuntimeStatus>();
builder.Services.AddSingleton<FinancialYearService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<StatusService>();
builder.Services.AddHttpClient(TallyGateway.HttpClientName);
builder.Services.AddSingleton<TallyGateway>();
builder.Services.AddSingleton<TallyConnectionChecker>();
builder.Services.AddHostedService<TallyMonitorService>();
builder.Services.AddSingleton<TallySyncService>();
builder.Services.AddHostedService<SyncWorker>();
builder.Services.AddSingleton<CatalogSearchService>();
builder.Services.AddSingleton<NumberingService>();
builder.Services.AddSingleton<PdfStorage>();
builder.Services.AddSingleton<PdfQuotationRenderer>();
builder.Services.AddSingleton<IQuotationDocumentRenderer>(sp => sp.GetRequiredService<PdfQuotationRenderer>());
builder.Services.AddScoped<QuotationService>();
builder.Services.AddSingleton<IAIProviderFactory, ClaudeProviderFactory>();
builder.Services.AddScoped<AiDataTools>();
builder.Services.AddScoped<AiService>();

builder.Services.AddAuthentication(TokenAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, TokenAuthenticationHandler>(TokenAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(SystemEndpoints.AdminPolicy, p => p.RequireRole("Admin"));
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddProblemDetails();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
    await DatabaseInitializer.InitializeAsync(db);
    await scope.ServiceProvider.GetRequiredService<AuthService>().EnsureBootstrapAdminAsync(CancellationToken.None);
    await TallySyncService.RecoverInterruptedAsync(db, DateTime.UtcNow, CancellationToken.None);
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapSystemEndpoints();
app.MapTallyEndpoints();
app.MapSyncEndpoints();
app.MapCatalogEndpoints();
app.MapQuotationEndpoints();
app.MapAiEndpoints();

app.Logger.LogInformation("TS Quotation Server starting. Data directory: {DataDir}", dataDir);
await app.RunAsync();

public partial class Program;
