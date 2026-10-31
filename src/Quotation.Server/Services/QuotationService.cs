using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Quotation.Contracts;
using Quotation.Core.Calculation;
using Quotation.Core.Domain;
using Quotation.Core.Validation;
using Quotation.Data;
using Quotation.Data.Entities;
using Quotation.Tally;

namespace Quotation.Server.Services;

/// <summary>Business error with an HTTP status and machine-readable code.</summary>
public sealed class QuotationException(int status, string code, string message, IReadOnlyList<string>? details = null) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IReadOnlyList<string> Details { get; } = details ?? [];
}

/// <summary>Produces the PDF file for an approved quotation (implemented by the PDF engine).</summary>
public interface IQuotationDocumentRenderer
{
    /// <summary>Renders the quotation and returns the PDF bytes; <paramref name="printed"/> adds "Printed on … By: …".</summary>
    byte[] Render(QuotationHeader quotation, CompanySettings company, QuotationSettings settings, Quotation.Pdf.PrintStamp? printed = null,
        byte[]? seal = null, PartyMaster? master = null);
}

public sealed record UserContext(string UserName, string DisplayName, string Machine, bool IsAdmin = false);

/// <summary>
/// Quotation lifecycle: create/update drafts, approve (validate → freshness check → number →
/// PDF), cancel and duplicate. Tally values (item name, HSN, GST, unit, buyer GSTIN/state) are
/// always taken from the synchronized Tally data, never from the client or the AI.
/// </summary>
public sealed class QuotationService(
    QuotationDbContext db,
    SettingsService settings,
    FinancialYearService fy,
    NumberingService numbering,
    AuditService audit,
    RuntimeStatus runtime,
    StatusService status,
    TallySyncService sync,
    LastRateService lastRates,
    CatalogSearchService catalog,
    IQuotationDocumentRenderer renderer,
    PdfStorage pdfStorage,
    TimeProvider clock,
    ILogger<QuotationService> log)
{
    public async Task<QuotationDto> GetAsync(Guid id, CancellationToken ct) => await WithNoticesAsync(await LoadAsync(id, ct), ct);

    /// <summary>The quotation plus a warning about items marked "Do Not Use" (DNU) in Tally since it was made.</summary>
    public async Task<QuotationDto> WithNoticesAsync(QuotationHeader q, CancellationToken ct)
    {
        var dto = ToDto(q);
        var ids = q.Lines.Where(l => l.ProductId is not null).Select(l => l.ProductId!.Value).ToList();
        if (ids.Count == 0) return dto;
        var hidden = (await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Description }).ToListAsync(ct))
            .Where(p => catalog.IsHidden(p.Description)).Select(p => p.Id).ToHashSet();
        dto.DoNotUseItems = q.Lines.OrderBy(l => l.LineNo).Where(l => l.ProductId is { } id && hidden.Contains(id))
            .Select(l => $"Line {l.LineNo}: {l.ItemName}").ToList();
        return dto;
    }

    public async Task<QuotationHeader> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Quotations.Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == id, ct)
        ?? throw new QuotationException(404, "NOT_FOUND", "Quotation not found.");

    /// <summary>Creates a quotation. User-created quotations get their number immediately.</summary>
    public async Task<QuotationDto> CreateAsync(SaveQuotationRequest request, QuotationSource source, UserContext user,
        CancellationToken ct, bool assignNumber = true, int? sourceEmailId = null, Guid? duplicatedFrom = null)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var q = new QuotationHeader
        {
            Id = Guid.NewGuid(),
            Status = source == QuotationSource.Manual ? QuotationStatus.Draft : QuotationStatus.PendingReview,
            Source = source,
            CreatedBy = user.UserName,
            CreatedUtc = now,
            SourceEmailId = sourceEmailId,
            DuplicatedFromId = duplicatedFrom,
            DocType = request.DocType,
            WithoutGst = request.WithoutGst,
        };
        await ApplyAsync(q, request, isNew: true, ct);
        // Only administrators choose the date; for everyone else it is the day the quotation is made.
        if (!user.IsAdmin) q.Date = fy.Today();
        db.Quotations.Add(q);
        if (q.WithoutGst)
        {
            // "Without GST" applies to one quotation only: the user's switch turns itself off.
            var owner = await db.Users.FirstOrDefaultAsync(u => u.Username == user.UserName, ct);
            if (owner is not null) owner.WithoutGstNext = false;
        }
        audit.Add(user.UserName, user.Machine, "QuotationCreated", "Quotation", q.Id.ToString(),
            $"Source={source}; Customer={q.Buyer.Name}; Lines={q.Lines.Count}");

        if (assignNumber)
        {
            var year = RequireActiveYear(q.Date);
            await numbering.AssignAndSaveAsync(db, q, year, () => db.SaveChangesAsync(ct), ct);
        }
        else
        {
            await db.SaveChangesAsync(ct);
        }
        return ToDto(q);
    }

    public async Task<QuotationDto> UpdateAsync(Guid id, SaveQuotationRequest request, UserContext user, CancellationToken ct)
    {
        var q = await LoadAsync(id, ct);
        if (q.Status == QuotationStatus.Cancelled) throw new QuotationException(409, ApiErrorCodes.InvalidState, "A cancelled quotation cannot be edited.");
        EnsureEditable(q, user);
        if (request.Revision != q.Revision)
        {
            throw new QuotationException(409, ApiErrorCodes.Concurrency,
                $"This quotation was changed by {q.UpdatedBy ?? q.CreatedBy} after you opened it. Reload it and apply your changes again.");
        }

        var wasFinal = q.Status is QuotationStatus.Approved or QuotationStatus.Generated;
        var date = q.Date;
        await ApplyAsync(q, request, isNew: false, ct);
        // Users keep the date the quotation was first saved with; administrators may change it.
        if (!user.IsAdmin) q.Date = date;
        if (q.Number is not null && q.FinancialYearStart is { } start && !new FinancialYear(start, settings.Quotation.FinancialYearStartMonth).Contains(q.Date))
        {
            throw new QuotationException(400, ApiErrorCodes.Validation,
                $"The date must stay within financial year {new FinancialYear(start, settings.Quotation.FinancialYearStartMonth).Label} of number {q.Number}.");
        }
        if (wasFinal)
        {
            // Editing an approved quotation sends it back to draft; it must be approved again.
            q.Status = QuotationStatus.Draft;
            q.ApprovedBy = null;
            q.ApprovedUtc = null;
        }
        q.UpdatedBy = user.UserName;
        q.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
        q.Revision++;
        audit.Add(user.UserName, user.Machine, wasFinal ? "QuotationEditedAfterApproval" : "QuotationUpdated", "Quotation", q.Id.ToString(), q.Number ?? "");

        // A person saving the quotation (including an AI draft under review) gives it its number.
        if (q.Number is null)
        {
            await numbering.AssignAndSaveAsync(db, q, RequireActiveYear(q.Date), () => SaveAsync(ct), ct);
        }
        else
        {
            await SaveAsync(ct);
        }
        return ToDto(q);
    }

    /// <summary>
    /// Human approval: refresh Tally data, validate, check data freshness, assign a number if needed,
    /// then generate the PDF. An approved quotation is saved even if PDF generation fails.
    /// </summary>
    public async Task<QuotationDto> ApproveAsync(Guid id, ApproveRequest request, UserContext user, CancellationToken ct)
    {
        var q = await LoadAsync(id, ct);
        if (q.Status == QuotationStatus.Cancelled) throw new QuotationException(409, ApiErrorCodes.InvalidState, "A cancelled quotation cannot be approved.");
        if (q.Status is not (QuotationStatus.Approved or QuotationStatus.Generated)) EnsureEditable(q, user);
        if (request.Revision is { } rev && rev != q.Revision)
        {
            throw new QuotationException(409, ApiErrorCodes.Concurrency, "This quotation was changed after you opened it. Reload it first.");
        }

        // 1. Make data as current as possible: a quick incremental sync when Tally is reachable.
        var liveRefreshed = false;
        if (runtime.TallyConnected && !sync.IsRunning)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var run = await sync.RunAsync(SyncKind.Incremental, user.UserName, timeout.Token);
                liveRefreshed = run.Status is SyncStatus.Succeeded or SyncStatus.Skipped;
            }
            catch (SyncAlreadyRunningException) { }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }

        // 2. Re-apply authoritative Tally values; if the Tally rate moved, the user must review.
        var rateChanges = await RefreshFromTallyAsync(q, ct);
        if (rateChanges.Count > 0)
        {
            Recalculate(q);
            q.Revision++;
            q.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
            q.UpdatedBy = "tally-sync";
            audit.Add(user.UserName, user.Machine, "RatesRefreshedFromTally", "Quotation", q.Id.ToString(), string.Join("; ", rateChanges));
            await SaveAsync(ct);
            throw new QuotationException(409, ApiErrorCodes.RatesChanged,
                "Selling rates changed in Tally since this quotation was prepared. The rates were updated — please review and approve again.",
                rateChanges);
        }

        // 3. Validate.
        var issues = Validate(q);
        if (issues.Count > 0)
        {
            throw new QuotationException(400, ApiErrorCodes.Validation, "The quotation cannot be approved yet:", issues.Select(i => i.ToString()).ToList());
        }

        // 4. Data freshness.
        string? warning = null;
        if (!liveRefreshed)
        {
            var lastSync = await status.LastSuccessfulSyncUtcAsync(ct);
            var staleAfter = TimeSpan.FromHours(settings.Quotation.StaleDataHours);
            var age = lastSync is null ? (TimeSpan?)null : clock.GetUtcNow().UtcDateTime - lastSync.Value;
            if (age is null || age > staleAfter || !runtime.TallyConnected)
            {
                warning = age is null
                    ? "Tally data has never been synchronized."
                    : $"Live Tally data could not be retrieved; using data synchronized {FormatAge(age.Value)} ago.";
                if (!request.OverrideStaleData)
                {
                    throw new QuotationException(409, ApiErrorCodes.StaleData,
                        warning + (settings.Quotation.AllowStaleDataOverride ? " Approve anyway?" : " Approval is blocked until Tally is available."));
                }
                if (!settings.Quotation.AllowStaleDataOverride)
                {
                    throw new QuotationException(409, ApiErrorCodes.StaleData, warning + " Overriding is disabled in Settings.");
                }
            }
        }

        // 5. Approve (+ number) and save before rendering, so an approval is never lost.
        var now = clock.GetUtcNow().UtcDateTime;
        q.Status = QuotationStatus.Approved;
        q.ApprovedBy = user.UserName;
        q.ApprovedUtc = now;
        q.DataFreshnessWarning = warning;
        q.Revision++;
        audit.Add(user.UserName, user.Machine, warning is null ? "QuotationApproved" : "QuotationApprovedWithStaleData", "Quotation",
            q.Id.ToString(), warning ?? "");
        if (q.Number is null)
        {
            await numbering.AssignAndSaveAsync(db, q, RequireActiveYear(q.Date), () => SaveAsync(ct), ct);
        }
        else
        {
            await SaveAsync(ct);
        }

        // The approved rates become the items' last rates (not stored in Tally).
        if (await lastRates.ApplyApprovedQuotationAsync(db, q, ct)) sync.NotifyDataChanged();

        // 6. PDF.
        await GeneratePdfAsync(q, user, ct);
        return await WithNoticesAsync(q, ct);
    }

    /// <summary>Generates (or regenerates) the PDF of an approved quotation.</summary>
    public async Task GeneratePdfAsync(QuotationHeader q, UserContext user, CancellationToken ct)
    {
        if (q.Status is not (QuotationStatus.Approved or QuotationStatus.Generated))
        {
            throw new QuotationException(409, ApiErrorCodes.InvalidState, "Only approved quotations can be printed.");
        }
        try
        {
            var bytes = renderer.Render(q, settings.Company, settings.Quotation, Stamp(user), master: MasterOf(q));
            q.PdfPath = await pdfStorage.SaveAsync(q, bytes, ct);
            q.PdfError = null;
            q.Status = QuotationStatus.Generated;
            audit.Add(user.UserName, user.Machine, "PdfGenerated", "Quotation", q.Id.ToString(), q.PdfPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "PDF generation failed for {Number}", q.Number);
            q.PdfError = "PDF generation failed: " + ex.Message;
            audit.Add(user.UserName, user.Machine, "PdfFailed", "Quotation", q.Id.ToString(), ex.Message);
        }
        await SaveAsync(ct);
    }

    public async Task<QuotationDto> CancelAsync(Guid id, UserContext user, CancellationToken ct)
    {
        var q = await LoadAsync(id, ct);
        if (q.Status == QuotationStatus.Cancelled) return ToDto(q);
        EnsureEditable(q, user);
        q.Status = QuotationStatus.Cancelled;
        q.CancelledBy = user.UserName;
        q.CancelledUtc = clock.GetUtcNow().UtcDateTime;
        q.Revision++;
        audit.Add(user.UserName, user.Machine, "QuotationCancelled", "Quotation", q.Id.ToString(), q.Number ?? "");
        await SaveAsync(ct);
        return ToDto(q);
    }

    public static readonly TimeSpan DeletedRetention = TimeSpan.FromDays(30);

    /// <summary>
    /// Administrator: moves a quotation or proforma invoice to the Deleted list (restorable for 30 days). The latest
    /// number of a series is released so the next document gets it again; an older number leaves a gap.
    /// </summary>
    public async Task DeleteAsync(Guid id, UserContext user, CancellationToken ct)
    {
        if (!user.IsAdmin) throw new QuotationException(403, ApiErrorCodes.Forbidden, "Only an administrator can delete quotations.");
        var q = await LoadAsync(id, ct);
        var number = q.Number ?? "(not numbered)";
        q.DeletedUtc = clock.GetUtcNow().UtcDateTime;
        q.DeletedBy = user.UserName;
        q.Revision++;
        audit.Add(user.UserName, user.Machine, "QuotationDeleted", "Quotation", q.Id.ToString(),
            $"{number}; {q.Buyer.Name}; {q.GrandTotal:0.00}");
        var released = await numbering.ReleaseIfLatestAsync(db, q, () => SaveAsync(ct), ct);
        if (released) log.LogInformation("{Number} deleted by {User}; the number will be reused", number, user.UserName);
    }

    public async Task<List<DeletedQuotationDto>> DeletedAsync(CancellationToken ct)
    {
        var list = await db.Quotations.IgnoreQueryFilters().AsNoTracking().Where(q => q.DeletedUtc != null)
            .OrderByDescending(q => q.DeletedUtc).ToListAsync(ct);
        var result = new List<DeletedQuotationDto>();
        foreach (var q in list)
        {
            result.Add(new DeletedQuotationDto(q.Id, q.Number ?? q.DeletedNumber ?? "(not numbered)", q.DocType, q.Date, q.Buyer.Name,
                q.GrandTotal, q.DeletedBy ?? "", q.DeletedUtc!.Value, q.DeletedUtc.Value + DeletedRetention,
                await NumberingService.IsNumberFreeAsync(db, q, ct)));
        }
        return result;
    }

    /// <summary>Administrator: brings a deleted quotation back (with its number if it is still free).</summary>
    public async Task<RestoreResultDto> RestoreAsync(Guid id, UserContext user, CancellationToken ct)
    {
        if (!user.IsAdmin) throw new QuotationException(403, ApiErrorCodes.Forbidden, "Only an administrator can restore quotations.");
        var q = await db.Quotations.IgnoreQueryFilters().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id && x.DeletedUtc != null, ct)
                ?? throw new QuotationException(404, "NOT_FOUND", "Deleted quotation not found.");
        var oldNumber = q.Number ?? q.DeletedNumber;
        q.DeletedUtc = null;
        q.DeletedBy = null;
        q.Revision++;
        var kept = await numbering.ReclaimAsync(db, q, async () =>
        {
            if (q.Number is null && q.DeletedNumber is null && oldNumber is not null)
            {
                // The number went to a new quotation meanwhile: back as a draft that gets the next number when saved.
                q.Status = QuotationStatus.Draft;
                q.ApprovedBy = null;
                q.ApprovedUtc = null;
                q.PdfPath = null;
            }
            audit.Add(user.UserName, user.Machine, "QuotationRestored", "Quotation", q.Id.ToString(), oldNumber ?? "");
            await SaveAsync(ct);
        }, ct);
        var message = kept
            ? $"{q.Number ?? "The quotation"} restored."
            : $"Restored as a draft without a number ({oldNumber} was given to another quotation); it gets the next number when saved.";
        return new RestoreResultDto(ToDto(q), kept, message);
    }

    /// <summary>Administrator: removes a deleted quotation for good (also its PDF).</summary>
    public async Task PurgeAsync(Guid id, UserContext user, CancellationToken ct)
    {
        if (!user.IsAdmin) throw new QuotationException(403, ApiErrorCodes.Forbidden, "Only an administrator can delete quotations.");
        var q = await db.Quotations.IgnoreQueryFilters().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id && x.DeletedUtc != null, ct)
                ?? throw new QuotationException(404, "NOT_FOUND", "Deleted quotation not found.");
        audit.Add(user.UserName, user.Machine, "QuotationDeletedPermanently", "Quotation", q.Id.ToString(),
            $"{q.Number ?? q.DeletedNumber}; {q.Buyer.Name}; {q.GrandTotal:0.00}");
        await RemoveAsync(q, ct);
    }

    /// <summary>Removes quotations deleted more than 30 days ago (run daily).</summary>
    public async Task<int> PurgeExpiredAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - DeletedRetention;
        var expired = await db.Quotations.IgnoreQueryFilters().Include(x => x.Lines)
            .Where(x => x.DeletedUtc != null && x.DeletedUtc < cutoff).ToListAsync(ct);
        foreach (var q in expired)
        {
            audit.Add("system", "server", "QuotationDeletedPermanently", "Quotation", q.Id.ToString(),
                $"{q.Number ?? q.DeletedNumber}; {q.Buyer.Name}; after 30 days in the Deleted list");
            await RemoveAsync(q, ct);
        }
        return expired.Count;
    }

    private async Task RemoveAsync(QuotationHeader q, CancellationToken ct)
    {
        var pdf = pdfStorage.FullPath(q.PdfPath);
        db.QuotationLines.RemoveRange(q.Lines);
        db.Quotations.Remove(q);
        await SaveAsync(ct);
        // The archived PDF is removed only if no other quotation now uses that file name.
        if (pdf is not null && File.Exists(pdf) && !await db.Quotations.IgnoreQueryFilters().AnyAsync(x => x.PdfPath == q.PdfPath, ct))
        {
            try { File.Delete(pdf); } catch (IOException) { }
        }
    }

    /// <summary>New quotation (new number, today's date) with the same customer, products and descriptions.</summary>
    public async Task<QuotationDto> DuplicateAsync(Guid id, UserContext user, CancellationToken ct)
    {
        var source = await LoadAsync(id, ct);
        var request = ToRequest(source);
        request.Date = fy.Today();
        request.BuyersReference = "";
        request.BuyersReferenceDate = null;
        request.WithoutGst = false;
        var refresh = settings.Quotation.RefreshRatesOnDuplicate;
        foreach (var line in request.Lines)
        {
            if (refresh || line.TallyRate is null || line.Rate == line.TallyRate) line.Rate = -1; // marker: use current Tally rate
            line.TallyRate = null;
        }
        var dto = await CreateAsync(request, QuotationSource.Manual, user, ct, duplicatedFrom: source.Id);
        audit.Add(user.UserName, user.Machine, "QuotationDuplicated", "Quotation", dto.Id.ToString(), $"From {source.Number}");
        await SaveAsync(ct);
        return dto;
    }

    /// <summary>Renders the current state with a DRAFT watermark (unapproved) without saving anything.</summary>
    public async Task<byte[]> PreviewPdfAsync(Guid id, PdfQuotationRenderer pdf, CancellationToken ct, UserContext? user = null)
    {
        var q = await LoadAsync(id, ct);
        var stamp = user is null ? null : Stamp(user);
        if (q.Status is QuotationStatus.Generated or QuotationStatus.Approved)
        {
            return pdf.Render(q, settings.Company, settings.Quotation, stamp, master: MasterOf(q));
        }
        var provisional = q.Number ?? (await numbering.PreviewAsync(db, fy.GetActive().Year, ct, q.DocType)).Number + " (provisional)";
        return pdf.RenderPreview(q, settings.Company, settings.Quotation, provisional, stamp, MasterOf(q));
    }

    /// <summary>
    /// The PDF of an approved quotation as printed now by <paramref name="user"/> ("Printed on … By: …").
    /// The copy archived at approval is used only if rendering fails.
    /// </summary>
    public async Task<byte[]?> PrintPdfAsync(Guid id, UserContext user, CancellationToken ct, byte[]? seal = null)
    {
        var q = await LoadAsync(id, ct);
        if (q.Status is not (QuotationStatus.Approved or QuotationStatus.Generated)) return null;
        if (seal is not null)
        {
            // A sealed copy is always rendered fresh (never the unsealed archive) and recorded.
            var bytes = renderer.Render(q, settings.Company, settings.Quotation, Stamp(user), seal, MasterOf(q));
            audit.Add(user.UserName, user.Machine, "PdfSealed", "Quotation", q.Id.ToString(), q.Number ?? "");
            await SaveAsync(ct);
            return bytes;
        }
        try
        {
            return renderer.Render(q, settings.Company, settings.Quotation, Stamp(user), master: MasterOf(q));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Printing {Number} fresh failed; using the archived PDF", q.Number);
            var path = pdfStorage.FullPath(q.PdfPath);
            return path is null ? null : await File.ReadAllBytesAsync(path, ct);
        }
    }

    /// <summary>Server local time (the office's time zone) and the logged-in user.</summary>
    private Quotation.Pdf.PrintStamp Stamp(UserContext user) => new(clock.GetLocalNow().DateTime, user.UserName);

    /// <summary>The buyer's Tally ledger as last synchronized (null for a party typed by hand).</summary>
    public PartyMaster? MasterOf(QuotationHeader q) =>
        q.CustomerId is { } id && db.Customers.AsNoTracking().FirstOrDefault(c => c.Id == id) is { } c ? PartyMaster.From(c) : null;

    public async Task<NextNumberDto> PreviewNextNumberAsync(DateOnly? date, CancellationToken ct, DocumentType type = DocumentType.Quotation) =>
        await numbering.PreviewAsync(db, RequireActiveYear(date ?? fy.Today()), ct, type);

    /// <summary>
    /// New proforma invoice (own number series, today's date) from a quotation: same customer, references,
    /// lines and rates. The quotation itself is unchanged. Nothing is created in Tally.
    /// </summary>
    public async Task<QuotationDto> ConvertToProformaAsync(Guid id, UserContext user, CancellationToken ct)
    {
        var source = await LoadAsync(id, ct);
        if (source.DocType == DocumentType.ProformaInvoice)
            throw new QuotationException(409, ApiErrorCodes.InvalidState, "This is already a proforma invoice.");
        var request = ToRequest(source);
        request.DocType = DocumentType.ProformaInvoice;
        request.Date = fy.Today();
        if (string.IsNullOrWhiteSpace(request.OtherReferences) && source.Number is not null)
            request.OtherReferences = "Our quotation " + source.Number;
        var dto = await CreateAsync(request, QuotationSource.Manual, user, ct, duplicatedFrom: source.Id);
        audit.Add(user.UserName, user.Machine, "ConvertedToProforma", "Quotation", dto.Id.ToString(), $"From {source.Number} to {dto.Number}");
        await SaveAsync(ct);
        return dto;
    }

    public List<ValidationIssue> Validate(QuotationHeader q) => QuotationValidator.Validate(new ValidationInput(
        q.CustomerId,
        q.Buyer.Name,
        q.Date,
        fy.GetActive().Year,
        q.Lines.OrderBy(l => l.LineNo).Select(l => new ValidationLine(l.LineNo, l.ProductId, l.ItemName, l.Hsn, l.GstRate,
            l.Quantity, l.Unit, l.Rate, l.DiscountPercent)).ToList(),
        q.PackingForwarding,
        settings.Quotation.RequireHsn));

    // ------------------------------------------------------------------ mapping

    private async Task ApplyAsync(QuotationHeader q, SaveQuotationRequest r, bool isNew, CancellationToken ct)
    {
        q.Date = r.Date == default ? fy.Today() : r.Date;
        q.BuyersReference = Trim(r.BuyersReference, 200);
        q.BuyersReferenceDate = r.BuyersReferenceDate;
        q.DispatchedThrough = Trim(r.DispatchedThrough, 200);
        q.PaymentTerms = Trim(r.PaymentTerms, 500);
        q.OtherReferences = Trim(r.OtherReferences, 500);
        q.Destination = Trim(r.Destination, 200);
        q.TermsOfDelivery = Trim(r.TermsOfDelivery, 500);
        q.DeliveryNote = Trim(r.DeliveryNote, 200);
        q.DispatchDocNo = Trim(r.DispatchDocNo, 200);
        q.DeliveryNoteDate = r.DeliveryNoteDate;
        q.Remarks = Trim(r.Remarks, 2000);
        q.TermsAndConditions = Trim(r.TermsAndConditions, 4000);
        q.PreparedBy = Trim(r.PreparedBy, 100);
        q.VerifiedBy = Trim(r.VerifiedBy, 100);
        q.PackingForwardingPercent = r.PackingForwardingPercent is > 0 ? r.PackingForwardingPercent : null;
        q.PackingForwarding = r.PackingForwarding;
        // The GST presentation in force when the quotation is saved; reprints use the same, so totals never change.
        q.TaxPresentation = settings.Quotation.TaxPresentation;
        q.RoundOffTotal = settings.Quotation.RoundOffTotal;

        // Buyer: Tally is authoritative for GSTIN and state; name/address/contact may be edited.
        q.CustomerId = r.CustomerId;
        q.Buyer = ToSnapshot(r.Buyer);
        if (r.CustomerId is { } customerId)
        {
            var c = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == customerId, ct)
                    ?? throw new QuotationException(400, ApiErrorCodes.Validation, "The selected customer no longer exists in Tally.");
            if (string.IsNullOrWhiteSpace(q.Buyer.Name)) q.Buyer.Name = c.MailingName.Length > 0 ? c.MailingName : c.Name;
            if (string.IsNullOrWhiteSpace(q.Buyer.Address)) q.Buyer.Address = c.Address;
            q.Buyer.Gstin = c.Gstin;
            q.Buyer.StateName = c.StateName;
            q.Buyer.StateCode = c.StateCode;
            if (string.IsNullOrWhiteSpace(q.Buyer.ContactPerson)) q.Buyer.ContactPerson = c.ContactPerson;
            if (string.IsNullOrWhiteSpace(q.Buyer.Phone)) q.Buyer.Phone = c.Mobile.Length > 0 ? c.Mobile : c.Phone;
            if (string.IsNullOrWhiteSpace(q.Buyer.Email)) q.Buyer.Email = c.Email;
        }
        q.ConsigneeSameAsBuyer = r.ConsigneeSameAsBuyer;
        q.Consignee = r.ConsigneeSameAsBuyer ? Copy(q.Buyer) : ToSnapshot(r.Consignee);
        if (!r.ConsigneeSameAsBuyer && q.Consignee.StateCode.Length == 0)
        {
            q.Consignee.StateCode = Core.Gst.IndianStates.ResolveCode(q.Consignee.Gstin, q.Consignee.StateName);
        }

        // Lines: product identity and tax data from Tally; quantity, rate override, discount, description from the user.
        var previous = q.Lines.ToDictionary(l => l.LineNo);
        var productIds = r.Lines.Where(l => l.ProductId is not null).Select(l => l.ProductId!.Value).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var lines = new List<QuotationLine>();
        var lineNo = 0;
        foreach (var rl in r.Lines)
        {
            lineNo++;
            var line = new QuotationLine
            {
                LineNo = lineNo,
                ProductId = rl.ProductId,
                Description = Trim(rl.Description, 2000),
                DueOn = Trim(rl.DueOn, 50),
                Quantity = rl.Quantity,
                DiscountPercent = rl.DiscountPercent,
                Rate = rl.Rate,
                ItemName = Trim(rl.ItemName, 300),
                PartNumber = Trim(rl.PartNumber, 100),
                Hsn = Trim(rl.Hsn, 10),
                GstRate = rl.GstRate,
                Unit = Trim(rl.Unit, 20),
            };
            if (rl.ProductId is { } pid)
            {
                if (!products.TryGetValue(pid, out var p))
                {
                    throw new QuotationException(400, ApiErrorCodes.Validation, $"Line {lineNo}: the selected product no longer exists in Tally.");
                }
                line.ItemName = p.Name;
                line.PartNumber = p.PartNumber;
                if (p.Hsn.Length > 0) line.Hsn = p.Hsn;
                if (p.GstRate is not null) line.GstRate = p.GstRate;
                if (p.Unit.Length > 0) line.Unit = p.Unit;

                var old = previous.Values.FirstOrDefault(o => o.ProductId == pid && o.LineNo == lineNo)
                          ?? previous.Values.FirstOrDefault(o => o.ProductId == pid);
                line.TallyRate = rl.TallyRate is not null && old?.TallyRate == rl.TallyRate
                    ? rl.TallyRate
                    : SuggestedRate(p, q.Date);
                if (line.Rate < 0) line.Rate = line.TallyRate ?? 0; // "use Tally rate" marker
            }
            lines.Add(line);
        }
        if (!isNew)
        {
            db.QuotationLines.RemoveRange(q.Lines);
        }
        q.Lines = lines;
        Recalculate(q);
    }

    /// <summary>The calculation input of a saved quotation: its own GST presentation, round off and "Without GST".</summary>
    public static CalcInput CalcInputOf(QuotationHeader q, string companyStateCode, bool usePercent = true)
    {
        var placeOfSupply = q.Consignee.StateCode.Length > 0 ? q.Consignee.StateCode : q.Buyer.StateCode;
        var interState = companyStateCode.Length > 0 && placeOfSupply.Length > 0 && companyStateCode != placeOfSupply;
        return new CalcInput(
            q.Lines.OrderBy(l => l.LineNo).Select(l => new CalcLine(l.Quantity, l.Rate, l.DiscountPercent, l.GstRate ?? 0, l.Unit)).ToList(),
            q.PackingForwarding, usePercent ? q.PackingForwardingPercent : null, q.TaxPresentation, q.RoundOffTotal, interState, q.WithoutGst);
    }

    /// <summary>
    /// Users who are not administrators may change a quotation only for <see cref="QuotationSettings.UserEditDays"/>
    /// days after it was first saved (counted in office days); administrators always.
    /// </summary>
    private void EnsureEditable(QuotationHeader q, UserContext user)
    {
        if (user.IsAdmin) return;
        var until = EditableUntil(q);
        if (DateOnly.FromDateTime(clock.GetLocalNow().DateTime) > until)
        {
            throw new QuotationException(403, ApiErrorCodes.Locked,
                $"{q.Number ?? "This quotation"} is locked: it was made more than {settings.Quotation.UserEditDays} days ago " +
                $"(editable until {until:dd-MMM-yyyy}). Ask an administrator to change it, or use Duplicate.");
        }
    }

    public DateOnly EditableUntil(QuotationHeader q)
    {
        var created = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(q.CreatedUtc, DateTimeKind.Utc), clock.LocalTimeZone);
        return DateOnly.FromDateTime(created).AddDays(Math.Max(settings.Quotation.UserEditDays, 0));
    }

    private void Recalculate(QuotationHeader q)
    {
        var qs = settings.Quotation;
        var result = QuotationCalculator.Calculate(CalcInputOf(q, settings.Company.StateCode));
        var ordered = q.Lines.OrderBy(l => l.LineNo).ToList();
        for (var i = 0; i < ordered.Count; i++) ordered[i].Amount = result.LineAmounts[i];
        q.Subtotal = result.Subtotal;
        q.PackingForwarding = result.PackingForwarding;
        q.TaxTotal = result.TaxTotal;
        q.RoundOff = result.RoundOff;
        q.GrandTotal = result.GrandTotal;
        q.TotalQuantity = result.TotalQuantity;
        q.AmountInWords = IndianFormat.AmountInWords(result.GrandTotal, qs.CurrencyWordsPrefix);
    }

    /// <summary>Re-applies Tally's current HSN/GST/unit and detects Tally rate changes on non-overridden lines.</summary>
    private async Task<List<string>> RefreshFromTallyAsync(QuotationHeader q, CancellationToken ct)
    {
        var changes = new List<string>();
        var ids = q.Lines.Where(l => l.ProductId is not null).Select(l => l.ProductId!.Value).ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        foreach (var line in q.Lines.Where(l => l.ProductId is not null))
        {
            if (!products.TryGetValue(line.ProductId!.Value, out var p)) continue;
            if (p.IsDeleted) changes.Add($"Line {line.LineNo}: {p.Name} was deleted in Tally.");
            if (p.Hsn.Length > 0) line.Hsn = p.Hsn;
            if (p.GstRate is not null) line.GstRate = p.GstRate;
            if (p.Unit.Length > 0) line.Unit = p.Unit;
            var current = RateOn(p, q.Date);
            if (current is not null && line.TallyRate is not null && current != line.TallyRate)
            {
                var overridden = line.Rate != line.TallyRate;
                changes.Add($"Line {line.LineNo}: Tally rate for {p.Name} changed from {line.TallyRate:0.00} to {current:0.00}" +
                            (overridden ? $" (your rate {line.Rate:0.00} kept)." : "."));
                if (!overridden) line.Rate = current.Value;
                line.TallyRate = current;
            }
        }
        return changes;
    }

    /// <summary>Rate offered for a new line: the Tally selling rate on the date, else the item's last quoted/sold rate.</summary>
    public decimal? SuggestedRate(Product p, DateOnly date) => RateOn(p, date) ?? p.LastRate;

    /// <summary>Tally selling rate applicable on <paramref name="date"/> (re-resolved from raw Tally data).</summary>
    public decimal? RateOn(Product p, DateOnly date)
    {
        if (p.TallyDataJson.Length == 0) return p.Rate;
        var data = JsonSerializer.Deserialize<ProductTallyData>(p.TallyDataJson, TallySyncService.Json);
        if (data is null) return p.Rate;
        var t = settings.Tally;
        return TallyRateResolver.Resolve(data.StandardPrices, data.PriceLevels, t.RateSource, t.PriceLevel, date).Rate;
    }

    private FinancialYear RequireActiveYear(DateOnly date)
    {
        var active = fy.GetActive().Year;
        if (!active.Contains(date))
        {
            throw new QuotationException(400, ApiErrorCodes.Validation,
                $"The date {date:dd-MMM-yyyy} is outside the active financial year {active.Label}. " +
                "Change the date, or change the active period in Tally (or the override in Settings).");
        }
        return active;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new QuotationException(409, ApiErrorCodes.Concurrency, "The quotation was changed by someone else. Reload it and try again.");
        }
    }

    private static string FormatAge(TimeSpan age) =>
        age.TotalDays >= 1 ? $"{age.TotalDays:0} day(s)" : age.TotalHours >= 1 ? $"{age.TotalHours:0} hour(s)" : $"{age.TotalMinutes:0} minute(s)";

    private static string Trim(string? s, int max) => s is null ? "" : s.Trim() is var t && t.Length > max ? t[..max] : s.Trim();

    private static PartySnapshot ToSnapshot(PartyDto p) => new()
    {
        Name = Trim(p.Name, 300),
        Address = Trim(p.Address, 1000),
        Gstin = Trim(p.Gstin, 15).ToUpperInvariant(),
        StateName = Trim(p.StateName, 100),
        StateCode = Trim(p.StateCode, 2),
        ContactPerson = Trim(p.ContactPerson, 200),
        Phone = Trim(p.Phone, 100),
        Email = Trim(p.Email, 200),
    };

    private static PartySnapshot Copy(PartySnapshot p) => new()
    {
        Name = p.Name, Address = p.Address, Gstin = p.Gstin, StateName = p.StateName, StateCode = p.StateCode,
        ContactPerson = p.ContactPerson, Phone = p.Phone, Email = p.Email,
    };

    public static PartyDto ToDto(PartySnapshot p) => new()
    {
        Name = p.Name, Address = p.Address, Gstin = p.Gstin, StateName = p.StateName, StateCode = p.StateCode,
        ContactPerson = p.ContactPerson, Phone = p.Phone, Email = p.Email,
    };

    public static SaveQuotationRequest ToRequest(QuotationHeader q) => new()
    {
        Revision = q.Revision,
        Date = q.Date,
        CustomerId = q.CustomerId,
        Buyer = ToDto(q.Buyer),
        Consignee = ToDto(q.Consignee),
        ConsigneeSameAsBuyer = q.ConsigneeSameAsBuyer,
        BuyersReference = q.BuyersReference,
        BuyersReferenceDate = q.BuyersReferenceDate,
        DispatchedThrough = q.DispatchedThrough,
        PaymentTerms = q.PaymentTerms,
        OtherReferences = q.OtherReferences,
        Destination = q.Destination,
        TermsOfDelivery = q.TermsOfDelivery,
        DocType = q.DocType,
        DeliveryNote = q.DeliveryNote,
        DispatchDocNo = q.DispatchDocNo,
        DeliveryNoteDate = q.DeliveryNoteDate,
        Remarks = q.Remarks,
        TermsAndConditions = q.TermsAndConditions,
        PackingForwarding = q.PackingForwarding,
        PackingForwardingPercent = q.PackingForwardingPercent,
        PreparedBy = q.PreparedBy,
        VerifiedBy = q.VerifiedBy,
        WithoutGst = q.WithoutGst,
        Lines = q.Lines.OrderBy(l => l.LineNo).Select(ToDto).ToList(),
    };

    public static QuotationLineDto ToDto(QuotationLine l) => new()
    {
        LineNo = l.LineNo, ProductId = l.ProductId, ItemName = l.ItemName, PartNumber = l.PartNumber, Description = l.Description, Hsn = l.Hsn,
        GstRate = l.GstRate, DueOn = l.DueOn, Quantity = l.Quantity, Unit = l.Unit, Rate = l.Rate, TallyRate = l.TallyRate,
        DiscountPercent = l.DiscountPercent, Amount = l.Amount,
    };

    public QuotationDto ToDto(QuotationHeader q)
    {
        var request = ToRequest(q);
        var qs = settings.Quotation;
        var calc = QuotationCalculator.Calculate(CalcInputOf(q, settings.Company.StateCode, usePercent: false));
        return new QuotationDto
        {
            Id = q.Id,
            Number = q.Number,
            DocType = q.DocType,
            FinancialYear = q.FinancialYearStart is { } s ? new FinancialYear(s, qs.FinancialYearStartMonth).Label : null,
            Date = q.Date,
            Status = q.Status,
            Source = q.Source,
            Revision = q.Revision,
            CustomerId = q.CustomerId,
            Buyer = request.Buyer,
            Consignee = request.Consignee,
            ConsigneeSameAsBuyer = q.ConsigneeSameAsBuyer,
            BuyersReference = q.BuyersReference,
            BuyersReferenceDate = q.BuyersReferenceDate,
            DispatchedThrough = q.DispatchedThrough,
            PaymentTerms = q.PaymentTerms,
            OtherReferences = q.OtherReferences,
            Destination = q.Destination,
            TermsOfDelivery = q.TermsOfDelivery,
            DeliveryNote = q.DeliveryNote,
            DispatchDocNo = q.DispatchDocNo,
            DeliveryNoteDate = q.DeliveryNoteDate,
            Remarks = q.Remarks,
            TermsAndConditions = q.TermsAndConditions,
            PackingForwarding = q.PackingForwarding,
            PackingForwardingPercent = q.PackingForwardingPercent,
            PreparedBy = q.PreparedBy,
            VerifiedBy = q.VerifiedBy,
            WithoutGst = q.WithoutGst,
            Lines = request.Lines,
            Subtotal = q.Subtotal,
            Taxes = calc.Taxes.Select(t => new TaxLineDto(t.Name, t.RatePercent, t.TaxableValue, t.Amount)).ToList(),
            TaxTotal = q.TaxTotal,
            RoundOff = q.RoundOff,
            GrandTotal = q.GrandTotal,
            TotalQuantity = q.TotalQuantity,
            TotalQuantityUnit = calc.TotalQuantityUnit,
            AmountInWords = q.AmountInWords,
            HasPdf = q.PdfPath is not null,
            PdfError = q.PdfError,
            DataFreshnessWarning = q.DataFreshnessWarning,
            DuplicatedFromId = q.DuplicatedFromId,
            SourceEmailId = q.SourceEmailId,
            CreatedBy = q.CreatedBy,
            CreatedUtc = q.CreatedUtc,
            UpdatedBy = q.UpdatedBy,
            UpdatedUtc = q.UpdatedUtc,
            ApprovedBy = q.ApprovedBy,
            ApprovedUtc = q.ApprovedUtc,
            CancelledBy = q.CancelledBy,
            CancelledUtc = q.CancelledUtc,
            EditableUntil = EditableUntil(q),
        };
    }
}
