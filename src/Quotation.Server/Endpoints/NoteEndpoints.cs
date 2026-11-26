using Microsoft.EntityFrameworkCore;
using Quotation.Contracts;
using Quotation.Data;
using Quotation.Data.Entities;
using Quotation.Server.Infrastructure;
using Quotation.Server.Services;

namespace Quotation.Server.Endpoints;

/// <summary>
/// Round 39 (Z8): internal notes on a quotation. Everyone who can open the quotation reads and adds; a note is changed or
/// deleted by its writer, and deleted by an administrator. Adding a note never changes the quotation itself.
/// </summary>
public static class NoteEndpoints
{
    public const int MaxLength = 2000;

    public static void MapNoteEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/quotations/{id:guid}/notes", async (Guid id, QuotationDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            var q = await db.Quotations.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Id, x.DocType, x.DuplicatedFromId }).FirstOrDefaultAsync(ct);
            if (q is null) return Results.NotFound(new ApiError("Quotation not found."));
            // A proforma invoice made from a quotation shows that quotation's notes too (a Duplicate does not).
            var ids = new List<Guid> { id };
            if (q.DocType == Core.Domain.DocumentType.ProformaInvoice && q.DuplicatedFromId is { } from) ids.Add(from);
            var notes = await db.QuotationNotes.AsNoTracking().Where(n => ids.Contains(n.QuotationId)).ToListAsync(ct);
            var user = QuotationEndpoints.User(ctx);
            return Results.Ok(notes.OrderBy(n => n.CreatedUtc).ThenBy(n => n.Id).Select(n => ToDto(n, user)).ToList());
        }).RequireAuthorization();

        app.MapPost("/api/quotations/{id:guid}/notes", async (Guid id, NoteTextRequest r, QuotationDbContext db, SettingsService settings,
            AuditService audit, TimeProvider clock, HttpContext ctx, CancellationToken ct) =>
        {
            if (!settings.Quotation.InternalNotes) return Results.Json(new ApiError("Notes are switched off in Settings."), statusCode: 409);
            var text = (r.Text ?? "").Trim();
            if (text.Length == 0) return Results.BadRequest(new ApiError("Write a note first."));
            if (text.Length > MaxLength) return Results.BadRequest(new ApiError($"A note can be up to {MaxLength} letters."));
            if (!await db.Quotations.AnyAsync(x => x.Id == id, ct)) return Results.NotFound(new ApiError("Quotation not found."));
            var user = QuotationEndpoints.User(ctx);
            var note = new QuotationNote
            {
                QuotationId = id, UserName = user.UserName, DisplayName = user.DisplayName, Text = text,
                CreatedUtc = clock.GetUtcNow().UtcDateTime,
            };
            db.QuotationNotes.Add(note);
            audit.Add(user.UserName, user.Machine, "NoteAdded", "Quotation", id.ToString(), Short(text));
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(note, user));
        }).RequireAuthorization();

        app.MapPut("/api/notes/{noteId:int}", async (int noteId, NoteTextRequest r, QuotationDbContext db, SettingsService settings,
            AuditService audit, TimeProvider clock, HttpContext ctx, CancellationToken ct) =>
        {
            if (!settings.Quotation.InternalNotes) return Results.Json(new ApiError("Notes are switched off in Settings."), statusCode: 409);
            var note = await db.QuotationNotes.FirstOrDefaultAsync(n => n.Id == noteId, ct);
            if (note is null) return Results.NotFound(new ApiError("Note not found."));
            var user = QuotationEndpoints.User(ctx);
            if (note.UserName != user.UserName) return Results.Json(new ApiError("Only the writer can change a note."), statusCode: 403);
            var text = (r.Text ?? "").Trim();
            if (text.Length == 0) return Results.BadRequest(new ApiError("Write a note first."));
            if (text.Length > MaxLength) return Results.BadRequest(new ApiError($"A note can be up to {MaxLength} letters."));
            if (text == note.Text) return Results.Ok(ToDto(note, user));
            note.Text = text;
            note.EditedUtc = clock.GetUtcNow().UtcDateTime;
            audit.Add(user.UserName, user.Machine, "NoteEdited", "Quotation", note.QuotationId.ToString(), Short(text));
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(note, user));
        }).RequireAuthorization();

        app.MapDelete("/api/notes/{noteId:int}", async (int noteId, QuotationDbContext db, AuditService audit, HttpContext ctx, CancellationToken ct) =>
        {
            var note = await db.QuotationNotes.FirstOrDefaultAsync(n => n.Id == noteId, ct);
            if (note is null) return Results.NotFound(new ApiError("Note not found."));
            var user = QuotationEndpoints.User(ctx);
            if (note.UserName != user.UserName && !user.IsAdmin)
                return Results.Json(new ApiError("Only the writer or an administrator can delete a note."), statusCode: 403);
            db.QuotationNotes.Remove(note);
            audit.Add(user.UserName, user.Machine, "NoteDeleted", "Quotation", note.QuotationId.ToString(), Short(note.Text));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static string Short(string text) => text.Length <= 80 ? text : text[..80] + "…";

    public static QuotationNoteDto ToDto(QuotationNote n, UserContext user) => new(n.Id, n.QuotationId, n.UserName,
        n.DisplayName.Length > 0 ? n.DisplayName : n.UserName, n.Text, DateTime.SpecifyKind(n.CreatedUtc, DateTimeKind.Utc),
        n.EditedUtc is { } e ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : null, n.UserName == user.UserName,
        n.UserName == user.UserName || user.IsAdmin);
}
