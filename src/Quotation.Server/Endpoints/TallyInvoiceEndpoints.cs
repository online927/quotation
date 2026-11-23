using Microsoft.EntityFrameworkCore;
using Quotation.Contracts;
using Quotation.Data;
using Quotation.Server.Auth;
using Quotation.Server.Services;
using Quotation.Tally;

namespace Quotation.Server.Endpoints;

/// <summary>
/// Round 36: "Copy from Tally invoice" (administrator switch, off at first). The chosen customer's sales invoices of the
/// last 12 months, read from Tally only when a user clicks the button: one request, one customer, through Tally's
/// voucher-type index. Tally is only read; nothing there is changed. Refused while Tally is busy, to keep it responsive.
/// </summary>
public static class TallyInvoiceEndpoints
{
    public const int Months = 12;

    public static void MapTallyInvoiceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/customers/{id:int}/tally-invoices", async (int id, QuotationDbContext db, SettingsService settings,
            TallyGateway gateway, TimeProvider clock, ILoggerFactory logs, CancellationToken ct) =>
        {
            if (!settings.Quotation.CopyFromTallyInvoice)
                return Results.Json(new ApiError("Copy from Tally invoice is switched off. An administrator can switch it on in Settings → Features."), statusCode: 409);
            var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
            if (customer is null) return Results.NotFound(new ApiError("This customer is not in Tally now."));
            if (gateway.Load.IsBusy) return Results.Json(new ApiError("Tally is busy right now. Try again in a minute."), statusCode: 503);

            var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            IReadOnlyList<TallyInvoice> invoices;
            try
            {
                var vouchers = new TallyVoucherService(gateway.CreateClient(TimeSpan.FromSeconds(45)));
                invoices = await vouchers.GetPartyInvoicesAsync("Sales", customer.Name, today.AddMonths(-Months), today, ct);
            }
            catch (Exception ex) when (ex is TallyException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logs.CreateLogger("TallyInvoices").LogWarning("Reading Tally invoices failed: {Message}", ex.Message);
                return Results.Json(new ApiError("Tally could not be read just now: " + ex.Message), statusCode: 503);
            }

            var names = invoices.SelectMany(i => i.Lines).Select(l => l.ItemName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var products = (await db.Products.AsNoTracking().Where(p => !p.IsDeleted && names.Contains(p.Name)).ToListAsync(ct))
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            return Results.Ok(invoices.Take(50).Select(i => new TallyInvoiceDto(i.Number, i.Date, i.VoucherType, i.Total,
                i.Lines.Select(l => new TallyInvoiceLineDto(l.ItemName, l.Quantity, l.Unit, l.Rate,
                    products.TryGetValue(l.ItemName, out var p) ? CatalogEndpoints.ToSummary(p) : null)).ToList())).ToList());
        }).RequireAuthorization();
    }
}
