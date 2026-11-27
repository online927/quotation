using Microsoft.EntityFrameworkCore;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Data;
using Quotation.Server.Infrastructure;
using Quotation.Server.Services;

namespace Quotation.Server.Endpoints;

public static class QuotationEndpoints
{
    public static void MapQuotationEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/quotations").RequireAuthorization();

        // Who else has this quotation open (checked in every 15 s by each open quotation screen).
        g.MapPost("/{id:guid}/presence", (Guid id, HttpContext ctx, PresenceTracker presence) =>
            Results.Ok(presence.CheckIn(id, ctx.User.UserName(), ctx.User.DisplayName(), ctx.Machine())));
        g.MapDelete("/{id:guid}/presence", (Guid id, HttpContext ctx, PresenceTracker presence) =>
        {
            presence.Leave(id, ctx.User.UserName(), ctx.Machine());
            return Results.NoContent();
        });

        g.MapGet("/", async (QuotationDbContext db, string? q, QuotationStatus? status, QuotationSource? source,
            DateOnly? from, DateOnly? to, int? customerId, int? take, int? skip, DocumentType? type, QuotationOutcome? outcome, bool? followUpDue,
            int? sequence, FinancialYearService fy, SettingsService settings, CancellationToken ct) =>
        {
            var notesOn = settings.Quotation.InternalNotes; // Round 39 (Z8): search, count and newest note only while on
            var query = db.Quotations.AsNoTracking().AsQueryable();
            // The number part of a quotation number ("142" of TSQ2627-142), in every year: the top search jumps to it.
            if (sequence is not null) query = query.Where(x => x.Number != null && x.Sequence == sequence);
            if (outcome is not null) query = query.Where(x => x.Outcome == outcome
                && (x.Status == QuotationStatus.Approved || x.Status == QuotationStatus.Generated) && x.DocType == DocumentType.Quotation);
            if (followUpDue == true)
            {
                var today = fy.Today();
                query = query.Where(x => x.Outcome == QuotationOutcome.Open && x.FollowUpDate != null && x.FollowUpDate <= today
                    && (x.Status == QuotationStatus.Approved || x.Status == QuotationStatus.Generated) && x.DocType == DocumentType.Quotation);
            }
            if (type is not null) query = query.Where(x => x.DocType == type);
            if (status is not null) query = query.Where(x => x.Status == status);
            if (source is not null) query = query.Where(x => x.Source == source);
            if (from is not null) query = query.Where(x => x.Date >= from);
            if (to is not null) query = query.Where(x => x.Date <= to);
            if (customerId is not null) query = query.Where(x => x.CustomerId == customerId);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var terms = SearchTerms(q);
                foreach (var (term, quantity) in terms.Take(5))
                {
                    // Round 41 (SR-1): "qty 5" / "x5": a line with that quantity.
                    if (quantity is { } qty)
                    {
                        var withQty = await AmountMatchesAsync(db, qty, quantityOnly: true, ct);
                        query = query.Where(x => withQty.Contains(x.Id));
                        continue;
                    }
                    // A date term (30-09-2026, 30/09/26, 30-Sep-2026) searches by quotation date.
                    if (DateOnly.TryParseExact(term, DateFormats, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out var day))
                    {
                        query = query.Where(x => x.Date == day);
                        continue;
                    }
                    // SQLite LIKE: \ escapes % and _ (the [ ] form is SQL Server only, so "_" and "%" never matched before).
                    var like = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                    // Round 36: an item code typed without its dashes, dots or spaces ("18790110" finds "187-901-10").
                    var compact = new string(term.Where(char.IsLetterOrDigit).ToArray());
                    var compactLike = compact.Length >= 4 ? "%" + compact + "%" : null;
                    // Round 36: Tally items whose code, other names (aliases) or HSN match: their quotations are found too.
                    var itemIds = db.Products.Where(p =>
                            EF.Functions.Like(p.PartNumber, like, "\\") || EF.Functions.Like(p.Aliases, like, "\\") ||
                            (compactLike != null && EF.Functions.Like(p.PartNumber.Replace("-", "").Replace(".", "").Replace(" ", "").Replace("/", ""), compactLike)))
                        .Select(p => (int?)p.Id);
                    var noted = db.QuotationNotes.Where(n => notesOn && EF.Functions.Like(n.Text, like, "\\")).Select(n => n.QuotationId);
                    // Round 41 (SR-1): a number ("75291", "75,291.00", "₹75,291") also finds the total, the total before tax,
                    // P&F, a line's amount or rate.
                    var amountIds = AmountOf(term) is { } amount ? await AmountMatchesAsync(db, amount, quantityOnly: false, ct) : [];
                    query = query.Where(x =>
                        amountIds.Contains(x.Id) ||
                        noted.Contains(x.Id) ||
                        EF.Functions.Like(x.Buyer.Address, like, "\\") ||
                        EF.Functions.Like(x.Buyer.Email, like, "\\") ||
                        EF.Functions.Like(x.Consignee.Address, like, "\\") ||
                        EF.Functions.Like(x.Consignee.Gstin, like, "\\") ||
                        EF.Functions.Like(x.Consignee.ContactPerson, like, "\\") ||
                        EF.Functions.Like(x.Consignee.Phone, like, "\\") ||
                        EF.Functions.Like(x.DispatchedThrough, like, "\\") ||
                        EF.Functions.Like(x.Destination, like, "\\") ||
                        EF.Functions.Like(x.Number!, like, "\\") ||
                        EF.Functions.Like(x.Buyer.Name, like, "\\") ||
                        EF.Functions.Like(x.Buyer.Gstin, like, "\\") ||
                        EF.Functions.Like(x.Buyer.ContactPerson, like, "\\") ||
                        EF.Functions.Like(x.Buyer.Phone, like, "\\") ||
                        EF.Functions.Like(x.Consignee.Name, like, "\\") ||
                        EF.Functions.Like(x.BuyersReference, like, "\\") ||
                        EF.Functions.Like(x.OtherReferences, like, "\\") ||
                        EF.Functions.Like(x.Remarks, like, "\\") ||
                        x.Lines.Any(l => EF.Functions.Like(l.ItemName, like, "\\") || EF.Functions.Like(l.Description, like, "\\") ||
                                         EF.Functions.Like(l.PartNumber, like, "\\") || EF.Functions.Like(l.Hsn, like, "\\") ||
                                         (compactLike != null && (
                                             EF.Functions.Like(l.PartNumber.Replace("-", "").Replace(".", "").Replace(" ", "").Replace("/", ""), compactLike) ||
                                             EF.Functions.Like(l.ItemName.Replace("-", "").Replace(".", "").Replace(" ", "").Replace("/", ""), compactLike))) ||
                                         itemIds.Contains(l.ProductId)));
                }
            }
            var rows = await query
                .OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedUtc)
                .Skip(Math.Max(0, skip ?? 0)).Take(Math.Clamp(take ?? 100, 1, 1000))
                .Select(x => new QuotationSummaryDto(x.Id, x.Number, x.Date, x.Status, x.Source, x.Buyer.Name, x.Buyer.Gstin,
                    x.Lines.OrderBy(l => l.LineNo).Select(l => l.ItemName).FirstOrDefault() ?? "", x.Lines.Count, x.GrandTotal,
                    x.CreatedBy, x.CreatedUtc, x.ApprovedBy, x.PdfPath != null, x.DocType, x.Outcome, x.FollowUpDate,
                    x.Owner ?? (x.CreatedBy == QuotationService.GmailUserName ? null : x.CreatedBy), x.Currency))
                .ToListAsync(ct);
            if (notesOn && rows.Count > 0)
            {
                var ids = rows.Select(r => r.Id).ToList();
                var notes = await db.QuotationNotes.AsNoTracking().Where(n => ids.Contains(n.QuotationId))
                    .Select(n => new { n.QuotationId, n.Text, n.CreatedUtc, n.Id }).ToListAsync(ct);
                var byQuotation = notes.GroupBy(n => n.QuotationId).ToDictionary(g => g.Key,
                    g => (Count: g.Count(), Latest: g.OrderByDescending(n => n.CreatedUtc).ThenByDescending(n => n.Id).First().Text));
                rows = rows.Select(r => byQuotation.TryGetValue(r.Id, out var n) ? r with { NoteCount = n.Count, LatestNote = n.Latest } : r).ToList();
            }
            return rows;
        });

        g.MapGet("/{id:guid}", (Guid id, QuotationService svc, CancellationToken ct) => Handle(() => svc.GetAsync(id, ct)));

        g.MapPost("/", (SaveQuotationRequest r, QuotationService svc, HttpContext ctx, CancellationToken ct) =>
            Handle(() => svc.CreateAsync(r, r.FromFormater ? QuotationSource.Formater : QuotationSource.Manual, User(ctx), ct)));

        g.MapPut("/{id:guid}", (Guid id, SaveQuotationRequest r, QuotationService svc, HttpContext ctx, CancellationToken ct) =>
            Handle(() => svc.UpdateAsync(id, r, User(ctx), ct)));

        g.MapPost("/{id:guid}/approve", (Guid id, ApproveRequest r, QuotationService svc, HttpContext ctx, CancellationToken ct) =>
            Handle(() => svc.ApproveAsync(id, r, User(ctx), ct)));

        g.MapPut("/{id:guid}/outcome", (Guid id, SetOutcomeRequest r, QuotationService svc, HttpContext ctx, CancellationToken ct) =>
            Handle(() => svc.SetOutcomeAsync(id, r, User(ctx), ct)));

        g.MapPost("/{id:guid}/cancel", (Guid id, QuotationService svc, HttpContext ctx, CancellationToken ct) =>
            Handle(() => svc.CancelAsync(id, User(ctx), ct)));

        // Administrator: delete (to the Deleted list), list deleted, restore, delete permanently.
        g.MapDelete("/{id:guid}", (Guid id, QuotationService svc, HttpContext ctx, CancellationToken ct) =>
            Handle(async () => { await svc.DeleteAsync(id, User(ctx), ct); return true; })).RequireAuthorization(SystemEndpoints.AdminPolicy);
        g.MapGet("/deleted", (QuotationService svc, CancellationToken ct) => Handle(() => svc.DeletedAsync(ct)))
            .RequireAuthorization(SystemEndpoints.AdminPolicy);
        g.MapPost("/deleted/{id:guid}/restore", (Guid id, QuotationService svc, HttpContext ctx, CancellationToken ct) =>
            Handle(() => svc.RestoreAsync(id, User(ctx), ct))).RequireAuthorization(SystemEndpoints.AdminPolicy);
        g.MapDelete("/deleted/{id:guid}", (Guid id, QuotationService svc, HttpContext ctx, CancellationToken ct) =>
            Handle(async () => { await svc.PurgeAsync(id, User(ctx), ct); return true; })).RequireAuthorization(SystemEndpoints.AdminPolicy);

        g.MapPost("/{id:guid}/duplicate", (Guid id, QuotationService svc, HttpContext ctx, CancellationToken ct) =>
            Handle(() => svc.DuplicateAsync(id, User(ctx), ct)));

        g.MapPost("/{id:guid}/convert-to-proforma", (Guid id, QuotationService svc, HttpContext ctx, CancellationToken ct) =>
            Handle(() => svc.ConvertToProformaAsync(id, User(ctx), ct)));

        g.MapPost("/{id:guid}/regenerate-pdf", (Guid id, QuotationService svc, HttpContext ctx, CancellationToken ct) =>
            Handle(async () =>
            {
                var q = await svc.LoadAsync(id, ct);
                await svc.GeneratePdfAsync(q, User(ctx), ct);
                return svc.ToDto(q);
            }));

        g.MapGet("/{id:guid}/pdf", async (Guid id, string? layout, QuotationDbContext db, QuotationService svc, PdfStorage storage, AuditService audit,
            HttpContext ctx, CancellationToken ct) =>
        {
            var q = await db.Quotations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            var path = storage.FullPath(q?.PdfPath);
            if (q is null || path is null) return Results.NotFound(new ApiError("No PDF has been generated for this quotation."));
            // Printed fresh, so it shows who printed it and when (as Tally does).
            var bytes = await svc.PrintPdfAsync(id, User(ctx), ct, layout: Layout(layout));
            if (bytes is null) return Results.NotFound(new ApiError("No PDF has been generated for this quotation."));
            await audit.WriteAsync(ctx.User.UserName(), ctx.Machine(), "PdfDownloaded", "Quotation", id.ToString(), q.Number ?? "", ct);
            return Results.File(bytes, "application/pdf", Path.GetFileName(path));
        });

        // The PDF with the user's own seal and their 4-digit seal PIN (administrators do not seal).
        g.MapPost("/{id:guid}/pdf/sealed", async (Guid id, string? layout, SealedPdfRequest r, QuotationService svc, SealService seal,
            SettingsService settings, QuotationDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            try
            {
                var user = User(ctx);
                var account = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == user.UserName, ct);
                var image = seal.Authorize(user, account, r.Pin, settings.Quotation.SealPin);
                var bytes = await svc.PrintPdfAsync(id, user, ct, image, Layout(layout));
                return bytes is null
                    ? Results.Json(new ApiError("Only an approved quotation can be sealed: approve it first.", null, ApiErrorCodes.InvalidState), statusCode: 409)
                    : Results.File(bytes, "application/pdf");
            }
            catch (QuotationException ex)
            {
                return Results.Json(new ApiError(ex.Message, null, ex.Code), statusCode: ex.Status);
            }
        });

        // Preview of a quotation that is not saved yet (Formater page): nothing is saved, no number is used.
        g.MapPost("/preview", async (SaveQuotationRequest r, QuotationService svc, PdfQuotationRenderer pdf, HttpContext ctx, CancellationToken ct) =>
        {
            try
            {
                return Results.File(await svc.PreviewRequestPdfAsync(r, pdf, User(ctx), ct), "application/pdf", "preview.pdf");
            }
            catch (QuotationException ex)
            {
                return Results.Json(new ApiError(ex.Message, null, ex.Code), statusCode: ex.Status);
            }
        });

        g.MapGet("/{id:guid}/preview", async (Guid id, QuotationService svc, PdfQuotationRenderer pdf, HttpContext ctx, CancellationToken ct) =>
        {
            try
            {
                return Results.File(await svc.PreviewPdfAsync(id, pdf, ct, User(ctx)), "application/pdf", "preview.pdf");
            }
            catch (QuotationException ex)
            {
                return Results.Json(new ApiError(ex.Message, null, ex.Code), statusCode: ex.Status);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ApiError(ex.Message));
            }
        });

        g.MapGet("/{id:guid}/validate", (Guid id, QuotationService svc, CancellationToken ct) => Handle(async () =>
        {
            var q = await svc.LoadAsync(id, ct);
            return svc.Validate(q).Select(i => new ValidationErrorDto(i.Field, i.Message, i.LineNo)).ToList();
        }));

        g.MapGet("/{id:guid}/audit", async (Guid id, QuotationDbContext db, CancellationToken ct) =>
            await db.AuditLog.AsNoTracking().Where(a => a.EntityType == "Quotation" && a.EntityId == id.ToString())
                .OrderBy(a => a.Id)
                .Select(a => new AuditEntryDto(a.Id, a.AtUtc, a.User, a.Machine, a.Action, a.EntityType, a.EntityId, a.Details))
                .ToListAsync(ct));

        g.MapGet("/recent-items", async (QuotationDbContext db, CatalogSearchService search, HttpContext ctx, CancellationToken ct) =>
        {
            var user = ctx.User.UserName();
            var recent = await db.Quotations.AsNoTracking().Where(x => x.CreatedBy == user || x.UpdatedBy == user)
                .OrderByDescending(x => x.CreatedUtc).Take(60)
                .Select(x => new { x.CustomerId, x.Buyer.Name, Lines = x.Lines.Select(l => new { l.ProductId, l.ItemName }) })
                .ToListAsync(ct);
            var customers = recent.Where(r => r.CustomerId != null).Select(r => new RecentItemDto(r.CustomerId!.Value, r.Name))
                .DistinctBy(c => c.Id).Take(8).ToList();
            var candidates = recent.SelectMany(r => r.Lines).Where(l => l.ProductId != null).Select(l => new RecentItemDto(l.ProductId!.Value, l.ItemName))
                .DistinctBy(p => p.Id).Take(30).ToList();
            // Items marked "Do Not Use" in Tally are not offered again.
            var ids = candidates.Select(c => c.Id).ToList();
            var usable = (await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id) && !p.IsDeleted).Select(p => new { p.Id, p.Description }).ToListAsync(ct))
                .Where(p => !search.IsHidden(p.Description)).Select(p => p.Id).ToHashSet();
            var products = candidates.Where(c => usable.Contains(c.Id)).Take(10).ToList();
            return new RecentItemsDto(customers, products);
        });

        g.MapGet("/next-number", (DateOnly? date, DocumentType? type, QuotationService svc, CancellationToken ct) =>
            Handle(() => svc.PreviewNextNumberAsync(date, ct, type ?? DocumentType.Quotation)));

        var n = app.MapGroup("/api/numbering").RequireAuthorization(SystemEndpoints.AdminPolicy);
        n.MapGet("/", async (int? financialYearStart, DocumentType? type, NumberingService numbering, FinancialYearService fy,
            SettingsService settings, QuotationDbContext db, CancellationToken ct) =>
        {
            var year = financialYearStart is { } s ? new FinancialYear(s, settings.Quotation.FinancialYearStartMonth) : fy.GetActive().Year;
            return await numbering.GetSeriesAsync(db, year, ct, type ?? DocumentType.Quotation);
        });
        n.MapPut("/", async (SetNextSequenceRequest r, NumberingService numbering, SettingsService settings, QuotationDbContext db,
            AuditService audit, HttpContext ctx, CancellationToken ct) =>
        {
            try
            {
                var year = new FinancialYear(r.FinancialYearStart, settings.Quotation.FinancialYearStartMonth);
                var result = await numbering.SetNextAsync(db, year, r.NextSequence, ct, r.DocType);
                await audit.WriteAsync(ctx.User.UserName(), ctx.Machine(), "NumberSeriesChanged", "NumberSeries", year.Label,
                    $"{r.DocType}: Next={r.NextSequence}", ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ApiError(ex.Message));
            }
        });
    }

    /// <summary>
    /// Round 41 (SR-1): the search words; "qty 5", "qty5", "x 5", "x5" become a quantity search. Other words stay text (a number
    /// is also tried as an amount).
    /// </summary>
    internal static List<(string Term, decimal? Quantity)> SearchTerms(string q)
    {
        var words = q.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<(string, decimal?)>();
        for (var i = 0; i < words.Length; i++)
        {
            var w = words[i];
            var lower = w.ToLowerInvariant();
            if ((lower is "qty" or "x" or "qty:") && i + 1 < words.Length && decimal.TryParse(words[i + 1], System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var n))
            {
                result.Add((w + " " + words[++i], n));
                continue;
            }
            foreach (var prefix in new[] { "qty", "x" })
                if (lower.StartsWith(prefix, StringComparison.Ordinal) && lower.Length > prefix.Length && decimal.TryParse(lower[prefix.Length..],
                        System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var m))
                {
                    result.Add((w, m));
                    goto next;
                }
            result.Add((w, null));
            next:;
        }
        return result;
    }

    /// <summary>A search word read as an amount: "75291", "75,291", "75291.50", "₹75,291", "Rs.75291/-" (null: not a number).</summary>
    internal static decimal? AmountOf(string term)
    {
        var s = term.Trim();
        foreach (var sign in new[] { "₹", "rs.", "rs", "inr" })
            if (s.StartsWith(sign, StringComparison.OrdinalIgnoreCase)) { s = s[sign.Length..]; break; }
        if (s.EndsWith("/-", StringComparison.Ordinal)) s = s[..^2];
        s = s.Replace(",", "");
        if (s.Length == 0 || !s.All(c => char.IsDigit(c) || c == '.') || s.Count(c => c == '.') > 1) return null;
        return decimal.TryParse(s, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v : null;
    }

    /// <summary>
    /// The quotations with this amount: the grand total, the total before tax, P&amp;F, a line's amount or rate (or, with
    /// <paramref name="quantityOnly"/>, a line's quantity). Without paise the whole rupee matches ("75291" = 75,291.00 … 75,291.99).
    /// Amounts are stored as text, so they are compared as numbers in SQL.
    /// </summary>
    private static async Task<List<Guid>> AmountMatchesAsync(QuotationDbContext db, decimal value, bool quantityOnly, CancellationToken ct)
    {
        var whole = decimal.Truncate(value) == value;
        var lo = (double)value;
        var hi = whole ? lo + 1 : lo + 0.005;
        lo -= 0.0001;
        var sql = quantityOnly
            ? "SELECT DISTINCT QuotationId AS Value FROM QuotationLines WHERE CAST(Quantity AS REAL) >= {0} AND CAST(Quantity AS REAL) < {1}"
            : "SELECT Id AS Value FROM Quotations WHERE (CAST(GrandTotal AS REAL) >= {0} AND CAST(GrandTotal AS REAL) < {1})" +
              " OR (CAST(Subtotal AS REAL) >= {0} AND CAST(Subtotal AS REAL) < {1})" +
              " OR (CAST(PackingForwarding AS REAL) >= {0} AND CAST(PackingForwarding AS REAL) < {1} AND CAST(PackingForwarding AS REAL) > 0)" +
              " UNION SELECT QuotationId AS Value FROM QuotationLines WHERE (CAST(Amount AS REAL) >= {0} AND CAST(Amount AS REAL) < {1})" +
              " OR (CAST(Rate AS REAL) >= {0} AND CAST(Rate AS REAL) < {1})";
        return await db.Database.SqlQueryRaw<Guid>(sql, lo, hi).ToListAsync(ct);
    }

    private static readonly string[] DateFormats = ["d-M-yyyy", "d/M/yyyy", "d-M-yy", "d/M/yy", "d-MMM-yyyy", "d-MMM-yy", "yyyy-MM-dd"];

    /// <summary>"?layout=Tally|Zoho" prints in that layout instead of the user's own (My Settings).</summary>
    private static string? Layout(string? layout) =>
        layout is null ? null
        : layout.Equals(PdfLayouts.Zoho, StringComparison.OrdinalIgnoreCase) ? PdfLayouts.Zoho
        : layout.Equals(PdfLayouts.Tally, StringComparison.OrdinalIgnoreCase) ? PdfLayouts.Tally
        : layout.Equals(PdfLayouts.Minimal, StringComparison.OrdinalIgnoreCase) ? PdfLayouts.Minimal
        : null;

    internal static UserContext User(HttpContext ctx) => new(ctx.User.UserName(), ctx.User.DisplayName(), ctx.Machine(), ctx.User.IsInRole("Admin"));

    internal static async Task<IResult> Handle<T>(Func<Task<T>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (QuotationException ex)
        {
            return Results.Json(new ApiError(ex.Message, ex.Details.Count > 0 ? ex.Details : null, ex.Code), statusCode: ex.Status);
        }
    }
}
