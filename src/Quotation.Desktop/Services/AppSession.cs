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
}
