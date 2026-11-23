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
        // Every client PC reports its slow requests (time from click to answer); administrators read the list.
        app.MapPost("/api/diagnostics/slow", (SlowRequestReport r, SlowRequestLog slow, HttpContext ctx, TimeProvider clock) =>
        {
            if (r.TotalMs < SlowRequestLog.Threshold.TotalMilliseconds || r.TotalMs > TimeSpan.FromHours(1).TotalMilliseconds) return Results.NoContent();
            // The client sends "GET /api/…"; the list shows it in words ("Search products", "Customer details" …).
            var parts = r.What.Split(' ', 2);
            var what = parts.Length == 2 ? SlowRequestLog.Describe(parts[0], parts[1]) : r.What;
            slow.ClientReport(r with { What = what }, ctx.User.UserName(), ctx.Machine(), clock.GetUtcNow().UtcDateTime);
            return Results.NoContent();
        }).RequireAuthorization();

        // Client updates (A2): every client asks which client version the server has, and downloads it from the server.
        app.MapGet("/api/update/client", (IOptions<ServerOptions> options, SettingsService settings) =>
        {
            var folder = options.Value.ResolveClientUpdateFolder();
            var setup = Path.Combine(folder, "Quotation-Setup.exe");
            var versionFile = Path.Combine(folder, "version.txt");
            if (!File.Exists(setup) || !File.Exists(versionFile)) return Results.Ok(new ClientUpdateDto(null, false, 0));
            return Results.Ok(new ClientUpdateDto(File.ReadAllText(versionFile).Trim(), settings.Backup.ClientsMustUpdate, new FileInfo(setup).Length));
        }).RequireAuthorization();
        app.MapGet("/api/update/client/download", (IOptions<ServerOptions> options) =>
        {
            var setup = Path.Combine(options.Value.ResolveClientUpdateFolder(), "Quotation-Setup.exe");
            return File.Exists(setup) ? Results.File(setup, "application/octet-stream", "Quotation-Setup.exe") : Results.NotFound();
        }).RequireAuthorization();

        var g = app.MapGroup("/api/diagnostics").RequireAuthorization(SystemEndpoints.AdminPolicy);
        g.MapGet("/slow", (SlowRequestLog slow) => slow.Recent());

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

        // Dashboard warnings for administrators: backups, second copy, disk space, Tally sync.
        g.MapGet("/health", (BackupService backups, CancellationToken ct) => backups.HealthAsync(ct));

        // Export everything: one file (database, PDFs, seal, logo) to move or rebuild the server.
        g.MapGet("/export", async (BackupService backups, AuditService audit, HttpContext ctx, CancellationToken ct) =>
        {
            var file = await backups.ExportAsync(ct);
            await audit.WriteAsync(ctx.User.UserName(), ctx.Machine(), "DatabaseExported", "Database", Path.GetFileName(file), ct: ct);
            var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.DeleteOnClose);
            return Results.File(stream, "application/octet-stream", Path.GetFileName(file));
        });

        // Round 36: the evening summary of a day (empty = today), and "send now" (e-mail).
        g.MapGet("/summary", async (string? day, EveningSummaryService summary, CancellationToken ct) =>
            await summary.BuildAsync(DateOnly.TryParseExact(day ?? "", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d) ? d : summary.Today, ct));

        // Round 36: "Who did what" on a day (yyyy-MM-dd, server's local date; empty = today), for one user or everyone.
        g.MapGet("/activity", async (string? day, string? user, ActivityService activity, TimeProvider clock, CancellationToken ct) =>
        {
            var date = DateOnly.TryParseExact(day ?? "", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d) ? d : DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            return await activity.DayAsync(date, user, ct);
        });

        // Round 36: a month's approved PDFs in one ZIP (month = "2026-11"; quotations and/or proforma invoices).
        g.MapGet("/month-pdfs", async (string? month, bool? quotations, bool? proforma, MonthPdfZip zips, AuditService audit, HttpContext ctx, CancellationToken ct) =>
        {
            if (!DateOnly.TryParseExact((month ?? "") + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var first))
                return Results.Json(new ApiError("Pick a month."), statusCode: 400);
            var q = quotations ?? true;
            var p = proforma ?? false;
            if (!q && !p) return Results.Json(new ApiError("Tick Quotations or Proforma invoices."), statusCode: 400);
            var made = await zips.MakeAsync(first.Year, first.Month, q, p, ct);
            if (made is not { } zip) return Results.Json(new ApiError($"No approved PDFs in {first:MMMM yyyy}."), statusCode: 404);
            await audit.WriteAsync(ctx.User.UserName(), ctx.Machine(), "MonthPdfsDownloaded", "Database", $"{zip.FileName} ({zip.Count} PDFs)", ct: ct);
            return Results.File(zip.Zip, "application/zip", zip.FileName);
        });

        // Restore from an export (.tsq) or a database backup (.db). The current database is backed up first.
        g.MapPost("/restore", async (HttpContext ctx, BackupService backups, CancellationToken ct) =>
        {
            if (ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                limit.MaxRequestBodySize = null; // exports with many PDFs can be large
            var temp = Path.Combine(Path.GetTempPath(), "tsq-upload-" + Guid.NewGuid().ToString("N"));
            try
            {
                await using (var file = File.Create(temp)) await ctx.Request.Body.CopyToAsync(file, ct);
                return Results.Ok(await backups.RestoreAsync(temp, ctx.User.UserName(), ct));
            }
            catch (QuotationException ex)
            {
                return Results.Json(new ApiError(ex.Message, ex.Details, ex.Code), statusCode: ex.Status);
            }
            finally
            {
                try { File.Delete(temp); } catch (IOException) { }
            }
        });

        g.MapPost("/backup", async (BackupService backups, AuditService audit, HttpContext ctx, CancellationToken ct) =>
        {
            var path = await backups.BackupNowAsync(ct);
            await audit.WriteAsync(ctx.User.UserName(), ctx.Machine(), "DatabaseBackup", "Database", Path.GetFileName(path), ct: ct);
            return Results.Ok(new { path });
        });

        // Google Drive backup (Settings → Backup & updates): the status, and "Back up now" (a new backup, then everything new).
        g.MapGet("/drive", (DriveBackupService drive, CancellationToken ct) => drive.StatusAsync(ct));
        g.MapPost("/drive", async (DriveBackupService drive, AuditService audit, HttpContext ctx, CancellationToken ct) =>
        {
            var status = await drive.CopyNowAsync(ctx.User.UserName(), makeBackup: true, ct);
            await audit.WriteAsync(ctx.User.UserName(), ctx.Machine(), "DriveBackup", "Database", status.Error ?? status.LastCopyText, ct: ct);
            return status;
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
