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
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Allocates the next number for <paramref name="fy"/>, then runs <paramref name="save"/>, atomically.</summary>
    public async Task AssignAndSaveAsync(QuotationDbContext db, QuotationHeader quotation, FinancialYear fy,
        Func<Task> save, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var (series, sequence, number) = await NextAsync(db, fy, ct);
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
    public async Task<NextNumberDto> PreviewAsync(QuotationDbContext db, FinancialYear fy, CancellationToken ct)
    {
        var (_, sequence, number) = await NextAsync(db, fy, ct, track: false);
        return new NextNumberDto(fy.Label, number, sequence, Provisional: true);
    }

    public async Task<NumberSeriesDto> GetSeriesAsync(QuotationDbContext db, FinancialYear fy, CancellationToken ct)
    {
        var (_, sequence, number) = await NextAsync(db, fy, ct, track: false);
        var maxUsed = await MaxUsedAsync(db, fy, ct);
        return new NumberSeriesDto(fy.Label, fy.StartYear, sequence, maxUsed + 1, number);
    }

    /// <summary>
    /// Sets the next sequence for a financial year, e.g. to continue the existing Tally series
    /// (last Tally quotation TSQ2526-3247 → next 3248). Cannot go below a number already used.
    /// </summary>
    public async Task<NumberSeriesDto> SetNextAsync(QuotationDbContext db, FinancialYear fy, int next, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var maxUsed = await MaxUsedAsync(db, fy, ct);
            if (next <= maxUsed) throw new InvalidOperationException($"Number {next} is already used or passed; the next number must be at least {maxUsed + 1}.");
            if (next < 1) throw new InvalidOperationException("The next number must be at least 1.");
            var series = await db.NumberSeries.FirstOrDefaultAsync(s => s.SeriesKey == SeriesKey && s.FinancialYearStart == fy.StartYear, ct);
            if (series is null)
            {
                series = new NumberSeries { SeriesKey = SeriesKey, FinancialYearStart = fy.StartYear };
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
        return await GetSeriesAsync(db, fy, ct);
    }

    private async Task<(NumberSeries Series, int Sequence, string Number)> NextAsync(QuotationDbContext db, FinancialYear fy,
        CancellationToken ct, bool track = true)
    {
        var pattern = settings.Quotation.NumberPattern;
        var series = await db.NumberSeries.FirstOrDefaultAsync(s => s.SeriesKey == SeriesKey && s.FinancialYearStart == fy.StartYear, ct);
        var maxUsed = await MaxUsedAsync(db, fy, ct);
        if (series is null)
        {
            series = new NumberSeries
            {
                SeriesKey = SeriesKey,
                FinancialYearStart = fy.StartYear,
                NextSequence = Math.Max(settings.Quotation.DefaultStartSequence, 1),
            };
            if (track) db.NumberSeries.Add(series);
        }
        var sequence = Math.Max(series.NextSequence, maxUsed + 1);
        var number = QuotationNumberFormatter.Format(pattern, fy, sequence);
        // Skip numbers that already exist (e.g. after a pattern change).
        while (await db.Quotations.AnyAsync(q => q.Number == number, ct))
        {
            sequence++;
            number = QuotationNumberFormatter.Format(pattern, fy, sequence);
        }
        return (series, sequence, number);
    }

    private static async Task<int> MaxUsedAsync(QuotationDbContext db, FinancialYear fy, CancellationToken ct) =>
        await db.Quotations.Where(q => q.FinancialYearStart == fy.StartYear).MaxAsync(q => q.Sequence, ct) ?? 0;
}
