using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quotation.Data;

namespace Quotation.Server.Auth;

/// <summary>Validates opaque bearer tokens issued by <see cref="AuthService"/> against ApiSessions.</summary>
public sealed class TokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    QuotationDbContext db,
    TimeProvider clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Token";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var token = header["Bearer ".Length..].Trim();
        if (token.Length == 0) return AuthenticateResult.NoResult();

        var hash = PasswordHasher.HashToken(token);
        var now = clock.GetUtcNow().UtcDateTime;
        var session = await db.ApiSessions.Include(s => s.User)
            .FirstOrDefaultAsync(s => s.TokenHash == hash, Context.RequestAborted);
        if (session?.User is null || !session.User.IsActive || session.ExpiresUtc < now)
        {
            return AuthenticateResult.Fail("Invalid or expired session.");
        }

        if (now - session.LastSeenUtc > TimeSpan.FromMinutes(5))
        {
            session.LastSeenUtc = now;
            await db.SaveChangesAsync(Context.RequestAborted);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.User.Id.ToString()),
            new(ClaimTypes.Name, session.User.Username),
            new("display_name", session.User.DisplayName),
            new(ClaimTypes.Role, session.User.Role.ToString()),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
