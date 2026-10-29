using Microsoft.EntityFrameworkCore;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Core.Numbering;
using Quotation.Data;
using Quotation.Data.Entities;

namespace Quotation.Server.Services;

/// <summary>
/// Central quotation-number allocation. Numbers are allocated only here, on the server, inside the
/// same database transaction that saves the quotation, while holding a process-wide lock. A unique
/// index on Quotations.Number is the final safeguard. Several client PCs can therefore never receive
/// the same number, and a failed save never consumes one.
/// </summary>
public sealed class NumberingService(SettingsService settings)
{
    public const string SeriesKey = "QUOTATION";
    public const string ProformaSeriesKey = "PROFORMA";

    private static string KeyOf(DocumentType type) => type == DocumentType.ProformaInvoice ? ProformaSeriesKey : SeriesKey;

    private string PatternOf(DocumentType type) =>
        type == DocumentType.ProformaInvoice ? settings.Quotation.ProformaNumberPattern : settings.Quotation.NumberPattern;

    private int StartOf(DocumentType type) =>
        Math.Max(type == DocumentType.ProformaInvoice ? settings.Quotation.ProformaDefaultStartSequence : settings.Quotation.DefaultStartSequence, 1);
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Allocates the next number for <paramref name="fy"/>, then runs <paramref name="save"/>, atomically.</summary>
    public async Task AssignAndSaveAsync(QuotationDbContext db, QuotationHeader quotation, FinancialYear fy,
        Func<Task> save, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var (series, sequence, number) = await NextAsync(db, fy, ct, quotation.DocType);
            series.NextSequence = sequence + 1;
            series.UpdatedUtc = DateTime.UtcNow;
            quotation.Number = number;
            quotation.Sequence = sequence;
            quotation.FinancialYearStart = fy.StartYear;
            await save();
            await tx.CommitAsync(ct);
        }
        catch
        {
            // Roll back the in-memory assignment so a retry allocates again.
            quotation.Number = null;
            quotation.Sequence = null;
            quotation.FinancialYearStart = null;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The number the next saved quotation would receive (not reserved).</summary>
    public async Task<NextNumberDto> PreviewAsync(QuotationDbContext db, FinancialYear fy, CancellationToken ct,
        DocumentType type = DocumentType.Quotation)
    {
        var (_, sequence, number) = await NextAsync(db, fy, ct, type, track: false);
        return new NextNumberDto(fy.Label, number, sequence, Provisional: true);
    }

    public async Task<NumberSeriesDto> GetSeriesAsync(QuotationDbContext db, FinancialYear fy, CancellationToken ct,
        DocumentType type = DocumentType.Quotation)
    {
        var (_, sequence, number) = await NextAsync(db, fy, ct, type, track: false);
        var maxUsed = await MaxUsedAsync(db, fy, type, ct);
        return new NumberSeriesDto(fy.Label, fy.StartYear, sequence, maxUsed + 1, number);
    }

    /// <summary>
    /// Sets the next sequence for a financial year, e.g. to continue the existing Tally series
    /// (last Tally quotation TSQ2526-3247 → next 3248). Cannot go below a number already used.
    /// </summary>
    public async Task<NumberSeriesDto> SetNextAsync(QuotationDbContext db, FinancialYear fy, int next, CancellationToken ct,
        DocumentType type = DocumentType.Quotation)
    {
        var key = KeyOf(type);
        await _gate.WaitAsync(ct);
        try
        {
            var maxUsed = await MaxUsedAsync(db, fy, type, ct);
            if (next <= maxUsed) throw new InvalidOperationException($"Number {next} is already used or passed; the next number must be at least {maxUsed + 1}.");
            if (next < 1) throw new InvalidOperationException("The next number must be at least 1.");
            var series = await db.NumberSeries.FirstOrDefaultAsync(s => s.SeriesKey == key && s.FinancialYearStart == fy.StartYear, ct);
            if (series is null)
            {
                series = new NumberSeries { SeriesKey = key, FinancialYearStart = fy.StartYear };
                db.NumberSeries.Add(series);
            }
            series.NextSequence = next;
            series.UpdatedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
        return await GetSeriesAsync(db, fy, ct, type);
    }

    private async Task<(NumberSeries Series, int Sequence, string Number)> NextAsync(QuotationDbContext db, FinancialYear fy,
        CancellationToken ct, DocumentType type, bool track = true)
    {
        var pattern = PatternOf(type);
        var key = KeyOf(type);
        var series = await db.NumberSeries.FirstOrDefaultAsync(s => s.SeriesKey == key && s.FinancialYearStart == fy.StartYear, ct);
        var maxUsed = await MaxUsedAsync(db, fy, type, ct);
        if (series is null)
        {
            series = new NumberSeries
            {
                SeriesKey = key,
                FinancialYearStart = fy.StartYear,
                NextSequence = StartOf(type),
            };
            if (track) db.NumberSeries.Add(series);
        }
        var sequence = Math.Max(series.NextSequence, maxUsed + 1);
        var number = QuotationNumberFormatter.Format(pattern, fy, sequence);
        // Skip numbers that already exist (e.g. after a pattern change).
        while (await db.Quotations.IgnoreQueryFilters().AnyAsync(q => q.Number == number, ct))
        {
            sequence++;
            number = QuotationNumberFormatter.Format(pattern, fy, sequence);
        }
        return (series, sequence, number);
    }

    // Deleted quotations that kept their number still hold it (IgnoreQueryFilters), so it is never issued twice.
    private static async Task<int> MaxUsedAsync(QuotationDbContext db, FinancialYear fy, DocumentType type, CancellationToken ct) =>
        await db.Quotations.IgnoreQueryFilters().Where(q => q.FinancialYearStart == fy.StartYear && q.DocType == type).MaxAsync(q => q.Sequence, ct) ?? 0;

    /// <summary>
    /// Deleting: when the quotation has the latest number of its series, the number is released so the next quotation
    /// gets it again (kept in DeletedNumber for a restore). An older number stays with the deleted quotation (a gap).
    /// Runs <paramref name="save"/> atomically with the change.
    /// </summary>
    public async Task<bool> ReleaseIfLatestAsync(QuotationDbContext db, QuotationHeader q, Func<Task> save, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var released = false;
            if (q.Sequence is { } sequence && q.FinancialYearStart is { } start)
            {
                var key = KeyOf(q.DocType);
                var series = await db.NumberSeries.FirstOrDefaultAsync(s => s.SeriesKey == key && s.FinancialYearStart == start, ct);
                var maxOther = await db.Quotations.IgnoreQueryFilters()
                    .Where(x => x.Id != q.Id && x.FinancialYearStart == start && x.DocType == q.DocType)
                    .MaxAsync(x => x.Sequence, ct) ?? 0;
                if (series is not null && series.NextSequence == sequence + 1 && maxOther < sequence)
                {
                    series.NextSequence = sequence;
                    series.UpdatedUtc = DateTime.UtcNow;
                    q.DeletedNumber = q.Number;
                    q.DeletedSequence = sequence;
                    q.Number = null;
                    q.Sequence = null;
                    released = true;
                }
            }
            await save();
            await tx.CommitAsync(ct);
            return released;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Restoring a deleted quotation that had released its number: it gets the number back if nobody has it yet,
    /// otherwise it stays un-numbered (it gets the next number when saved). Returns true when the number was kept.
    /// </summary>
    public async Task<bool> ReclaimAsync(QuotationDbContext db, QuotationHeader q, Func<Task> save, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var kept = q.Number is not null;
            if (q.Number is null && q.DeletedNumber is { } number && q.DeletedSequence is { } sequence && q.FinancialYearStart is { } start)
            {
                var taken = await db.Quotations.IgnoreQueryFilters().AnyAsync(x => x.Id != q.Id &&
                    (x.Number == number || (x.FinancialYearStart == start && x.DocType == q.DocType && x.Sequence == sequence)), ct);
                if (!taken)
                {
                    q.Number = number;
                    q.Sequence = sequence;
                    var key = KeyOf(q.DocType);
                    var series = await db.NumberSeries.FirstOrDefaultAsync(s => s.SeriesKey == key && s.FinancialYearStart == start, ct);
                    if (series is not null && series.NextSequence <= sequence)
                    {
                        series.NextSequence = sequence + 1;
                        series.UpdatedUtc = DateTime.UtcNow;
                    }
                    kept = true;
                }
                else
                {
                    q.FinancialYearStart = null;
                }
            }
            q.DeletedNumber = null;
            q.DeletedSequence = null;
            await save();
            await tx.CommitAsync(ct);
            return kept;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Whether <paramref name="number"/> is still free (used to warn before a restore).</summary>
    public static async Task<bool> IsNumberFreeAsync(QuotationDbContext db, QuotationHeader q, CancellationToken ct) =>
        q.DeletedNumber is not { } number || !await db.Quotations.IgnoreQueryFilters().AnyAsync(x => x.Id != q.Id &&
            (x.Number == number || (x.FinancialYearStart == q.FinancialYearStart && x.DocType == q.DocType && x.Sequence == q.DeletedSequence)), ct);
}
