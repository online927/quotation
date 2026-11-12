using CommunityToolkit.Mvvm.ComponentModel;
using Quotation.Contracts;
using Quotation.Core.Domain;

namespace Quotation.Desktop.ViewModels;

/// <summary>A quotation (or proforma) whose number matches what is typed in the top search box.</summary>
public sealed partial class NumberMatchViewModel(QuotationSummaryDto summary, string yearNote) : ObservableObject
{
    public QuotationSummaryDto Summary { get; } = summary;
    public Guid Id => Summary.Id;

    [ObservableProperty] private bool _isHighlighted;

    public bool IsProforma => Summary.DocType == DocumentType.ProformaInvoice;
    public string Kind => IsProforma ? "Proforma" : "Quotation";
    public string Badge => IsProforma ? "PI" : "Q";
    public string Title => $"{Kind} {Summary.Number}";
    public string YearNote => yearNote;
    public bool HasYearNote => yearNote.Length > 0;
    public string StatusText => Summary.Status == QuotationStatus.PendingReview ? "Pending review" : Summary.Status.ToString();
    public bool IsFinal => Summary.Status is QuotationStatus.Approved or QuotationStatus.Generated;
    public bool IsPending => Summary.Status is QuotationStatus.Draft or QuotationStatus.PendingReview;
    public bool IsCancelled => Summary.Status == QuotationStatus.Cancelled;

    public string Detail =>
        $"{Summary.CustomerName} · {Summary.Date:dd-MMM-yyyy} · ₹ {Quotation.Core.Calculation.IndianFormat.Amount(Summary.GrandTotal)} · {Summary.Owner ?? Summary.CreatedBy}";
}
