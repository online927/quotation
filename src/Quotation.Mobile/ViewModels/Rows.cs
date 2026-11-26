using Quotation.Contracts;
using Quotation.Core.Calculation;
using Quotation.Core.Domain;

namespace Quotation.Mobile.ViewModels;

/// <summary>A quotation in a list: number and status, customer and total, date and who made it.</summary>
public sealed class QuotationRow(QuotationSummaryDto q)
{
    public QuotationSummaryDto Summary { get; } = q;
    public Guid Id => Summary.Id;
    public string Number => Summary.Number ?? "Draft";
    public string Customer => Summary.CustomerName.Length > 0 ? Summary.CustomerName : "(no customer yet)";
    public string Total => Summary.TotalText;
    public string ShortTotal => (Summary.IsUsd ? "$ " : "") + IndianFormat.Amount(Summary.GrandTotal, 0);
    public string DateText => Summary.Date.ToString("dd-MMM-yy");
    public string When => $"{Summary.Date:dd-MMM} · by {Summary.CreatedBy}";
    public string StatusText => Status(Summary.Status, Summary.DocType);
    public bool IsOk => Summary.Status is QuotationStatus.Approved or QuotationStatus.Generated && Summary.DocType == DocumentType.Quotation;
    public bool IsWarn => Summary.DocType == DocumentType.ProformaInvoice && Summary.Status != QuotationStatus.Cancelled || Summary.Status == QuotationStatus.PendingReview;
    public bool IsBad => Summary.Status == QuotationStatus.Cancelled;

    public static string Status(QuotationStatus status, DocumentType type) => status switch
    {
        QuotationStatus.Draft => "Draft",
        QuotationStatus.PendingReview => "To review",
        QuotationStatus.Cancelled => "Cancelled",
        _ => type == DocumentType.ProformaInvoice ? "Proforma" : "Approved",
    };
}

/// <summary>A line of a quotation on the phone: name, "qty × rate", amount.</summary>
public sealed class LineRow(QuotationLineDto l, bool usd = false)
{
    public string No => l.LineNo.ToString();
    public string Name => l.ItemName;
    public string Detail => $"{l.Quantity:0.###} {l.Unit} × {(usd ? "$" : "₹")}{IndianFormat.Amount(l.Rate)}".Replace("  ", " ")
                            + (l.DiscountPercent > 0 ? $" · {l.DiscountPercent:0.##} %" : "");
    public string Amount => IndianFormat.Amount(l.Amount);
}

/// <summary>A name and a value in a details card (GSTIN, Contact …).</summary>
public sealed record InfoRow(string Label, string Value, string? Link = null)
{
    public bool IsLink => Link is not null;
    public bool IsText => Link is null;
}

/// <summary>A tax or total line under the items.</summary>
public sealed record TotalRow(string Label, string Value);

/// <summary>Round 41 (Z8 phone): one internal note: its text and "Ravi · 26-Nov 11:42 (edited)".</summary>
public sealed class NoteRow(QuotationNoteDto n)
{
    public int Id => n.Id;
    public string Text => n.Text;
    public string Who => $"{(n.DisplayName.Length > 0 ? n.DisplayName : n.UserName)} · {n.CreatedUtc.ToLocalTime():dd-MMM HH:mm}"
                         + (n.IsEdited ? " (edited)" : "");
    public bool Mine => n.Mine;
}
