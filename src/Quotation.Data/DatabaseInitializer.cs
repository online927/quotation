using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Quotation.Data;

public static class DatabaseInitializer
{
    public static string BuildConnectionString(string databasePath) => new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        // Microsoft.Data.Sqlite retries SQLITE_BUSY until the command timeout elapses.
        DefaultTimeout = 30,
        Pooling = true,
    }.ToString();

    /// <summary>Applies pending migrations and enables WAL so readers never block the writer.</summary>
    public static async Task InitializeAsync(QuotationDbContext db, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", ct);
    }
}

/// <summary>Used only by the EF Core tools (dotnet ef migrations add ...).</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<QuotationDbContext>
{
    public QuotationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<QuotationDbContext>()
            .UseSqlite(DatabaseInitializer.BuildConnectionString("design-time.db"))
            .Options;
        return new QuotationDbContext(options);
    }
}
