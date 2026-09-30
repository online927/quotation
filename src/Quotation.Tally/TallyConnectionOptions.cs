namespace Quotation.Tally;

/// <param name="Url">TallyPrime XML/HTTP endpoint, e.g. http://192.168.1.10:9000</param>
/// <param name="CompanyName">Company to query; empty = the company currently selected in that Tally instance.</param>
public sealed record TallyConnectionOptions(string Url, string CompanyName, TimeSpan Timeout);
