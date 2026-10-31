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
            AuthService svc, AuditService audit, HttpContext ctx, CancellationToken ct) =>
        {
            var user = await db.Users.FindAsync([id], ct);
            if (user is null) return Results.NotFound();
            var errors = AuthService.ValidatePassword(req.Password);
            if (errors.Count > 0) return Results.BadRequest(new ApiError("Invalid password.", errors));
            audit.Add(ctx.User.UserName(), ctx.Machine(), "PasswordReset", "User", user.Username);
            await svc.ResetPasswordAsync(user, req.Password, ct);
            return Results.NoContent();
        });

        users.MapPut("/{id:int}", async (int id, UpdateUserRequest req, QuotationDbContext db, AuthService svc, AuditService audit,
            HttpContext ctx, CancellationToken ct) =>
        {
            var before = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
            if (before is null) return Results.NotFound();
            var (user, error) = await svc.UpdateUserAsync(id, req, ctx.User.UserName(), ct);
            if (error is not null) return Results.BadRequest(new ApiError(error));
            var changes = new List<string>();
            if (before.Username != user!.Username) changes.Add($"login {before.Username} → {user.Username}");
            if (before.DisplayName != user.DisplayName) changes.Add($"name {before.DisplayName} → {user.DisplayName}");
            if (before.Role != user.Role) changes.Add($"role {before.Role} → {user.Role}");
            if (before.IsActive != user.IsActive) changes.Add(user.IsActive ? "activated" : "deactivated");
            await audit.WriteAsync(ctx.User.UserName(), ctx.Machine(), "UserChanged", "User", user.Username, string.Join("; ", changes), ct);
            return Results.Ok(AuthService.ToDto(user));
        });

        // The current user's own switches.
        app.MapPut("/api/me/preferences", async (MyPreferencesDto req, HttpContext ctx, QuotationDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.FirstAsync(u => u.Username == ctx.User.UserName(), ct);
            user.WithoutGstNext = req.WithoutGstNext;
            await db.SaveChangesAsync(ct);
            return Results.Ok(AuthService.ToDto(user));
        }).RequireAuthorization();

        static AllSettingsDto ForClient(SettingsService s, SealService seal)
        {
            var dto = s.GetAllForClient();
            dto.SealPinConfigured = seal.HasPin;
            dto.SealImageConfigured = seal.HasImage;
            return dto;
        }

        app.MapGet("/api/settings", (SettingsService s, SealService seal) => ForClient(s, seal)).RequireAuthorization();
        app.MapPut("/api/settings", async (AllSettingsDto dto, SettingsService s, SealService seal, AuditService audit, HttpContext ctx,
            CancellationToken ct) =>
        {
            var errors = ValidateSettings(dto);
            if (!string.IsNullOrEmpty(dto.SealPin) && SealService.ValidatePin(dto.SealPin) is { } pinError) errors.Add(pinError);
            if (errors.Count > 0) return Results.BadRequest(new ApiError("Invalid settings.", errors));
            await s.SaveAllFromClientAsync(dto, ctx.User.UserName(), ct);
            if (!string.IsNullOrEmpty(dto.SealPin))
            {
                await seal.SetPinAsync(dto.SealPin, ctx.User.UserName(), ct);
                audit.Add(ctx.User.UserName(), ctx.Machine(), "SealPinChanged", "Settings", "seal");
            }
            await audit.WriteAsync(ctx.User.UserName(), ctx.Machine(), "SettingsChanged", "Settings", "all", ct: ct);
            return Results.Ok(ForClient(s, seal));
        }).RequireAuthorization(AdminPolicy);

        // Company seal image (administrator): upload (PNG/JPG body), view, remove.
        app.MapPut("/api/settings/seal", async (HttpRequest request, SealService seal, AuditService audit, HttpContext ctx, CancellationToken ct) =>
        {
            using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms, ct);
            if (await seal.SaveImageAsync(ms.ToArray(), ct) is { } error) return Results.BadRequest(new ApiError(error));
            await audit.WriteAsync(ctx.User.UserName(), ctx.Machine(), "SealImageChanged", "Settings", "seal", ct: ct);
            return Results.NoContent();
        }).RequireAuthorization(AdminPolicy);
        // A sample page with the seal. Size and position can be given (the values on the Settings screen, not saved yet),
        // otherwise the saved ones are used.
        app.MapGet("/api/settings/seal/preview", (double? width, double? offsetX, double? offsetY, SealService seal, SettingsService s,
            PdfQuotationRenderer pdf, HttpContext ctx, TimeProvider clock) =>
        {
            var company = s.Company;
            if (width is { } w) company.SealWidth = w;
            if (offsetX is { } x) company.SealOffsetX = x;
            if (offsetY is { } y) company.SealOffsetY = y;
            if (seal.ReadImage() is not { } image) return Results.NotFound(new ApiError("No seal image yet."));
            var sample = new QuotationHeader
            {
                Number = "SAMPLE",
                Date = DateOnly.FromDateTime(clock.GetLocalNow().DateTime),
                Buyer = new PartySnapshot { Name = "Sample Customer", Address = "Seal position preview" },
                Consignee = new PartySnapshot { Name = "Sample Customer", Address = "Seal position preview" },
                TaxPresentation = s.Quotation.TaxPresentation,
                RoundOffTotal = s.Quotation.RoundOffTotal,
                Status = QuotationStatus.Draft,
                Lines = [new QuotationLine { LineNo = 1, ItemName = "Sample Item", Hsn = "82051000", GstRate = 18, Quantity = 1, Unit = "NOS", Rate = 1000, Amount = 1000 }],
            };
            var bytes = pdf.Render(sample, company, s.Quotation,
                new Quotation.Pdf.PrintStamp(clock.GetLocalNow().DateTime, ctx.User.UserName()), image);
            return Results.File(bytes, "application/pdf");
        }).RequireAuthorization(AdminPolicy);
        // The latest quotation (not proforma) with a Tally customer (or a sample) printed with the quotation settings on the Settings screen
        // (heading, Party Details; not saved yet). Nothing is saved.
        app.MapPost("/api/settings/print/preview", async (QuotationSettings options, SettingsService s, QuotationDbContext db,
            QuotationService quotations, PdfQuotationRenderer pdf, HttpContext ctx, TimeProvider clock, CancellationToken ct) =>
        {
            var q = await db.Quotations.AsNoTracking().Include(x => x.Lines)
                .Where(x => x.CustomerId != null && x.DocType == DocumentType.Quotation)
                .OrderByDescending(x => x.CreatedUtc)
                .FirstOrDefaultAsync(ct) ?? new QuotationHeader
                {
                    Number = "SAMPLE",
                    Date = DateOnly.FromDateTime(clock.GetLocalNow().DateTime),
                    Buyer = new PartySnapshot
                    {
                        Name = "Sample Customer Private Limited", Address = "Plot No. 12, MIDC Industrial Area\nChakan, Pune\nPIN: 410501",
                        Gstin = "27AAACS1234F1Z3", StateName = "Maharashtra", StateCode = "27", ContactPerson = "Mr. Sample",
                        Phone = "9800000000", Email = "purchase@sample.example",
                    },
                    TaxPresentation = s.Quotation.TaxPresentation,
                    RoundOffTotal = s.Quotation.RoundOffTotal,
                    Lines = [new QuotationLine { LineNo = 1, ItemName = "Sample Item", Hsn = "82051000", GstRate = 18, Quantity = 1, Unit = "NOS", Rate = 1000, Amount = 1000 }],
                };
            var bytes = pdf.RenderPreview(q, s.Company, options, q.Number ?? "PREVIEW",
                new Quotation.Pdf.PrintStamp(clock.GetLocalNow().DateTime, ctx.User.UserName()), quotations.MasterOf(q));
            return Results.File(bytes, "application/pdf");
        }).RequireAuthorization(AdminPolicy);
        app.MapGet("/api/settings/seal", (SealService seal) =>
            seal.ReadImage() is { } bytes ? Results.File(bytes, "application/octet-stream") : Results.NotFound()).RequireAuthorization(AdminPolicy);
        app.MapDelete("/api/settings/seal", async (SealService seal, AuditService audit, HttpContext ctx, CancellationToken ct) =>
        {
            seal.DeleteImage();
            await audit.WriteAsync(ctx.User.UserName(), ctx.Machine(), "SealImageRemoved", "Settings", "seal", ct: ct);
            return Results.NoContent();
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
