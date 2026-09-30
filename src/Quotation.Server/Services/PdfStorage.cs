using Microsoft.Extensions.Options;
using Quotation.Core.Numbering;
using Quotation.Data.Entities;

namespace Quotation.Server.Services;

/// <summary>Stores generated PDFs under DataDirectory\pdf\{financial year}\{number}.pdf.</summary>
public sealed class PdfStorage(IOptions<ServerOptions> options)
{
    private readonly string _root = Path.Combine(options.Value.ResolveDataDirectory(), "pdf");

    public async Task<string> SaveAsync(QuotationHeader q, byte[] pdf, CancellationToken ct)
    {
        var folder = Path.Combine(_root, (q.FinancialYearStart ?? q.Date.Year).ToString());
        Directory.CreateDirectory(folder);
        var name = QuotationNumberFormatter.ToFileName(q.Number ?? q.Id.ToString("N")) + ".pdf";
        var path = Path.Combine(folder, name);
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, pdf, ct);
        File.Move(temp, path, overwrite: true); // atomic replace: never a half-written PDF
        return Path.GetRelativePath(_root, path);
    }

    public string? FullPath(string? relative)
    {
        if (string.IsNullOrEmpty(relative)) return null;
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        return full.StartsWith(Path.GetFullPath(_root), StringComparison.Ordinal) && File.Exists(full) ? full : null;
    }
}
