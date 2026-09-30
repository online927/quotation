using Microsoft.Extensions.Options;
using Quotation.Core.Domain;
using Quotation.Tally;

namespace Quotation.Server.Services;

/// <summary>
/// Periodically checks the Tally connection, the open company and the active financial year,
/// and publishes the result to <see cref="RuntimeStatus"/>.
/// </summary>
public sealed class TallyConnectionChecker(
    TallyGateway gateway,
    RuntimeStatus runtime,
    SettingsService settings,
    FinancialYearService fy,
    TimeProvider clock,
    ILogger<TallyConnectionChecker> log)
{
    public async Task<(TallyCompanyInfo? Info, string? Error)> CheckAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        try
        {
            var info = await gateway.Companies(TimeSpan.FromSeconds(15))
                .GetCompanyInfoAsync(settings.Quotation.FinancialYearStartMonth, ct);
            var wasConnected = runtime.TallyConnected;
            runtime.SetTally(true, info.Company.Name, null, now, info.ActiveFinancialYearStart);
            if (info.ActiveFinancialYearStart is { } start) await fy.RememberDetectedAsync(start, ct);
            if (!wasConnected) log.LogInformation("Tally connected: {Company}, period {From}–{To}", info.Company.Name, info.PeriodFrom, info.PeriodTo);
            return (info, null);
        }
        catch (TallyException ex)
        {
            if (runtime.TallyConnected || runtime.TallyError != ex.Message) log.LogWarning("Tally unavailable: {Message}", ex.Message);
            runtime.SetTally(false, null, ex.Message, now, null);
            return (null, ex.Message);
        }
    }
}

public sealed class TallyMonitorService(
    IServiceProvider services,
    SettingsService settings,
    IOptions<ServerOptions> options,
    ILogger<TallyMonitorService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.EnableBackgroundWorkers) return;
        var checker = services.GetRequiredService<TallyConnectionChecker>();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await checker.CheckAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Tally connection check failed unexpectedly");
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, settings.Tally.ConnectionCheckSeconds)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
