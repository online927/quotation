using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quotation.Data;

namespace Quotation.Server.Services;

/// <summary>
/// Consistent online backups of the SQLite database (SQLite backup API — safe while the server is
/// running). A daily backup is kept for 30 days in DataDirectory\backups.
/// </summary>
public sealed class BackupService(IServiceScopeFactory scopes, IOptions<ServerOptions> options, TimeProvider clock, ILogger<BackupService> log)
{
    public const int KeepDays = 30;
    public string Folder => Path.Combine(options.Value.ResolveDataDirectory(), "backups");

    public async Task<string> BackupNowAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Folder);
        var target = Path.Combine(Folder, $"quotation-{clock.GetLocalNow():yyyyMMdd-HHmmss}.db");
        using var scope = scopes.CreateScope();
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
        SqliteConnection.ClearAllPools();
        Prune();
        log.LogInformation("Database backup written to {Path}", target);
        return target;
    }

    public DateTime? LastBackupLocal() =>
        Directory.Exists(Folder) ? Directory.EnumerateFiles(Folder, "quotation-*.db").Select(File.GetLastWriteTime).DefaultIfEmpty().Max() is var d && d != default ? d : null : null;

    private void Prune()
    {
        var cutoff = clock.GetLocalNow().DateTime.AddDays(-KeepDays);
        foreach (var f in Directory.EnumerateFiles(Folder, "quotation-*.db").Where(f => File.GetLastWriteTime(f) < cutoff))
        {
            try { File.Delete(f); } catch (IOException) { }
        }
    }
}

public sealed class BackupWorker(BackupService backups, IOptions<ServerOptions> options, ILogger<BackupWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.EnableBackgroundWorkers) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var last = backups.LastBackupLocal();
                if (last is null || DateTime.Now - last > TimeSpan.FromHours(24)) await backups.BackupNowAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Database backup failed");
            }
            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
