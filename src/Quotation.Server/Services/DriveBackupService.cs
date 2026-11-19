using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Data;
using Quotation.Gmail;

namespace Quotation.Server.Services;

public interface IDriveClientFactory
{
    /// <summary>The Google account (Settings → Gmail) is connected.</summary>
    bool Connected { get; }

    /// <summary>Null when the Google account is not connected.</summary>
    IDriveClient? Create();
}

public sealed class GoogleDriveClientFactory(SettingsService settings) : IDriveClientFactory
{
    public bool Connected => !string.IsNullOrWhiteSpace(settings.GetSecret(SettingsService.SecretGmailClient)) &&
                             !string.IsNullOrWhiteSpace(settings.GetSecret(SettingsService.SecretGmailToken));

    public IDriveClient? Create()
    {
        var client = settings.GetSecret(SettingsService.SecretGmailClient);
        var token = settings.GetSecret(SettingsService.SecretGmailToken);
        return string.IsNullOrWhiteSpace(client) || string.IsNullOrWhiteSpace(token) ? null : new GoogleDriveClient(client, token);
    }
}

/// <summary>
/// Google Drive backup (Settings → Backup &amp; updates): the newest database backup and the approved PDFs are copied to the
/// office Google Drive, in "TS Quotations" → "Database backups" and "Approved PDFs" → a folder per month. Uses the Google
/// account of the Gmail connection. Runs every 15 minutes (only when there is something new) and on "Back up now".
/// Never touches Tally. A copy that fails is tried again next time.
/// </summary>
public sealed class DriveBackupService(
    IDriveClientFactory drives,
    SettingsService settings,
    BackupService backups,
    PdfStorage pdfs,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<DriveBackupService> log)
{
    public const string StateKey = "drive-backup";
    public const string RootFolder = "TS Quotations";
    public const string BackupsFolder = "Database backups";
    public const string PdfsFolder = "Approved PDFs";
    /// <summary>At most this many PDFs per copy, so a long history goes up a little at a time.</summary>
    public const int PdfsPerRun = 100;

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>What was copied (kept in the settings store, so a restart does not copy everything again).</summary>
    public sealed class DriveState
    {
        public string? LastBackupFile { get; set; }
        public Dictionary<string, string> Folders { get; set; } = [];
        /// <summary>PDF (its path in the pdf folder) → "file id|size|time written", so a remade PDF replaces its copy.</summary>
        public Dictionary<string, string> Pdfs { get; set; } = [];
        public DateTime? LastCopyLocal { get; set; }
        public string LastCopyText { get; set; } = "";
        public string? Error { get; set; }
    }

    public DriveState State => settings.Get<DriveState>(StateKey);

    public async Task<DriveBackupStatusDto> StatusAsync(CancellationToken ct)
    {
        var s = State;
        var account = settings.Gmail.ConnectedAccount;
        var waiting = settings.Backup.DrivePdfs ? (await ApprovedPdfsAsync(ct)).Count(p => !IsCopied(s, p)) : 0;
        return new DriveBackupStatusDto(drives.Connected, account, s.LastCopyLocal, s.LastCopyText, s.Error, waiting);
    }

    /// <summary>Copies what is new; returns the status. <paramref name="makeBackup"/>: a new database backup first (Back up now).</summary>
    public async Task<DriveBackupStatusDto> CopyNowAsync(string user, bool makeBackup, CancellationToken ct)
    {
        var options = settings.Backup;
        if (drives.Create() is not { } drive)
        {
            await SaveAsync(s => s.Error = "Connect the Google account first (Gmail connection).", user, ct);
            return await StatusAsync(ct);
        }
        if (!options.DriveBackups && !options.DrivePdfs)
        {
            (drive as IDisposable)?.Dispose();
            return await StatusAsync(ct);
        }
        await _gate.WaitAsync(ct);
        try
        {
            var s = State;
            var before = (s.Error, s.Folders.Count, s.Pdfs.Count, s.LastBackupFile);
            var sent = new List<string>();
            long bytes = 0;
            try
            {
                var root = await FolderAsync(drive, s, RootFolder, null, ct);
                if (options.DriveBackups)
                {
                    var newest = makeBackup ? await backups.BackupNowAsync(ct) : NewestBackup();
                    if (newest is not null && Path.GetFileName(newest) != s.LastBackupFile)
                    {
                        var folder = await FolderAsync(drive, s, BackupsFolder, root, ct);
                        await drive.UploadAsync(folder, Path.GetFileName(newest), newest, "application/x-sqlite3", null, ct);
                        s.LastBackupFile = Path.GetFileName(newest);
                        bytes = new FileInfo(newest).Length;
                        sent.Add("database " + Size(bytes));
                    }
                }
                if (options.DrivePdfs)
                {
                    var count = 0;
                    string? pdfRoot = null;
                    foreach (var p in (await ApprovedPdfsAsync(ct)).Where(p => !IsCopied(s, p)).Take(PdfsPerRun))
                    {
                        pdfRoot ??= await FolderAsync(drive, s, PdfsFolder, root, ct);
                        var month = await FolderAsync(drive, s, p.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture), pdfRoot, ct);
                        var existing = s.Pdfs.TryGetValue(p.Relative, out var was) ? was.Split('|')[0] : null;
                        var id = await drive.UploadAsync(month, p.DriveName, p.FullPath, "application/pdf", existing, ct);
                        s.Pdfs[p.Relative] = $"{id}|{p.Mark}";
                        count++;
                    }
                    if (count > 0) sent.Add(count == 1 ? "1 PDF" : $"{count} PDFs");
                }
                s.Error = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                s.Error = ex is Google.GoogleApiException { HttpStatusCode: System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized }
                    || ex.Message.Contains("insufficient", StringComparison.OrdinalIgnoreCase)
                    ? "Google Drive did not allow it: connect the Google account again (Gmail connection) and tick Google Drive."
                    : "The copy to Google Drive failed: " + ex.Message;
                log.LogWarning("Google Drive backup failed: {Message}", ex.Message);
            }
            if (sent.Count > 0)
            {
                s.LastCopyLocal = clock.GetLocalNow().DateTime;
                s.LastCopyText = string.Join(" + ", sent);
                log.LogInformation("Copied to Google Drive: {What}", s.LastCopyText);
            }
            // Saved only when something changed (the copy every 15 minutes usually finds nothing new)
            if (sent.Count > 0 || before != (s.Error, s.Folders.Count, s.Pdfs.Count, s.LastBackupFile))
                await settings.SaveAsync(StateKey, s, user, ct);
        }
        finally
        {
            _gate.Release();
            (drive as IDisposable)?.Dispose();
        }
        return await StatusAsync(ct);
    }

    private async Task SaveAsync(Action<DriveState> change, string user, CancellationToken ct)
    {
        var s = State;
        change(s);
        await settings.SaveAsync(StateKey, s, user, ct);
    }

    private static async Task<string> FolderAsync(IDriveClient drive, DriveState s, string name, string? parent, CancellationToken ct)
    {
        var key = (parent ?? "") + "/" + name;
        if (s.Folders.TryGetValue(key, out var id)) return id;
        id = await drive.FolderAsync(name, parent, ct);
        s.Folders[key] = id;
        return id;
    }

    private string? NewestBackup() =>
        Directory.Exists(backups.Folder)
            ? Directory.EnumerateFiles(backups.Folder, "quotation-*.db").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;

    private sealed record ApprovedPdf(string Relative, string FullPath, DateOnly Date, string DriveName, string Mark);

    private static bool IsCopied(DriveState s, ApprovedPdf p) => s.Pdfs.TryGetValue(p.Relative, out var was) && was.EndsWith("|" + p.Mark, StringComparison.Ordinal);

    private async Task<List<ApprovedPdf>> ApprovedPdfsAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
        var rows = await db.Quotations.AsNoTracking()
            .Where(q => q.PdfPath != null && (q.Status == QuotationStatus.Approved || q.Status == QuotationStatus.Generated))
            .OrderBy(q => q.Date).Select(q => new { q.PdfPath, q.Date, q.Number, Buyer = q.Buyer.Name }).ToListAsync(ct);
        var list = new List<ApprovedPdf>();
        foreach (var r in rows)
        {
            if (pdfs.FullPath(r.PdfPath) is not { } full) continue;
            var info = new FileInfo(full);
            var name = Path.GetFileNameWithoutExtension(full) + (r.Buyer.Trim().Length > 0 ? " - " + Clean(r.Buyer.Trim()) : "") + ".pdf";
            list.Add(new ApprovedPdf(r.PdfPath!, full, r.Date, name, $"{info.Length}:{info.LastWriteTimeUtc.Ticks}"));
        }
        return list;
    }

    private static string Clean(string name) =>
        new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' or ':' ? ' ' : c).ToArray()).Trim();

    public static string Size(long bytes) => bytes >= 1024 * 1024
        ? (bytes / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
        : Math.Max(1, bytes / 1024).ToString(CultureInfo.InvariantCulture) + " KB";
}

/// <summary>Every 15 minutes: what is new goes to Google Drive (when the Google account is connected and a tick is on).</summary>
public sealed class DriveBackupWorker(DriveBackupService drive, SettingsService settings, IDriveClientFactory drives, IOptions<ServerOptions> options,
    ILogger<DriveBackupWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.EnableBackgroundWorkers) return;
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var b = settings.Backup;
                if ((b.DriveBackups || b.DrivePdfs) && drives.Connected) await drive.CopyNowAsync("system", makeBackup: false, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Google Drive backup failed");
            }
            try { await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
