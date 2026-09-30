using Quotation.Data;
using Quotation.Data.Entities;

namespace Quotation.Server.Services;

public sealed class AuditService(QuotationDbContext db, TimeProvider clock, ILogger<AuditService> log)
{
    /// <summary>Adds an audit entry to the current unit of work (saved with the caller's SaveChanges).</summary>
    public void Add(string user, string machine, string action, string entityType, string entityId, string details = "")
    {
        db.AuditLog.Add(new AuditEntry
        {
            AtUtc = clock.GetUtcNow().UtcDateTime,
            User = user,
            Machine = machine,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details.Length > 4000 ? details[..4000] : details,
        });
        log.LogInformation("AUDIT {Action} {EntityType} {EntityId} by {User}@{Machine}", action, entityType, entityId, user, machine);
    }

    public async Task WriteAsync(string user, string machine, string action, string entityType, string entityId,
        string details = "", CancellationToken ct = default)
    {
        Add(user, machine, action, entityType, entityId, details);
        await db.SaveChangesAsync(ct);
    }
}
