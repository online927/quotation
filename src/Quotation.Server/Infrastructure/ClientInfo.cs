using System.Security.Claims;

namespace Quotation.Server.Infrastructure;

public static class ClientInfo
{
    public const string MachineHeader = "X-Client-Machine";

    public static string UserName(this ClaimsPrincipal user) => user.Identity?.Name ?? "system";

    public static string DisplayName(this ClaimsPrincipal user) =>
        user.FindFirst("display_name")?.Value ?? user.UserName();

    public static string Machine(this HttpContext ctx)
    {
        var header = ctx.Request.Headers[MachineHeader].ToString();
        if (!string.IsNullOrWhiteSpace(header)) return header.Length > 64 ? header[..64] : header;
        return ctx.Connection.RemoteIpAddress?.ToString() ?? "";
    }
}
