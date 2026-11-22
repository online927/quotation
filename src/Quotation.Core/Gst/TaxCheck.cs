namespace Quotation.Core.Gst;

/// <summary>
/// Checks made before a quotation is approved, so it never goes out with the wrong kind of GST: a GSTIN that does not
/// belong to the party's state, a GSTIN that is mistyped (wrong shape or wrong check letter), or no state at all (then IGST cannot be told from CGST + SGST).
/// </summary>
public static class TaxCheck
{
    public sealed record Party(string Caption, string Gstin, string StateName, string StateCode);

    /// <summary>The problems found; empty when everything agrees.</summary>
    public static List<string> Problems(params Party[] parties)
    {
        var problems = new List<string>();
        foreach (var p in parties)
        {
            var gstin = (p.Gstin ?? "").Trim().ToUpperInvariant();
            var code = (p.StateCode ?? "").Trim();
            // The format (15 characters: state code, PAN, …), then the state code and the check letter (the 15th character,
            // worked out from the other 14: one typing mistake in Tally changes it).
            if (gstin.Length > 0 && !Gstin.IsWellFormed(gstin))
            {
                problems.Add($"{p.Caption}'s GSTIN {gstin} looks mistyped (a GSTIN has 15 characters, e.g. 29ABCDE1234F1Z5).");
            }
            else if (gstin.Length > 0)
            {
                if (code.Length > 0 && gstin[..2] != code)
                {
                    var gstinState = IndianStates.NameForCode(gstin[..2]) ?? "code " + gstin[..2];
                    var state = string.IsNullOrWhiteSpace(p.StateName) ? "code " + code : p.StateName.Trim();
                    problems.Add($"{p.Caption}'s GSTIN {gstin} is from {gstinState} ({gstin[..2]}), but the state is {state} ({code}).");
                }
                if (Gstin.Problem(gstin) is { } mistake)
                {
                    problems.Add($"{p.Caption}'s GSTIN {gstin} " + mistake.Replace("Looks mistyped: the", "looks mistyped: its") + ".");
                }
            }
            if (code.Length == 0)
            {
                problems.Add($"{p.Caption} has no state: the app cannot tell whether IGST or CGST + SGST applies.");
            }
        }
        return problems;
    }

    /// <summary>"IGST (Maharashtra, another state)" or "CGST + SGST (Karnataka, same state)".</summary>
    public static string Describe(string companyStateCode, string placeOfSupplyCode)
    {
        if (string.IsNullOrWhiteSpace(placeOfSupplyCode)) return "CGST + SGST (no state: taken as the same state)";
        var name = IndianStates.NameForCode(placeOfSupplyCode) ?? "code " + placeOfSupplyCode;
        // The same rule as the totals: IGST only when both states are known and differ.
        var interState = !string.IsNullOrWhiteSpace(companyStateCode) && companyStateCode != placeOfSupplyCode;
        return interState ? $"IGST ({name}, another state)" : $"CGST + SGST ({name}, same state)";
    }
}
