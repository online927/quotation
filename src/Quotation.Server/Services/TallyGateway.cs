using Quotation.Tally;

namespace Quotation.Server.Services;

/// <summary>Creates Tally clients from the current settings (settings can change at runtime).</summary>
public sealed class TallyGateway(IHttpClientFactory httpFactory, SettingsService settings, TallyLoadGuard load, ILoggerFactory loggers)
{
    public const string HttpClientName = "tally";

    /// <summary>All requests from this server pass the same load guard (one at a time, measured).</summary>
    public TallyLoadGuard Load => load;

    public TallyXmlClient CreateClient(TimeSpan? timeout = null, bool probe = false)
    {
        var s = settings.Tally;
        var http = httpFactory.CreateClient(HttpClientName);
        http.Timeout = Timeout.InfiniteTimeSpan; // per-request timeout is enforced by TallyXmlClient
        return new TallyXmlClient(http,
            new TallyConnectionOptions(s.Url, s.CompanyName, timeout ?? TimeSpan.FromSeconds(Math.Max(5, s.TimeoutSeconds))),
            loggers.CreateLogger<TallyXmlClient>(), load, probe);
    }

    /// <summary>Company/period requests are small and regular: they measure Tally's response time.</summary>
    public TallyCompanyService Companies(TimeSpan? timeout = null) => new(CreateClient(timeout, probe: true));
}
