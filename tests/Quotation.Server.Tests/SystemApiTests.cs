using System.Net;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Server.Infrastructure;

namespace Quotation.Server.Tests;

public class SystemApiTests : IDisposable
{
    private readonly TestServer _server = new();

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task Health_is_anonymous()
    {
        var health = await _server.CreateApi().HealthAsync();
        Assert.Equal("ok", health.Status);
    }

    [Fact]
    public async Task Protected_endpoints_require_login()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => _server.CreateApi().DashboardAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, ex.Status);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_and_audited()
    {
        var api = _server.CreateApi();
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.LoginAsync("admin", "nope"));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.Status);

        var admin = await _server.LoginAsAdminAsync();
        var audit = await admin.AuditAsync();
        Assert.Contains(audit, a => a.Action == "LoginFailed" && a.EntityId == "admin");
    }

    [Fact]
    public async Task Bootstrap_admin_must_change_password()
    {
        var api = await _server.LoginAsAdminAsync();
        Assert.True(api.CurrentUser!.MustChangePassword);
        await api.ChangePasswordAsync("admin123", "better-password");
        var me = await api.MeAsync();
        Assert.False(me.MustChangePassword);

        var again = _server.CreateApi();
        await again.LoginAsync("admin", "better-password");
    }

    [Fact]
    public async Task Logout_revokes_token()
    {
        var api = await _server.LoginAsAdminAsync();
        var token = api.Token;
        await api.LogoutAsync();
        api.SetToken(token);
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.DashboardAsync());
        Assert.True(ex.IsUnauthorized);
    }

    [Fact]
    public async Task Dashboard_reports_empty_system()
    {
        var api = await _server.LoginAsAdminAsync();
        var dash = await api.DashboardAsync();
        Assert.Equal(0, dash.TodaysQuotations);
        Assert.False(dash.Status.TallyConnected);
        Assert.True(dash.Status.DataIsStale);
        Assert.Equal("System date", dash.Status.ActiveFinancialYearSource);
    }

    [Fact]
    public async Task Settings_round_trip_and_secrets_are_write_only()
    {
        var api = await _server.LoginAsAdminAsync();
        var s = await api.SettingsAsync();
        s.Company.CompanyName = "T.SAIFUDDIN & CO.";
        s.Company.AddressLines = ["No. 72, N.R.Road, Bangalore", "INDIA"];
        s.Quotation.NumberPattern = "TSQ{FY}-{SEQ}";
        s.Ai.ApiKey = "sk-ant-api03-verysecretvalue";
        var saved = await api.SaveSettingsAsync(s);

        Assert.Equal("T.SAIFUDDIN & CO.", saved.Company.CompanyName);
        Assert.Null(saved.Ai.ApiKey);
        Assert.True(saved.Ai.ApiKeyConfigured);

        var reloaded = await api.SettingsAsync();
        Assert.Equal(2, reloaded.Company.AddressLines.Count);
        Assert.Null(reloaded.Ai.ApiKey);
        Assert.True(reloaded.Ai.ApiKeyConfigured);

        // Saving again without a key keeps the stored key.
        await api.SaveSettingsAsync(reloaded);
        Assert.True((await api.SettingsAsync()).Ai.ApiKeyConfigured);
    }

    [Fact]
    public async Task Invalid_settings_are_rejected()
    {
        var api = await _server.LoginAsAdminAsync();
        var s = await api.SettingsAsync();
        s.Quotation.NumberPattern = "TSQ{FY}";
        s.Tally.Url = "not a url";
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.SaveSettingsAsync(s));
        Assert.Equal(HttpStatusCode.BadRequest, ex.Status);
        Assert.Equal(2, ex.Details.Count);
    }

    [Fact]
    public async Task Non_admin_cannot_change_settings()
    {
        var admin = await _server.LoginAsAdminAsync();
        await admin.CreateUserAsync(new CreateUserRequest("clerk", "Clerk", "clerk123", UserRole.User));

        var clerk = _server.CreateApi();
        await clerk.LoginAsync("clerk", "clerk123");
        var s = await clerk.SettingsAsync();
        var ex = await Assert.ThrowsAsync<ApiException>(() => clerk.SaveSettingsAsync(s));
        Assert.Equal(HttpStatusCode.Forbidden, ex.Status);
    }

    [Fact]
    public async Task Deactivated_user_cannot_log_in()
    {
        var admin = await _server.LoginAsAdminAsync();
        var user = await admin.CreateUserAsync(new CreateUserRequest("temp", "Temp", "temp123", UserRole.User));
        await admin.SetUserActiveAsync(user.Id, false);
        await Assert.ThrowsAsync<ApiException>(() => _server.CreateApi().LoginAsync("temp", "temp123"));
    }

    [Theory]
    [InlineData("key sk-ant-api03-abcdefghijklmnop end", "key sk-ant-*** end")]
    [InlineData("Authorization: Bearer abcdefghijklmnop123", "Authorization: Bearer ***")]
    [InlineData("{\"refresh_token\": \"1//0gabc\", \"x\": 1}", "{\"refresh_token\": \"***\", \"x\": 1}")]
    [InlineData("token ya29.a0AfH6SMBxyz", "token ya29.***")]
    public void Log_redaction(string input, string expected)
    {
        Assert.Equal(expected, RedactingFormatter.Redact(input));
    }

    [Fact]
    public void Secret_protector_round_trip()
    {
        var protector = new SecretProtector(_server.DataDirectory);
        var protectedText = protector.Protect("sk-ant-secret");
        Assert.DoesNotContain("sk-ant-secret", protectedText);
        Assert.Equal("sk-ant-secret", protector.Unprotect(protectedText));
    }
}
