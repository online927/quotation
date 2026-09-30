using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Data;
using Quotation.Data.Entities;
using Quotation.Server.Auth;
using Quotation.Server.Infrastructure;
using Quotation.Server.Services;

namespace Quotation.Server.Endpoints;

public static class SystemEndpoints
{
    public const string AdminPolicy = "Admin";

    public static void MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0";
        app.MapGet("/api/health", (TimeProvider clock) =>
            new HealthDto("ok", version, clock.GetUtcNow().UtcDateTime)).AllowAnonymous();

        var auth = app.MapGroup("/api/auth");
        auth.MapPost("/login", async (LoginRequest req, AuthService svc, AuditService audit, HttpContext ctx, CancellationToken ct) =>
        {
            var result = await svc.LoginAsync(req, ctx.Machine(), ct);
            if (result is null)
            {
                await audit.WriteAsync(req.Username, ctx.Machine(), "LoginFailed", "User", req.Username, ct: ct);
                return Results.Json(new ApiError("Invalid username or password."), statusCode: 401);
            }
            await audit.WriteAsync(result.User.Username, ctx.Machine(), "Login", "User", result.User.Id.ToString(), ct: ct);
            return Results.Ok(result);
        }).AllowAnonymous();

        auth.MapPost("/logout", async (HttpContext ctx, AuthService svc, CancellationToken ct) =>
        {
            var header = ctx.Request.Headers.Authorization.ToString();
            if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) await svc.LogoutAsync(header[7..].Trim(), ct);
            return Results.NoContent();
        }).RequireAuthorization();

        auth.MapGet("/me", async (HttpContext ctx, QuotationDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.FirstAsync(u => u.Username == ctx.User.UserName(), ct);
            return AuthService.ToDto(user);
        }).RequireAuthorization();

        auth.MapPost("/change-password", async (ChangePasswordRequest req, HttpContext ctx, QuotationDbContext db,
            AuditService audit, CancellationToken ct) =>
        {
            var user = await db.Users.FirstAsync(u => u.Username == ctx.User.UserName(), ct);
            if (!PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash))
                return Results.BadRequest(new ApiError("Current password is incorrect."));
            var errors = AuthService.ValidatePassword(req.NewPassword);
            if (errors.Count > 0) return Results.BadRequest(new ApiError("Invalid password.", errors));
            user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
            user.MustChangePassword = false;
            audit.Add(user.Username, ctx.Machine(), "PasswordChanged", "User", user.Id.ToString());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();

        var users = app.MapGroup("/api/users").RequireAuthorization(AdminPolicy);
        users.MapGet("/", async (QuotationDbContext db, CancellationToken ct) =>
            (await db.Users.OrderBy(u => u.Username).ToListAsync(ct)).Select(AuthService.ToDto));

        users.MapPost("/", async (CreateUserRequest req, QuotationDbContext db, AuditService audit, HttpContext ctx,
            TimeProvider clock, CancellationToken ct) =>
        {
            var username = req.Username.Trim();
            if (username.Length == 0) return Results.BadRequest(new ApiError("Username is required."));
            var errors = AuthService.ValidatePassword(req.Password);
            if (errors.Count > 0) return Results.BadRequest(new ApiError("Invalid password.", errors));
            if (await db.Users.AnyAsync(u => u.Username == username, ct))
                return Results.Conflict(new ApiError($"User '{username}' already exists."));
            var user = new AppUser
            {
                Username = username,
                DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? username : req.DisplayName.Trim(),
                PasswordHash = PasswordHasher.Hash(req.Password),
                Role = req.Role,
                MustChangePassword = true,
                CreatedUtc = clock.GetUtcNow().UtcDateTime,
            };
            db.Users.Add(user);
            audit.Add(ctx.User.UserName(), ctx.Machine(), "UserCreated", "User", username, $"Role={req.Role}");
            await db.SaveChangesAsync(ct);
            return Results.Ok(AuthService.ToDto(user));
        });

        users.MapPost("/{id:int}/active/{active:bool}", async (int id, bool active, QuotationDbContext db,
            AuditService audit, HttpContext ctx, CancellationToken ct) =>
        {
            var user = await db.Users.FindAsync([id], ct);
            if (user is null) return Results.NotFound();
            if (!active && user.Username == ctx.User.UserName())
                return Results.BadRequest(new ApiError("You cannot deactivate your own account."));
            user.IsActive = active;
            audit.Add(ctx.User.UserName(), ctx.Machine(), active ? "UserActivated" : "UserDeactivated", "User", user.Username);
            await db.SaveChangesAsync(ct);
            return Results.Ok(AuthService.ToDto(user));
        });

        users.MapPost("/{id:int}/reset-password", async (int id, [FromBody] CreateUserRequest req, QuotationDbContext db,
            AuditService audit, HttpContext ctx, CancellationToken ct) =>
        {
            var user = await db.Users.FindAsync([id], ct);
            if (user is null) return Results.NotFound();
            var errors = AuthService.ValidatePassword(req.Password);
            if (errors.Count > 0) return Results.BadRequest(new ApiError("Invalid password.", errors));
            user.PasswordHash = PasswordHasher.Hash(req.Password);
            user.MustChangePassword = true;
            audit.Add(ctx.User.UserName(), ctx.Machine(), "PasswordReset", "User", user.Username);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        app.MapGet("/api/settings", (SettingsService s) => s.GetAllForClient()).RequireAuthorization();
        app.MapPut("/api/settings", async (AllSettingsDto dto, SettingsService s, AuditService audit, HttpContext ctx,
            CancellationToken ct) =>
        {
            var errors = ValidateSettings(dto);
            if (errors.Count > 0) return Results.BadRequest(new ApiError("Invalid settings.", errors));
            await s.SaveAllFromClientAsync(dto, ctx.User.UserName(), ct);
            await audit.WriteAsync(ctx.User.UserName(), ctx.Machine(), "SettingsChanged", "Settings", "all", ct: ct);
            return Results.Ok(s.GetAllForClient());
        }).RequireAuthorization(AdminPolicy);

        app.MapGet("/api/status", (StatusService s, CancellationToken ct) => s.GetAsync(ct)).RequireAuthorization();
        app.MapGet("/api/dashboard", (StatusService s, CancellationToken ct) => s.GetDashboardAsync(ct)).RequireAuthorization();

        app.MapGet("/api/audit", async (QuotationDbContext db, string? entityType, string? entityId, int? take,
            CancellationToken ct) =>
        {
            var q = db.AuditLog.AsQueryable();
            if (!string.IsNullOrEmpty(entityType)) q = q.Where(a => a.EntityType == entityType);
            if (!string.IsNullOrEmpty(entityId)) q = q.Where(a => a.EntityId == entityId);
            return await q.OrderByDescending(a => a.Id).Take(Math.Clamp(take ?? 200, 1, 2000))
                .Select(a => new AuditEntryDto(a.Id, a.AtUtc, a.User, a.Machine, a.Action, a.EntityType, a.EntityId, a.Details))
                .ToListAsync(ct);
        }).RequireAuthorization(AdminPolicy);
    }

    internal static List<string> ValidateSettings(AllSettingsDto dto)
    {
        var errors = new List<string>();
        var pattern = dto.Quotation.NumberPattern ?? "";
        if (!pattern.Contains("{SEQ", StringComparison.Ordinal)) errors.Add("Number pattern must contain {SEQ}.");
        if (dto.Quotation.FinancialYearStartMonth is < 1 or > 12) errors.Add("Financial year start month must be 1-12.");
        if (dto.Quotation.DefaultStartSequence < 1) errors.Add("Start sequence must be at least 1.");
        if (!Uri.TryCreate(dto.Tally.Url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            errors.Add("Tally URL must be like http://192.168.1.10:9000");
        if (dto.Quotation.DefaultPackingForwardingAmount < 0 || dto.Quotation.DefaultPackingForwardingPercent < 0)
            errors.Add("Packing & forwarding cannot be negative.");
        if (dto.Company.QuotationValidityDays < 0) errors.Add("Validity days cannot be negative.");
        if (!string.IsNullOrWhiteSpace(dto.Gmail.ClientSecretJson) && Quotation.Gmail.GmailOAuth.ValidateClientSecret(dto.Gmail.ClientSecretJson) is { } gmailError)
            errors.Add(gmailError);
        if (string.IsNullOrWhiteSpace(dto.Gmail.Query)) errors.Add("Gmail search query cannot be empty.");
        return errors;
    }
}
