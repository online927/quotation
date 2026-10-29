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

    public static UserDto ToDto(AppUser u) => new(u.Id, u.Username, u.DisplayName, u.Role, u.IsActive, u.MustChangePassword, u.WithoutGstNext);

    /// <summary>
    /// Administrator edits a user. The login name must stay unique; an administrator cannot deactivate themselves or
    /// remove their own admin role, and the last active administrator cannot be removed. Existing quotations and
    /// the history keep the name that was used at the time.
    /// </summary>
    public async Task<(AppUser? User, string? Error)> UpdateUserAsync(int id, UpdateUserRequest request, string actingUser, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return (null, "User not found.");
        var username = request.Username.Trim();
        if (username.Length == 0) return (null, "The login name is required.");
        if (username.Any(char.IsWhiteSpace)) return (null, "The login name cannot contain spaces.");
        if (await db.Users.AnyAsync(u => u.Id != id && u.Username == username, ct)) return (null, $"Login name '{username}' is already used.");
        var self = string.Equals(user.Username, actingUser, StringComparison.OrdinalIgnoreCase);
        if (self && !request.IsActive) return (null, "You cannot deactivate your own account.");
        if (self && request.Role != UserRole.Admin) return (null, "You cannot remove your own administrator role.");
        var losesAdmin = user.Role == UserRole.Admin && user.IsActive && (request.Role != UserRole.Admin || !request.IsActive);
        if (losesAdmin && !await db.Users.AnyAsync(u => u.Id != id && u.Role == UserRole.Admin && u.IsActive, ct))
            return (null, "At least one active administrator is needed.");

        user.Username = username;
        user.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? username : request.DisplayName.Trim();
        user.Role = request.Role;
        user.IsActive = request.IsActive;
        if (!user.IsActive) await db.ApiSessions.Where(s => s.UserId == id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        return (user, null);
    }

    /// <summary>Sets a temporary password (changed at the next login) and logs the user out on every PC.</summary>
    public async Task ResetPasswordAsync(AppUser user, string password, CancellationToken ct)
    {
        user.PasswordHash = PasswordHasher.Hash(password);
        user.MustChangePassword = true;
        await db.ApiSessions.Where(s => s.UserId == user.Id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Recovery on the server PC when no administrator can log in: sets the password of <paramref name="username"/>
    /// (default: "admin", or the first administrator), makes it an active administrator and logs it out everywhere.
    /// </summary>
    public async Task<string> RecoverAdminAsync(string password, string? username, CancellationToken ct)
    {
        var errors = ValidatePassword(password);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        var user = username is { Length: > 0 }
            ? await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct)
            : await db.Users.FirstOrDefaultAsync(u => u.Username == "admin", ct)
              ?? await db.Users.Where(u => u.Role == UserRole.Admin).OrderBy(u => u.Id).FirstOrDefaultAsync(ct);
        if (user is null)
        {
            user = new AppUser { Username = username is { Length: > 0 } ? username : "admin", DisplayName = "Administrator", CreatedUtc = clock.GetUtcNow().UtcDateTime };
            db.Users.Add(user);
        }
        user.PasswordHash = PasswordHasher.Hash(password);
        user.Role = UserRole.Admin;
        user.IsActive = true;
        user.MustChangePassword = false;
        await db.SaveChangesAsync(ct);
        await db.ApiSessions.Where(s => s.UserId == user.Id).ExecuteDeleteAsync(ct);
        return user.Username;
    }

    public static IReadOnlyList<string> ValidatePassword(string password)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(password) || password.Length < 6) errors.Add("Password must be at least 6 characters.");
        return errors;
    }
}
