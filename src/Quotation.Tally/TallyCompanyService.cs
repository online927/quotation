using System.Diagnostics;
using System.Xml.Linq;

namespace Quotation.Tally;

public sealed record TallyCompany(
    string Name,
    string Guid,
    DateOnly? FinancialYearFrom,
    DateOnly? BooksFrom,
    long MaxMasterAlterId,
    string StateName,
    string Gstin);

/// <summary>Information about the company and the period currently active in the Tally instance.</summary>
public sealed record TallyCompanyInfo(
    TallyCompany Company,
    IReadOnlyList<string> LoadedCompanies,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    DateOnly? CurrentDate,
    int? ActiveFinancialYearStart,
    TimeSpan ResponseTime);

/// <summary>Company discovery, connection test and active financial-year detection.</summary>
public sealed class TallyCompanyService(TallyXmlClient client)
{
    internal static readonly string[] CompanyMethods =
    [
        "Name", "GUID", "StartingFrom", "BooksFrom", "AltMstId", "AlterID", "StateName", "GSTRegistrationNumber",
    ];

    /// <summary>Lists the companies currently loaded in the Tally instance.</summary>
    public async Task<IReadOnlyList<TallyCompany>> GetLoadedCompaniesAsync(CancellationToken ct = default)
    {
        var request = TallyRequestBuilder.Collection(new TallyCollectionRequest
        {
            Name = "QtCompanies",
            Type = "Company",
            NativeMethods = CompanyMethods,
        }, companyName: null);
        var doc = await client.ExportAsync(request, ct);
        return doc.Descendants("COMPANY").Select(ParseCompany).Where(c => c.Name.Length > 0).ToList();
    }

    /// <summary>
    /// Tests the connection and reads the active company and period. The active financial year is
    /// derived from the current period (Alt+F2) of the Tally instance.
    /// </summary>
    public async Task<TallyCompanyInfo> GetCompanyInfoAsync(int financialYearStartMonth = 4, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var loaded = await GetLoadedCompaniesAsync(ct);
        if (loaded.Count == 0)
        {
            throw new TallyException("Tally is running but no company is open. Open the company in TallyPrime.");
        }

        var wanted = client.Options.CompanyName;
        var request = TallyRequestBuilder.Collection(new TallyCollectionRequest
        {
            Name = "QtCompanyPeriod",
            Type = "Company",
            NativeMethods = CompanyMethods,
            Computes = new Dictionary<string, string>
            {
                ["QtCurrentCompany"] = "##SVCurrentCompany",
                ["QtFromDate"] = "##SVFromDate",
                ["QtToDate"] = "##SVToDate",
                ["QtCurrentDate"] = "##SVCurrentDate",
            },
        }, string.IsNullOrWhiteSpace(wanted) ? null : wanted);
        var doc = await client.ExportAsync(request, ct);
        var companies = doc.Descendants("COMPANY").ToList();

        XElement? selected;
        if (!string.IsNullOrWhiteSpace(wanted))
        {
            selected = companies.FirstOrDefault(c => NameOf(c).Equals(wanted.Trim(), StringComparison.OrdinalIgnoreCase));
            if (selected is null)
            {
                throw new TallyException(
                    $"Company '{wanted}' is not open in Tally. Open companies: {string.Join(", ", loaded.Select(c => c.Name))}.");
            }
        }
        else
        {
            var current = companies.Select(c => TallyText.Text(c, "QTCURRENTCOMPANY")).FirstOrDefault(s => s.Length > 0);
            selected = companies.FirstOrDefault(c => NameOf(c).Equals(current, StringComparison.OrdinalIgnoreCase))
                       ?? companies.FirstOrDefault();
        }
        if (selected is null) throw new TallyException("Tally did not return company details.");

        var company = ParseCompany(selected);
        var from = TallyText.Date(TallyText.Text(selected, "QTFROMDATE"));
        var to = TallyText.Date(TallyText.Text(selected, "QTTODATE"));
        var currentDate = TallyText.Date(TallyText.Text(selected, "QTCURRENTDATE"));

        return new TallyCompanyInfo(
            company,
            loaded.Select(c => c.Name).ToList(),
            from,
            to,
            currentDate,
            DetectFinancialYear(from, currentDate, company.FinancialYearFrom, financialYearStartMonth),
            sw.Elapsed);
    }

    /// <summary>
    /// Active FY = FY containing the start of Tally's current period; falls back to the FY of Tally's
    /// current date. The company's "financial year beginning from" is only the first FY of the books,
    /// so it is used just to align the start month.
    /// </summary>
    public static int? DetectFinancialYear(DateOnly? periodFrom, DateOnly? currentDate, DateOnly? companyFyFrom, int startMonth)
    {
        if (companyFyFrom is { } cf) startMonth = cf.Month;
        var basis = periodFrom ?? currentDate;
        if (basis is null) return null;
        return basis.Value.Month >= startMonth ? basis.Value.Year : basis.Value.Year - 1;
    }

    internal static TallyCompany ParseCompany(XElement e) => new(
        NameOf(e),
        TallyText.Text(e, "GUID"),
        TallyText.Date(TallyText.Text(e, "STARTINGFROM")),
        TallyText.Date(TallyText.Text(e, "BOOKSFROM")),
        TallyText.Long(TallyText.Text(e, "ALTMSTID")),
        TallyText.Text(e, "STATENAME"),
        TallyText.Text(e, "GSTREGISTRATIONNUMBER"));

    internal static string NameOf(XElement e)
    {
        var attr = TallyText.Clean(e.Attribute("NAME")?.Value);
        return attr.Length > 0 ? attr : TallyText.Text(e, "NAME");
    }
}
