using Microsoft.EntityFrameworkCore;
using Quotation.Data.Entities;

namespace Quotation.Data;

public class QuotationDbContext(DbContextOptions<QuotationDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockGroup> StockGroups => Set<StockGroup>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<QuotationHeader> Quotations => Set<QuotationHeader>();
    public DbSet<QuotationLine> QuotationLines => Set<QuotationLine>();
    public DbSet<NumberSeries> NumberSeries => Set<NumberSeries>();
    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<ApiSession> ApiSessions => Set<ApiSession>();
    public DbSet<SettingEntry> Settings => Set<SettingEntry>();
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Product>(e =>
        {
            e.HasIndex(x => x.TallyGuid).IsUnique();
            e.HasIndex(x => x.Name);
            e.Property(x => x.Name).UseCollation("NOCASE");
        });

        b.Entity<StockGroup>(e =>
        {
            e.HasIndex(x => x.TallyGuid).IsUnique();
            e.HasIndex(x => x.Name);
            e.Property(x => x.Name).UseCollation("NOCASE");
        });

        b.Entity<Unit>(e =>
        {
            e.HasIndex(x => x.TallyGuid).IsUnique();
            e.Property(x => x.Name).UseCollation("NOCASE");
        });

        b.Entity<Customer>(e =>
        {
            e.HasIndex(x => x.TallyGuid).IsUnique();
            e.HasIndex(x => x.Name);
            e.HasIndex(x => x.Gstin);
            e.Property(x => x.Name).UseCollation("NOCASE");
        });

        b.Entity<QuotationHeader>(e =>
        {
            e.ToTable("Quotations");
            e.HasKey(x => x.Id);
            // Final safety net against duplicate numbers (NULLs are allowed for un-numbered AI drafts).
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.FinancialYearStart, x.Sequence }).IsUnique();
            e.HasIndex(x => x.Date);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CustomerId);
            e.OwnsOne(x => x.Buyer, o => o.WithOwner());
            e.OwnsOne(x => x.Consignee, o => o.WithOwner());
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.QuotationId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<QuotationLine>(e =>
        {
            e.HasIndex(x => new { x.QuotationId, x.LineNo });
            e.HasIndex(x => x.ProductId);
        });

        b.Entity<NumberSeries>(e =>
        {
            e.HasIndex(x => new { x.SeriesKey, x.FinancialYearStart }).IsUnique();
        });

        b.Entity<EmailMessage>(e =>
        {
            // Guarantees an e-mail is never ingested twice, even across restarts.
            e.HasIndex(x => x.GmailMessageId).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ThreadId);
        });

        b.Entity<AppUser>(e =>
        {
            e.ToTable("Users");
            e.Property(x => x.Username).UseCollation("NOCASE");
            e.HasIndex(x => x.Username).IsUnique();
        });

        b.Entity<ApiSession>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<SettingEntry>(e =>
        {
            e.HasKey(x => x.Key);
        });

        b.Entity<AuditEntry>(e =>
        {
            e.ToTable("AuditLog");
            e.HasIndex(x => x.AtUtc);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
        });

        b.Entity<SyncRun>(e => e.HasIndex(x => x.StartedUtc));
    }
}
