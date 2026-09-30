using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Server.Services;
using Quotation.Tally;

namespace Quotation.Server.Endpoints;

public static class TallyEndpoints
{
    public static void MapTallyEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/tally").RequireAuthorization();

        g.MapPost("/test", async (TallyConnectionChecker checker, SettingsService settings, CancellationToken ct) =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (info, error) = await checker.CheckAsync(ct);
            if (info is null)
            {
                return new TallyTestResultDto(false, error ?? "Unknown error", null, [], null, null, null, sw.ElapsedMilliseconds);
            }
            var fyLabel = info.ActiveFinancialYearStart is { } s
                ? new FinancialYear(s, settings.Quotation.FinancialYearStartMonth).Label
                : null;
            return new TallyTestResultDto(true,
                $"Connected to '{info.Company.Name}' in {sw.ElapsedMilliseconds} ms.",
                info.Company.Name, info.LoadedCompanies, info.PeriodFrom, info.PeriodTo, fyLabel, sw.ElapsedMilliseconds);
        });

        g.MapGet("/companies", async (TallyGateway gateway, CancellationToken ct) =>
        {
            try
            {
                var list = await gateway.Companies(TimeSpan.FromSeconds(15)).GetLoadedCompaniesAsync(ct);
                return Results.Ok(list.Select(c => c.Name).ToList());
            }
            catch (TallyException ex)
            {
                return Results.Json(new ApiError(ex.Message), statusCode: 503);
            }
        });
    }
}
