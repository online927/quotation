using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quotation.Contracts;
using Quotation.Data;
using Quotation.Data.Entities;
using Quotation.Pdf;

namespace Quotation.Server.Services;

/// <summary>
/// Round 36: PDF datasheets of Tally items (Products page). Kept in the data folder as datasheets\{product id}.pdf; with
/// "Add to the quotation PDF" ticked, quoting the item adds its pages after the quotation's own pages. Tally is not touched.
/// </summary>
public sealed class DatasheetService(IOptions<ServerOptions> options)
{
    public const int MaxBytes = 15 * 1024 * 1024;
    private readonly string _folder = Path.Combine(options.Value.ResolveDataDirectory(), "datasheets");

    public string PathOf(int productId) => Path.Combine(_folder, productId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".pdf");

    public static DatasheetDto ToDto(ProductDatasheet d) => new(d.ProductId, d.FileName, d.Pages, d.Bytes, d.AddToPdf, d.UploadedBy, d.UploadedUtc);

    public async Task<DatasheetDto> SaveAsync(QuotationDbContext db, int productId, string fileName, byte[] pdf, string user, DateTime nowUtc, CancellationToken ct)
    {
        if (pdf.Length == 0 || pdf.Length > MaxBytes)
            throw new QuotationException(400, ApiErrorCodes.Validation, "The datasheet must be a PDF of at most 15 MB.");
        if (PdfAppender.PageCount(pdf) is not { } pages || pages == 0)
            throw new QuotationException(400, ApiErrorCodes.Validation, "This file is not a PDF that can be read (or it has a password).");
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct))
            throw new QuotationException(404, ApiErrorCodes.Validation, "This item is not in Tally now.");
        Directory.CreateDirectory(_folder);
        var path = PathOf(productId);
        await File.WriteAllBytesAsync(path + ".tmp", pdf, ct);
        File.Move(path + ".tmp", path, overwrite: true);
        var row = await db.Datasheets.FirstOrDefaultAsync(d => d.ProductId == productId, ct);
        if (row is null)
        {
            row = new ProductDatasheet { ProductId = productId };
            db.Datasheets.Add(row);
        }
        row.FileName = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "datasheet.pdf" : fileName.Trim());
        if (row.FileName.Length > 200) row.FileName = row.FileName[..200];
        row.Pages = pages;
        row.Bytes = pdf.Length;
        row.UploadedBy = user;
        row.UploadedUtc = nowUtc;
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    public async Task RemoveAsync(QuotationDbContext db, int productId, CancellationToken ct)
    {
        await db.Datasheets.Where(d => d.ProductId == productId).ExecuteDeleteAsync(ct);
        try { File.Delete(PathOf(productId)); } catch (IOException) { }
    }

    /// <summary>The quotation PDF followed by the datasheets (ticked "Add to the quotation PDF") of its items, each once, in line order.</summary>
    public byte[] Append(QuotationDbContext db, QuotationHeader q, byte[] pdf)
    {
        var ids = q.Lines.OrderBy(l => l.LineNo).Where(l => l.ProductId is not null).Select(l => l.ProductId!.Value).Distinct().ToList();
        if (ids.Count == 0) return pdf;
        var ticked = db.Datasheets.AsNoTracking().Where(d => ids.Contains(d.ProductId) && d.AddToPdf).Select(d => d.ProductId).ToHashSet();
        if (ticked.Count == 0) return pdf;
        var extras = ids.Where(ticked.Contains).Select(PathOf).Where(File.Exists).Select(File.ReadAllBytes).ToList();
        return PdfAppender.Append(pdf, extras);
    }
}
