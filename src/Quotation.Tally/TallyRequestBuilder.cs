using System.Security;
using System.Text;

namespace Quotation.Tally;

/// <summary>A TDL collection definition sent inline with an Export request.</summary>
public sealed class TallyCollectionRequest
{
    public required string Name { get; init; }
    /// <summary>Tally object type: Company, StockItem, StockGroup, Unit, Ledger …</summary>
    public required string Type { get; init; }
    public IReadOnlyList<string> NativeMethods { get; init; } = ["*"];
    /// <summary>Computed methods: name → TDL formula (e.g. "QtFrom" → "##SVFromDate").</summary>
    public IReadOnlyDictionary<string, string> Computes { get; init; } = new Dictionary<string, string>();
    /// <summary>Filters: formula name → TDL formula. All filters must be true.</summary>
    public IReadOnlyDictionary<string, string> Filters { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Builds TallyPrime XML requests. This builder can only produce <c>Export</c> requests:
/// the application never imports, alters or deletes anything in Tally.
/// </summary>
public static class TallyRequestBuilder
{
    public static string Collection(TallyCollectionRequest c, string? companyName, IReadOnlyDictionary<string, string>? staticVariables = null)
    {
        var sb = new StringBuilder();
        sb.Append("<ENVELOPE><HEADER><VERSION>1</VERSION><TALLYREQUEST>Export</TALLYREQUEST><TYPE>Collection</TYPE>");
        sb.Append("<ID>").Append(Esc(c.Name)).Append("</ID></HEADER><BODY><DESC><STATICVARIABLES>");
        sb.Append("<SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>");
        if (!string.IsNullOrWhiteSpace(companyName))
        {
            sb.Append("<SVCURRENTCOMPANY>").Append(Esc(companyName)).Append("</SVCURRENTCOMPANY>");
        }
        if (staticVariables is not null)
        {
            foreach (var (k, v) in staticVariables)
            {
                sb.Append('<').Append(k.ToUpperInvariant()).Append('>').Append(Esc(v)).Append("</").Append(k.ToUpperInvariant()).Append('>');
            }
        }
        sb.Append("</STATICVARIABLES><TDL><TDLMESSAGE>");
        sb.Append("<COLLECTION NAME=\"").Append(Esc(c.Name)).Append("\" ISMODIFY=\"No\" ISFIXED=\"No\" ISINITIALIZE=\"No\" ISOPTION=\"No\" ISINTERNAL=\"No\">");
        sb.Append("<TYPE>").Append(Esc(c.Type)).Append("</TYPE>");
        sb.Append("<NATIVEMETHOD>").Append(Esc(string.Join(", ", c.NativeMethods))).Append("</NATIVEMETHOD>");
        foreach (var (name, formula) in c.Computes)
        {
            sb.Append("<COMPUTE>").Append(Esc($"{name} : {formula}")).Append("</COMPUTE>");
        }
        if (c.Filters.Count > 0)
        {
            sb.Append("<FILTER>").Append(Esc(string.Join(", ", c.Filters.Keys))).Append("</FILTER>");
        }
        sb.Append("</COLLECTION>");
        foreach (var (name, formula) in c.Filters)
        {
            sb.Append("<SYSTEM TYPE=\"Formulae\" NAME=\"").Append(Esc(name)).Append("\">").Append(Esc(formula)).Append("</SYSTEM>");
        }
        sb.Append("</TDLMESSAGE></TDL></DESC></BODY></ENVELOPE>");
        return sb.ToString();
    }

    private static string Esc(string s) => SecurityElement.Escape(s) ?? "";
}
