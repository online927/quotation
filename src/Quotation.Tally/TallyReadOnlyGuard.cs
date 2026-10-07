using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Quotation.Tally;

/// <summary>
/// Last line of defence that keeps Tally data untouched. Every request is checked before it is sent:
/// it must be a single Export request whose inline TDL only defines read-only collections and
/// formulae. Anything that could import, create, alter or delete data in Tally is refused.
/// </summary>
public static partial class TallyReadOnlyGuard
{
    // Elements that carry data or actions into Tally (imports, vouchers/masters, TDL functions/actions).
    private static readonly HashSet<string> ForbiddenElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "IMPORTDATA", "REQUESTDATA", "TALLYMESSAGE", "FUNCTION", "ACTION", "BUTTON", "KEY", "FORM",
        "PART", "LINE", "FIELD", "MENU", "REPORT", "OBJECT", "VOUCHER", "LEDGER", "STOCKITEM",
    };

    // TDL actions that write data; refused anywhere in the request text.
    private static readonly string[] ForbiddenText =
    [
        "SAVE TARGET", "CREATE TARGET", "ALTER TARGET", "DELETE TARGET", "NEW OBJECT",
        "SET TARGET", "INSERT COLLECTION OBJECT", "SYNC", "EXECUTE", "IMPORT",
    ];

    /// <exception cref="InvalidOperationException">The request is not a pure read request.</exception>
    public static void EnsureReadOnly(string requestXml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(requestXml);
        }
        catch (XmlException)
        {
            throw Refuse("the request is not valid XML");
        }

        if (doc.Root?.Name.LocalName != "ENVELOPE") throw Refuse("unexpected request envelope");

        var requests = doc.Descendants().Where(e => e.Name.LocalName.Equals("TALLYREQUEST", StringComparison.OrdinalIgnoreCase)).ToList();
        if (requests.Count != 1 || requests[0].Value.Trim() != "Export") throw Refuse("only Export requests may be sent to Tally");

        foreach (var e in doc.Descendants())
        {
            if (ForbiddenElements.Contains(e.Name.LocalName)) throw Refuse($"element <{e.Name.LocalName}> is not allowed");
        }

        foreach (var tdl in doc.Descendants("TDLMESSAGE"))
        {
            foreach (var def in tdl.Elements())
            {
                var kind = def.Name.LocalName;
                if (kind == "COLLECTION")
                {
                    if (!string.Equals((string?)def.Attribute("ISMODIFY") ?? "No", "No", StringComparison.OrdinalIgnoreCase))
                        throw Refuse("collections may not modify existing definitions");
                }
                else if (!(kind == "SYSTEM" && string.Equals((string?)def.Attribute("TYPE"), "Formulae", StringComparison.OrdinalIgnoreCase)))
                {
                    throw Refuse($"TDL definition <{kind}> is not allowed");
                }
            }
        }

        // Only TDL formulas are checked for action words, and quoted names inside them are skipped, so a
        // company, group or item named e.g. "ABC IMPORTS" still works.
        foreach (var tdl in doc.Descendants("TDL"))
        {
            var formulas = string.Join(" ", tdl.DescendantNodes().OfType<XText>().Select(t => t.Value));
            var tdlText = QuotedLiteral().Replace(formulas, "\"\"");
            foreach (var text in ForbiddenText)
            {
                if (tdlText.Contains(text, StringComparison.OrdinalIgnoreCase)) throw Refuse($"\"{text}\" is not allowed");
            }
        }
    }

    // A TDL string literal; a doubled quote inside it is an escaped quote.
    [GeneratedRegex("\"(?:[^\"]|\"\")*\"")]
    private static partial Regex QuotedLiteral();

    private static InvalidOperationException Refuse(string reason) =>
        new($"Blocked a request to Tally ({reason}). This application only reads from Tally and never changes Tally data.");
}
