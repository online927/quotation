namespace Quotation.Server.Tests;

public class TallyApiTests : IDisposable
{
    private readonly TestServer _server = new(today: new DateOnly(2026, 9, 30));

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task Test_connection_reports_company_and_financial_year()
    {
        var api = await _server.LoginAsAdminAsync();
        var result = await api.TestTallyAsync();
        Assert.True(result.Success, result.Message);
        Assert.Equal("T.SAIFUDDIN & CO.", result.Company);
        Assert.Equal("2026-27", result.ActiveFinancialYear);

        var status = await api.StatusAsync();
        Assert.True(status.TallyConnected);
        Assert.Equal("T.SAIFUDDIN & CO.", status.TallyCompany);
        Assert.Equal("2026-27", status.ActiveFinancialYear);
        Assert.Equal("Tally", status.ActiveFinancialYearSource);
    }

    [Fact]
    public async Task Disconnection_is_reported_and_last_known_year_is_kept()
    {
        var api = await _server.LoginAsAdminAsync();
        _server.Tally.Data.PeriodFrom = new DateOnly(2025, 4, 1);
        Assert.True((await api.TestTallyAsync()).Success);

        _server.Tally.Online = false;
        var result = await api.TestTallyAsync();
        Assert.False(result.Success);
        Assert.Contains("Cannot connect to Tally", result.Message);

        var status = await api.StatusAsync();
        Assert.False(status.TallyConnected);
        Assert.NotNull(status.TallyError);
        // Detected year stays in effect while Tally is down.
        Assert.Equal("2025-26", status.ActiveFinancialYear);
    }

    [Fact]
    public async Task Financial_year_override_wins()
    {
        var api = await _server.LoginAsAdminAsync();
        await api.TestTallyAsync();
        var s = await api.SettingsAsync();
        s.Tally.ActiveFinancialYearOverride = 2024;
        await api.SaveSettingsAsync(s);
        var status = await api.StatusAsync();
        Assert.Equal("2024-25", status.ActiveFinancialYear);
        Assert.Equal("Override", status.ActiveFinancialYearSource);
    }

    [Fact]
    public async Task Wrong_company_name_is_explained()
    {
        var api = await _server.LoginAsAdminAsync();
        var s = await api.SettingsAsync();
        s.Tally.CompanyName = "SOME OTHER CO";
        await api.SaveSettingsAsync(s);
        var result = await api.TestTallyAsync();
        Assert.False(result.Success);
        Assert.Contains("SOME OTHER CO", result.Message);
    }

    [Fact]
    public async Task Server_never_sends_non_export_requests()
    {
        var api = await _server.LoginAsAdminAsync();
        await api.TestTallyAsync();
        await api.TallyCompaniesAsync();
        Assert.True(_server.Tally.RequestCount > 0);
        Assert.Equal(0, _server.Tally.NonExportRequestCount);
    }
}
