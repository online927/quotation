using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Data;
using Quotation.Data.Entities;

namespace Quotation.Server.Auth;

public sealed class AuthService(QuotationDbContext db, IOptions<ServerOptions> options, TimeProvider clock)
{
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, string machine, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username.Trim(), ct);
        if (user is null || !user.IsActive || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            return null;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var token = PasswordHasher.NewToken();
        var session = new ApiSession
        {
            UserId = user.Id,
            TokenHash = PasswordHasher.HashToken(token),
            ClientMachine = request.ClientMachine ?? machine,
            CreatedUtc = now,
            LastSeenUtc = now,
            ExpiresUtc = now.AddHours(options.Value.SessionHours),
        };
        user.LastLoginUtc = now;
        db.ApiSessions.Add(session);

        // Housekeeping: drop expired sessions.
        await db.ApiSessions.Where(s => s.ExpiresUtc < now).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        return new LoginResponse(token, session.ExpiresUtc, ToDto(user));
    }

    public async Task LogoutAsync(string token, CancellationToken ct)
    {
        var hash = PasswordHasher.HashToken(token);
        await db.ApiSessions.Where(s => s.TokenHash == hash).ExecuteDeleteAsync(ct);
    }

    public async Task EnsureBootstrapAdminAsync(CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct)) return;
        db.Users.Add(new AppUser
        {
            Username = "admin",
            DisplayName = "Administrator",
            PasswordHash = PasswordHasher.Hash(options.Value.BootstrapAdminPassword),
            Role = UserRole.Admin,
            MustChangePassword = true,
            CreatedUtc = clock.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(ct);
    }

    public static UserDto ToDto(AppUser u) => new(u.Id, u.Username, u.DisplayName, u.Role, u.IsActive, u.MustChangePassword);

    public static IReadOnlyList<string> ValidatePassword(string password)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(password) || password.Length < 6) errors.Add("Password must be at least 6 characters.");
        return errors;
    }
}
