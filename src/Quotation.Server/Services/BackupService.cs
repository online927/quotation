using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Data;

namespace Quotation.Server.Services;

/// <summary>
/// Consistent online backups of the SQLite database (SQLite backup API — safe while the server is running): every few
/// hours (Settings > Backup &amp; updates, 3 by default), kept 30 days in DataDirectory\backups, and copied to a second
/// folder when one is set (USB drive, another PC, a cloud-synced folder). Also: health warnings for the dashboard, and
/// "Export everything" / "Restore from backup file" for moving or rebuilding the server.
/// </summary>
public sealed class BackupService(
    IServiceScopeFactory scopes,
    IOptions<ServerOptions> options,
    SettingsService settings,
    CatalogSearchService catalog,
    TimeProvider clock,
    ILogger<BackupService> log)
{
    public const int KeepDays = 30;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string DataDirectory => options.Value.ResolveDataDirectory();
    public string Folder => Path.Combine(DataDirectory, "backups");

    /// <summary>When the last copy reached the second folder, and why the last attempt failed (null = it worked).</summary>
    public DateTime? LastCopyLocal { get; private set; }
    public string? LastCopyError { get; private set; }

    public async Task<string> BackupNowAsync(CancellationToken ct, string prefix = "quotation")
    {
        Directory.CreateDirectory(Folder);
        var target = Path.Combine(Folder, $"{prefix}-{clock.GetLocalNow():yyyyMMdd-HHmmss}.db");
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
            var source = (SqliteConnection)db.Database.GetDbConnection();
            await source.OpenAsync(ct);
            try
            {
                using var destination = new SqliteConnection(DatabaseInitializer.BuildConnectionString(target));
                await destination.OpenAsync(ct);
                source.BackupDatabase(destination);
            }
            finally
            {
                await source.CloseAsync();
            }
        }
        SqliteConnection.ClearAllPools();
        Prune(Folder);
        log.LogInformation("Database backup written to {Path}", target);
        CopyToSecondFolder(target);
        return target;
    }

    /// <summary>A copy in the second folder, so a dead disk or a stolen PC does not take the backups with it.</summary>
    private void CopyToSecondFolder(string backup)
    {
        var folder = settings.Backup.CopyFolder.Trim();
        if (folder.Length == 0) return;
        try
        {
            Directory.CreateDirectory(folder);
            File.Copy(backup, Path.Combine(folder, Path.GetFileName(backup)), overwrite: true);
            Prune(folder);
            LastCopyLocal = clock.GetLocalNow().DateTime;
            LastCopyError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            LastCopyError = ex.Message;
            log.LogWarning("Backup copy to {Folder} failed: {Message}", folder, ex.Message);
        }
    }

    public DateTime? LastBackupLocal() =>
        Directory.Exists(Folder) ? Directory.EnumerateFiles(Folder, "quotation-*.db").Select(File.GetLastWriteTime).DefaultIfEmpty().Max() is var d && d != default ? d : null : null;

    private void Prune(string folder)
    {
        var cutoff = clock.GetLocalNow().DateTime.AddDays(-KeepDays);
        foreach (var f in Directory.EnumerateFiles(folder, "quotation-*.db").Where(f => File.GetLastWriteTime(f) < cutoff))
        {
            try { File.Delete(f); } catch (IOException) { }
        }
    }

    // ================================================================= dashboard warnings (A4)

    /// <summary>Things an administrator should look at: old or missing backups, a full disk, no Tally sync for a day.</summary>
    public async Task<List<HealthWarningDto>> HealthAsync(CancellationToken ct)
    {
        var list = new List<HealthWarningDto>();
        var now = clock.GetLocalNow().DateTime;
        var every = TimeSpan.FromHours(Math.Max(1, settings.Backup.EveryHours));
        var last = LastBackupLocal();
        if (last is null || now - last > every * 2)
            list.Add(new("bad", last is null ? "No database backup has been made yet." : $"Last database backup {Ago(now - last.Value)} ago."));
        var copyFolder = settings.Backup.CopyFolder.Trim();
        if (copyFolder.Length == 0)
            list.Add(new("warn", "No second backup copy is set up. Choose a USB drive or another PC in Settings > Backup & updates."));
        else if (LastCopyError is { } error)
            list.Add(new("bad", $"Second backup folder not reachable ({copyFolder}): {error}"));
        else if (LastCopyLocal is { } copied && now - copied > every * 2)
            list.Add(new("bad", $"Last backup copy {Ago(now - copied)} ago."));
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(DataDirectory))!);
            var freeGb = drive.AvailableFreeSpace / 1024d / 1024 / 1024;
            if (freeGb < 2) list.Add(new("bad", $"Server disk almost full: {freeGb:0.0} GB free."));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
            var lastSync = await db.SyncRuns.Where(r => r.Kind != SyncKind.LastRates && (r.Status == SyncStatus.Succeeded || r.Status == SyncStatus.Skipped))
                .OrderByDescending(r => r.StartedUtc).Select(r => r.FinishedUtc).FirstOrDefaultAsync(ct);
            var age = lastSync is null ? (TimeSpan?)null : clock.GetUtcNow().UtcDateTime - lastSync.Value;
            if (age is null || age > TimeSpan.FromHours(24))
                list.Add(new("warn", age is null ? "Tally has never been synchronized." : $"No Tally sync for {Ago(age.Value)}. Is Tally open on its PC?"));
        }
        return list;
    }

    private static string Ago(TimeSpan t) =>
        t.TotalDays >= 2 ? $"{(int)t.TotalDays} days" : t.TotalHours >= 1 ? $"{(int)t.TotalHours} hours" : $"{Math.Max(1, (int)t.TotalMinutes)} minutes";

    // ================================================================= export everything / restore (A5)

    /// <summary>
    /// One file with everything needed to rebuild the server: the database, the PDFs, the seal and the logo.
    /// The Claude key and the Gmail connection are not included (they are locked to the server PC).
    /// </summary>
    public async Task<string> ExportAsync(CancellationToken ct)
    {
        var database = await BackupNowAsync(ct);
        var file = Path.Combine(Path.GetTempPath(), $"TSQuotation-backup-{clock.GetLocalNow():dd-MMM-yyyy-HHmm}.tsq");
        if (File.Exists(file)) File.Delete(file);
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(database, "quotation.db", CompressionLevel.Optimal);
            AddFolder(zip, Path.Combine(DataDirectory, "pdf"), "pdf");
            AddFolder(zip, Path.Combine(DataDirectory, "seal"), "seal");
            if (settings.Company.LogoPath is { Length: > 0 } logo && File.Exists(logo))
                zip.CreateEntryFromFile(logo, "logo/" + Path.GetFileName(logo), CompressionLevel.Fastest);
            var manifest = zip.CreateEntry("manifest.json");
            await using (var w = manifest.Open())
            {
                await JsonSerializer.SerializeAsync(w, new
                {
                    Created = clock.GetLocalNow().DateTime,
                    Server = Environment.MachineName,
                    Version = typeof(BackupService).Assembly.GetName().Version?.ToString(),
                }, cancellationToken: ct);
            }
        }
        log.LogInformation("Export written to {File}", file);
        return file;
    }

    private static void AddFolder(ZipArchive zip, string folder, string prefix)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            zip.CreateEntryFromFile(f, prefix + "/" + Path.GetRelativePath(folder, f).Replace('\\', '/'), CompressionLevel.Fastest);
    }

    /// <summary>
    /// Restores from an export (.tsq) or a plain database backup (.db). The current database is backed up first
    /// ("quotation-before-restore-…"). Users must sign in again afterwards.
    /// </summary>
    public async Task<RestoreSummaryDto> RestoreAsync(string uploaded, string user, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        var work = Path.Combine(Path.GetTempPath(), "tsq-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string database;
            ZipArchive? zip = null;
            if (IsSqlite(uploaded))
            {
                database = uploaded;
            }
            else
            {
                try { zip = ZipFile.OpenRead(uploaded); }
                catch (InvalidDataException) { throw new QuotationException(400, ApiErrorCodes.Validation, "This is not a TS Quotation backup file (.tsq or .db)."); }
                var entry = zip.GetEntry("quotation.db") ?? throw new QuotationException(400, ApiErrorCodes.Validation, "The backup file has no database in it.");
                database = Path.Combine(work, "quotation.db");
                entry.ExtractToFile(database);
            }
            using (zip)
            {
                if (!LooksLikeOurDatabase(database))
                    throw new QuotationException(400, ApiErrorCodes.Validation, "The file is not a TS Quotation database.");

                await BackupNowAsync(ct, "quotation-before-restore");

                // Copy the backup into the live database (SQLite backup API), then bring it to this version's layout.
                using (var scope = scopes.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
                    var livePath = ((SqliteConnection)db.Database.GetDbConnection()).DataSource;
                    SqliteConnection.ClearAllPools();
                    using (var source = new SqliteConnection(DatabaseInitializer.BuildConnectionString(database)))
                    using (var destination = new SqliteConnection(DatabaseInitializer.BuildConnectionString(livePath)))
                    {
                        await source.OpenAsync(ct);
                        await destination.OpenAsync(ct);
                        source.BackupDatabase(destination);
                    }
                    SqliteConnection.ClearAllPools();
                    await DatabaseInitializer.InitializeAsync(db, ct);
                }

                var pdfs = 0;
                bool seal = false, logo = false;
                if (zip is not null)
                {
                    foreach (var entry in zip.Entries.Where(e => e.Name.Length > 0))
                    {
                        string? target = null;
                        if (entry.FullName.StartsWith("pdf/", StringComparison.Ordinal)) { target = Path.Combine(DataDirectory, "pdf", entry.FullName[4..]); pdfs++; }
                        else if (entry.FullName.StartsWith("seal/", StringComparison.Ordinal)) { target = Path.Combine(DataDirectory, "seal", entry.FullName[5..]); seal = true; }
                        else if (entry.FullName.StartsWith("logo/", StringComparison.Ordinal)) { target = Path.Combine(DataDirectory, entry.Name); logo = true; }
                        if (target is null) continue;
                        var full = Path.GetFullPath(target);
                        if (!full.StartsWith(Path.GetFullPath(DataDirectory), StringComparison.OrdinalIgnoreCase)) continue; // never outside the data folder
                        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                        entry.ExtractToFile(full, overwrite: true);
                    }
                }

                // Everything cached from the old database is read again.
                settings.ClearCache();
                if (logo && zip!.Entries.FirstOrDefault(e => e.FullName.StartsWith("logo/", StringComparison.Ordinal) && e.Name.Length > 0) is { } logoEntry
                    && settings.Company is { } company && (company.LogoPath is null || !File.Exists(company.LogoPath)))
                {
                    company.LogoPath = Path.Combine(DataDirectory, logoEntry.Name);
                    await settings.SaveAsync(SettingsService.CompanyKey, company, user, ct);
                }
                catalog.Reset();

                using var after = scopes.CreateScope();
                var restored = after.ServiceProvider.GetRequiredService<QuotationDbContext>();
                var count = await restored.Quotations.CountAsync(ct);
                after.ServiceProvider.GetRequiredService<AuditService>().Add(user, "", "DatabaseRestored", "Database", "",
                    $"{count} quotations, {pdfs} PDFs{(seal ? ", seal" : "")}{(logo ? ", logo" : "")}");
                await restored.SaveChangesAsync(ct);
                log.LogWarning("Database restored by {User}: {Count} quotations, {Pdfs} PDFs", user, count, pdfs);
                return new RestoreSummaryDto(count, pdfs, seal, logo);
            }
        }
        finally
        {
            _gate.Release();
            try { Directory.Delete(work, recursive: true); } catch (IOException) { }
        }
    }

    private static bool IsSqlite(string file)
    {
        var header = new byte[16];
        using var s = File.OpenRead(file);
        return s.Read(header, 0, 16) == 16 && System.Text.Encoding.ASCII.GetString(header).StartsWith("SQLite format 3", StringComparison.Ordinal);
    }

    private static bool LooksLikeOurDatabase(string file)
    {
        try
        {
            using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('Quotations','Users','__EFMigrationsHistory')";
            return Convert.ToInt32(cmd.ExecuteScalar()) == 3;
        }
        catch (SqliteException)
        {
            return false;
        }
    }
}

public sealed class BackupWorker(BackupService backups, SettingsService settings, IServiceScopeFactory scopes, IOptions<ServerOptions> options,
    ILogger<BackupWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.EnableBackgroundWorkers) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var last = backups.LastBackupLocal();
                var every = TimeSpan.FromHours(Math.Max(1, settings.Backup.EveryHours));
                if (last is null || DateTime.Now - last > every) await backups.BackupNowAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Database backup failed");
            }
            try
            {
                // Quotations deleted more than 30 days ago leave the Deleted list for good.
                using var scope = scopes.CreateScope();
                var purged = await scope.ServiceProvider.GetRequiredService<QuotationService>().PurgeExpiredAsync(stoppingToken);
                if (purged > 0) log.LogInformation("{Count} deleted quotation(s) removed after 30 days", purged);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Removing expired deleted quotations failed");
            }
            try { await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
