namespace Quotation.Core.Gst;

/// <summary>
/// Which state's GST a quotation follows when the buyer (bill to) and the consignee (ship to) are in different states.
/// For "bill to – ship to" GST normally follows the buyer's state (IGST Act, section 10(1)(b)); the user can choose the
/// ship-to state instead. Quotations made before this choice existed followed the ship-to state and keep doing so.
/// </summary>
public static class PlaceOfSupply
{
    /// <summary>The state code whose GST applies; the other one when the chosen party has no state.</summary>
    public static string Code(string? buyerCode, string? shipToCode, bool byBuyer)
    {
        var buyer = (buyerCode ?? "").Trim();
        var shipTo = (shipToCode ?? "").Trim();
        if (byBuyer) return buyer.Length > 0 ? buyer : shipTo;
        return shipTo.Length > 0 ? shipTo : buyer;
    }

    /// <summary>IGST when both states are known and differ (the same rule as the totals); CGST + SGST otherwise.</summary>
    public static bool IsInterState(string? companyCode, string? placeCode)
    {
        var company = (companyCode ?? "").Trim();
        var place = (placeCode ?? "").Trim();
        return company.Length > 0 && place.Length > 0 && company != place;
    }

    /// <summary>True when the buyer's and the ship-to's states give a different GST (IGST against CGST + SGST): then the
    /// user chooses. Two other states (IGST either way) or the same state need no choice.</summary>
    public static bool ChoiceMatters(string? companyCode, string? buyerCode, string? shipToCode)
    {
        var buyer = (buyerCode ?? "").Trim();
        var shipTo = (shipToCode ?? "").Trim();
        return buyer.Length > 0 && shipTo.Length > 0 && buyer != shipTo
               && IsInterState(companyCode, buyer) != IsInterState(companyCode, shipTo);
    }

    /// <summary>"IGST" or "CGST + SGST" for a state.</summary>
    public static string GstName(string? companyCode, string? placeCode) => IsInterState(companyCode, placeCode) ? "IGST" : "CGST + SGST";
}
