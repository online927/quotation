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
    /// <summary>Renders the quotation and returns the PDF bytes.</summary>
    byte[] Render(QuotationHeader quotation, CompanySettings company, QuotationSettings settings);
}

public sealed record UserContext(string UserName, string DisplayName, string Machine);

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
    IQuotationDocumentRenderer renderer,
    PdfStorage pdfStorage,
    TimeProvider clock,
    ILogger<QuotationService> log)
{
    public async Task<QuotationDto> GetAsync(Guid id, CancellationToken ct) => ToDto(await LoadAsync(id, ct));

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
        };
        await ApplyAsync(q, request, isNew: true, ct);
        db.Quotations.Add(q);
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
        if (request.Revision != q.Revision)
        {
            throw new QuotationException(409, ApiErrorCodes.Concurrency,
                $"This quotation was changed by {q.UpdatedBy ?? q.CreatedBy} after you opened it. Reload it and apply your changes again.");
        }

        var wasFinal = q.Status is QuotationStatus.Approved or QuotationStatus.Generated;
        await ApplyAsync(q, request, isNew: false, ct);
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

        if (q.Number is null && q.Source == QuotationSource.Manual)
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

        // 6. PDF.
        await GeneratePdfAsync(q, user, ct);
        return ToDto(q);
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
            var bytes = renderer.Render(q, settings.Company, settings.Quotation);
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
        q.Status = QuotationStatus.Cancelled;
        q.CancelledBy = user.UserName;
        q.CancelledUtc = clock.GetUtcNow().UtcDateTime;
        q.Revision++;
        audit.Add(user.UserName, user.Machine, "QuotationCancelled", "Quotation", q.Id.ToString(), q.Number ?? "");
        await SaveAsync(ct);
        return ToDto(q);
    }

    /// <summary>New quotation (new number, today's date) with the same customer, products and descriptions.</summary>
    public async Task<QuotationDto> DuplicateAsync(Guid id, UserContext user, CancellationToken ct)
    {
        var source = await LoadAsync(id, ct);
        var request = ToRequest(source);
        request.Date = fy.Today();
        request.BuyersReference = "";
        request.BuyersReferenceDate = null;
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
    public async Task<byte[]> PreviewPdfAsync(Guid id, PdfQuotationRenderer pdf, CancellationToken ct)
    {
        var q = await LoadAsync(id, ct);
        if (q.Status is QuotationStatus.Generated or QuotationStatus.Approved)
        {
            return pdf.Render(q, settings.Company, settings.Quotation);
        }
        var provisional = q.Number ?? (await numbering.PreviewAsync(db, fy.GetActive().Year, ct)).Number + " (provisional)";
        return pdf.RenderPreview(q, settings.Company, settings.Quotation, provisional);
    }

    public async Task<NextNumberDto> PreviewNextNumberAsync(DateOnly? date, CancellationToken ct) =>
        await numbering.PreviewAsync(db, RequireActiveYear(date ?? fy.Today()), ct);

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
        q.Remarks = Trim(r.Remarks, 2000);
        q.TermsAndConditions = Trim(r.TermsAndConditions, 4000);
        q.PreparedBy = Trim(r.PreparedBy, 100);
        q.VerifiedBy = Trim(r.VerifiedBy, 100);
        q.PackingForwardingPercent = r.PackingForwardingPercent is > 0 ? r.PackingForwardingPercent : null;
        q.PackingForwarding = r.PackingForwarding;

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
                if (p.Hsn.Length > 0) line.Hsn = p.Hsn;
                if (p.GstRate is not null) line.GstRate = p.GstRate;
                if (p.Unit.Length > 0) line.Unit = p.Unit;

                var old = previous.Values.FirstOrDefault(o => o.ProductId == pid && o.LineNo == lineNo)
                          ?? previous.Values.FirstOrDefault(o => o.ProductId == pid);
                line.TallyRate = rl.TallyRate is not null && old?.TallyRate == rl.TallyRate
                    ? rl.TallyRate
                    : RateOn(p, q.Date);
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

    private void Recalculate(QuotationHeader q)
    {
        var qs = settings.Quotation;
        var companyState = settings.Company.StateCode;
        var placeOfSupply = q.Consignee.StateCode.Length > 0 ? q.Consignee.StateCode : q.Buyer.StateCode;
        var interState = companyState.Length > 0 && placeOfSupply.Length > 0 && companyState != placeOfSupply;
        var result = QuotationCalculator.Calculate(new CalcInput(
            q.Lines.OrderBy(l => l.LineNo).Select(l => new CalcLine(l.Quantity, l.Rate, l.DiscountPercent, l.GstRate ?? 0, l.Unit)).ToList(),
            q.PackingForwarding, q.PackingForwardingPercent, qs.TaxPresentation, qs.RoundOffTotal, interState));
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
        Remarks = q.Remarks,
        TermsAndConditions = q.TermsAndConditions,
        PackingForwarding = q.PackingForwarding,
        PackingForwardingPercent = q.PackingForwardingPercent,
        PreparedBy = q.PreparedBy,
        VerifiedBy = q.VerifiedBy,
        Lines = q.Lines.OrderBy(l => l.LineNo).Select(ToDto).ToList(),
    };

    public static QuotationLineDto ToDto(QuotationLine l) => new()
    {
        LineNo = l.LineNo, ProductId = l.ProductId, ItemName = l.ItemName, Description = l.Description, Hsn = l.Hsn,
        GstRate = l.GstRate, DueOn = l.DueOn, Quantity = l.Quantity, Unit = l.Unit, Rate = l.Rate, TallyRate = l.TallyRate,
        DiscountPercent = l.DiscountPercent, Amount = l.Amount,
    };

    public QuotationDto ToDto(QuotationHeader q)
    {
        var request = ToRequest(q);
        var qs = settings.Quotation;
        var companyState = settings.Company.StateCode;
        var placeOfSupply = q.Consignee.StateCode.Length > 0 ? q.Consignee.StateCode : q.Buyer.StateCode;
        var calc = QuotationCalculator.Calculate(new CalcInput(
            q.Lines.OrderBy(l => l.LineNo).Select(l => new CalcLine(l.Quantity, l.Rate, l.DiscountPercent, l.GstRate ?? 0, l.Unit)).ToList(),
            q.PackingForwarding, null, qs.TaxPresentation, qs.RoundOffTotal,
            companyState.Length > 0 && placeOfSupply.Length > 0 && companyState != placeOfSupply));
        return new QuotationDto
        {
            Id = q.Id,
            Number = q.Number,
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
            Remarks = q.Remarks,
            TermsAndConditions = q.TermsAndConditions,
            PackingForwarding = q.PackingForwarding,
            PackingForwardingPercent = q.PackingForwardingPercent,
            PreparedBy = q.PreparedBy,
            VerifiedBy = q.VerifiedBy,
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
        };
    }
}
