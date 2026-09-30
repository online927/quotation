using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quotation.Contracts;
using Quotation.Data;
using Quotation.Server.Infrastructure;
using Quotation.Server.Services;

namespace Quotation.Server.Endpoints;

/// <summary>Administrator diagnostics: system state and the (credential-redacted) server log.</summary>
public static class DiagnosticsEndpoints
{
    public static void MapDiagnosticsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/diagnostics").RequireAuthorization(SystemEndpoints.AdminPolicy);

        g.MapGet("/", async (QuotationDbContext db, StatusService status, IOptions<ServerOptions> options, CancellationToken ct) =>
        {
            var dir = options.Value.ResolveDataDirectory();
            var dbFile = db.Database.GetDbConnection().DataSource;
            long dbBytes = 0;
            foreach (var f in new[] { dbFile, dbFile + "-wal" })
            {
                if (!string.IsNullOrEmpty(f) && File.Exists(f)) dbBytes += new FileInfo(f).Length;
            }
            var pdfDir = Path.Combine(dir, "pdf");
            var logDir = Path.Combine(dir, "logs");
            return new DiagnosticsDto(
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "",
                RuntimeInformation.FrameworkDescription,
                RuntimeInformation.OSDescription,
                Environment.MachineName,
                dir,
                dbBytes,
                await db.Products.CountAsync(p => !p.IsDeleted, ct),
                await db.Customers.CountAsync(c => !c.IsDeleted, ct),
                await db.Quotations.CountAsync(ct),
                Directory.Exists(pdfDir) ? Directory.EnumerateFiles(pdfDir, "*.pdf", SearchOption.AllDirectories).Count() : 0,
                await status.GetAsync(ct),
                Directory.Exists(logDir)
                    ? Directory.EnumerateFiles(logDir, "*.log").Select(Path.GetFileName).OfType<string>().OrderDescending().ToList()
                    : []);
        });

        g.MapGet("/log", (IOptions<ServerOptions> options, string? file, int? lines) =>
        {
            var logDir = Path.Combine(options.Value.ResolveDataDirectory(), "logs");
            if (!Directory.Exists(logDir)) return Results.Text("");
            var name = string.IsNullOrWhiteSpace(file)
                ? Directory.EnumerateFiles(logDir, "*.log").OrderDescending().FirstOrDefault()
                : Path.Combine(logDir, Path.GetFileName(file));
            if (name is null || !File.Exists(name)) return Results.Text("");
            // The log is open for writing by the server: share read/write.
            using var stream = new FileStream(name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var all = reader.ReadToEnd().Split('\n');
            var tail = string.Join('\n', all.TakeLast(Math.Clamp(lines ?? 300, 10, 5000)));
            return Results.Text(RedactingFormatter.Redact(tail), "text/plain");
        });
    }
}
