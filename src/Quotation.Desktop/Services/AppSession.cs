using Quotation.ApiClient;

namespace Quotation.Desktop.Services;

/// <summary>Holds the per-session state shared by view models.</summary>
public sealed class AppSession(ClientConfig config, Func<string, QuotationApiClient> clientFactory)
{
    public ClientConfig Config { get; } = config;
    public QuotationApiClient Api { get; private set; } = clientFactory(config.ServerUrl);

    public void Reconnect(string serverUrl)
    {
        Config.ServerUrl = serverUrl;
        Api = clientFactory(serverUrl);
    }

    public bool IsAdmin => Api.CurrentUser?.Role == Core.Domain.UserRole.Admin;

    public DocumentLauncher Documents { get; set; } = new(config);

    /// <summary>Settings cached for the session (company state, tax presentation, defaults).</summary>
    public Contracts.AllSettingsDto? Settings { get; set; }

    public async Task<Contracts.AllSettingsDto> GetSettingsAsync(bool refresh = false)
    {
        if (Settings is null || refresh) Settings = await Api.SettingsAsync();
        return Settings;
    }
}
