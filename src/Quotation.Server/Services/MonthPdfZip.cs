using System.Globalization;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Quotation.Core.Domain;
using Quotation.Data;

namespace Quotation.Server.Services;

/// <summary>
/// Round 36: a month's approved PDFs in one ZIP (Diagnostics → Month's PDFs, administrators): quotations and/or proforma
/// invoices dated in that month, each as "Number Customer.pdf". Only reads the PDFs already made; Tally is not asked.
/// </summary>
public sealed class MonthPdfZip(QuotationDbContext db, PdfStorage pdfs)
{
    /// <summary>The ZIP's bytes and file name, or null when that month has no PDFs.</summary>
    public async Task<(byte[] Zip, string FileName, int Count)?> MakeAsync(int year, int month, bool quotations, bool proforma, CancellationToken ct)
    {
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1);
        var rows = await db.Quotations.AsNoTracking()
            .Where(q => q.PdfPath != null && q.Date >= from && q.Date < to
                        && (q.Status == QuotationStatus.Approved || q.Status == QuotationStatus.Generated)
                        && ((quotations && q.DocType == DocumentType.Quotation) || (proforma && q.DocType == DocumentType.ProformaInvoice)))
            .OrderBy(q => q.Date).ThenBy(q => q.Sequence)
            .Select(q => new { q.PdfPath, q.Number, Buyer = q.Buyer.Name })
            .ToListAsync(ct);
        using var memory = new MemoryStream();
        var count = 0;
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                if (pdfs.FullPath(r.PdfPath) is not { } full) continue;
                var name = Clean(((r.Number ?? Path.GetFileNameWithoutExtension(full)) + " " + r.Buyer.Trim()).Trim());
                var entry = name + ".pdf";
                for (var n = 2; !used.Add(entry); n++) entry = $"{name} ({n}).pdf";
                var e = zip.CreateEntry(entry, CompressionLevel.Optimal);
                await using var target = e.Open();
                await using var source = File.OpenRead(full);
                await source.CopyToAsync(target, ct);
                count++;
            }
        }
        if (count == 0) return null;
        var what = proforma && !quotations ? "Proforma" : "Quotations";
        return (memory.ToArray(), $"{what}-{from.ToString("yyyy-MM", CultureInfo.InvariantCulture)}.zip", count);
    }

    private static string Clean(string name) =>
        new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? ' ' : c).ToArray()).Trim();
}
