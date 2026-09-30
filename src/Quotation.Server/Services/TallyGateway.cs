using Quotation.Tally;

namespace Quotation.Server.Services;

/// <summary>Creates Tally clients from the current settings (settings can change at runtime).</summary>
public sealed class TallyGateway(IHttpClientFactory httpFactory, SettingsService settings, ILoggerFactory loggers)
{
    public const string HttpClientName = "tally";

    public TallyXmlClient CreateClient(TimeSpan? timeout = null)
    {
        var s = settings.Tally;
        var http = httpFactory.CreateClient(HttpClientName);
        http.Timeout = Timeout.InfiniteTimeSpan; // per-request timeout is enforced by TallyXmlClient
        return new TallyXmlClient(http,
            new TallyConnectionOptions(s.Url, s.CompanyName, timeout ?? TimeSpan.FromSeconds(Math.Max(5, s.TimeoutSeconds))),
            loggers.CreateLogger<TallyXmlClient>());
    }

    public TallyCompanyService Companies(TimeSpan? timeout = null) => new(CreateClient(timeout));
}
