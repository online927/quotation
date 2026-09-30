using Quotation.Core.Domain;

namespace Quotation.Core.Validation;

public sealed record ValidationLine(
    int LineNo,
    int? ProductId,
    string ItemName,
    string Hsn,
    decimal? GstRate,
    decimal Quantity,
    string Unit,
    decimal Rate,
    decimal DiscountPercent);

public sealed record ValidationInput(
    int? CustomerId,
    string BuyerName,
    DateOnly Date,
    FinancialYear ActiveFinancialYear,
    IReadOnlyList<ValidationLine> Lines,
    decimal PackingForwarding,
    bool RequireHsn);

public sealed record ValidationIssue(string Field, string Message, int? LineNo = null)
{
    public override string ToString() => LineNo is null ? Message : $"Line {LineNo}: {Message}";
}

/// <summary>Checks performed before a quotation may be approved and its PDF generated.</summary>
public static class QuotationValidator
{
    /// <summary>GST rates notified in India (percent).</summary>
    public static readonly IReadOnlySet<decimal> ValidGstRates = new HashSet<decimal> { 0m, 0.1m, 0.25m, 1m, 1.5m, 3m, 5m, 6m, 7.5m, 12m, 18m, 28m, 40m };

    public static List<ValidationIssue> Validate(ValidationInput q)
    {
        var issues = new List<ValidationIssue>();
        if (q.CustomerId is null) issues.Add(new("Customer", "Select a customer from Tally."));
        if (string.IsNullOrWhiteSpace(q.BuyerName)) issues.Add(new("Buyer", "Buyer name is empty."));
        if (!q.ActiveFinancialYear.Contains(q.Date))
        {
            issues.Add(new("Date", $"Date {q.Date:dd-MMM-yyyy} is outside the active financial year {q.ActiveFinancialYear.Label} " +
                                   $"({q.ActiveFinancialYear.StartDate:dd-MMM-yyyy} to {q.ActiveFinancialYear.EndDate:dd-MMM-yyyy})."));
        }
        if (q.PackingForwarding < 0) issues.Add(new("PackingForwarding", "Packing & forwarding cannot be negative."));
        if (q.Lines.Count == 0) issues.Add(new("Lines", "Add at least one product."));

        foreach (var l in q.Lines)
        {
            if (l.ProductId is null) issues.Add(new("Product", "Select the product from the Tally product list.", l.LineNo));
            if (l.Quantity <= 0) issues.Add(new("Quantity", "Quantity must be greater than zero.", l.LineNo));
            if (l.Rate <= 0) issues.Add(new("Rate", "Rate must be greater than zero.", l.LineNo));
            if (l.DiscountPercent is < 0 or >= 100) issues.Add(new("Discount", "Discount must be between 0 and 100%.", l.LineNo));
            if (l.GstRate is null) issues.Add(new("GST", "GST rate is missing (not set in Tally).", l.LineNo));
            else if (!ValidGstRates.Contains(l.GstRate.Value)) issues.Add(new("GST", $"GST rate {l.GstRate}% is not a valid GST rate.", l.LineNo));
            if (q.RequireHsn && string.IsNullOrWhiteSpace(l.Hsn)) issues.Add(new("HSN", "HSN/SAC is missing.", l.LineNo));
            else if (!string.IsNullOrWhiteSpace(l.Hsn) && !IsValidHsn(l.Hsn)) issues.Add(new("HSN", $"HSN/SAC '{l.Hsn}' is not 4, 6 or 8 digits.", l.LineNo));
            if (string.IsNullOrWhiteSpace(l.Unit)) issues.Add(new("Unit", "Unit is missing.", l.LineNo));
        }
        return issues;
    }

    public static bool IsValidHsn(string hsn)
    {
        var h = hsn.Trim();
        return h.Length is 4 or 6 or 8 && h.All(char.IsDigit);
    }
}
