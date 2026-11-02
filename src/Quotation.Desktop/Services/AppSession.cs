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

    /// <summary>Shows the screens light or dark and remembers it on this PC for the login screen.</summary>
    public void ApplyTheme(string theme)
    {
        ThemeService.Apply(theme);
        if (Config.Theme == theme) return;
        Config.Theme = theme;
        try
        {
            Config.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only the login screen's look depends on it.
        }
    }

    /// <summary>Saves the signed-in user's light/dark choice on the server (it follows them to every PC).</summary>
    public async Task SetThemeAsync(string theme)
    {
        ApplyTheme(theme);
        Api.UpdateCurrentUser(await Api.SetMyPreferencesAsync(new Contracts.MyPreferencesDto(Theme: theme)));
    }

    /// <summary>Settings cached for the session (company state, tax presentation, defaults).</summary>
    public Contracts.AllSettingsDto? Settings { get; set; }

    public async Task<Contracts.AllSettingsDto> GetSettingsAsync(bool refresh = false)
    {
        if (Settings is null || refresh) Settings = await Api.SettingsAsync();
        return Settings;
    }
}
