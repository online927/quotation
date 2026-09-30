using Quotation.Core.Calculation;
using Quotation.Core.Domain;
using Quotation.Data.Entities;
using Quotation.Pdf;

namespace Quotation.Server.Services;

/// <summary>Maps a saved quotation to the print model and renders the Tally-style PDF.</summary>
public sealed class PdfQuotationRenderer(ILogger<PdfQuotationRenderer> log) : IQuotationDocumentRenderer
{
    private readonly QuotationPdfRenderer _renderer = new();

    public byte[] Render(QuotationHeader q, CompanySettings company, QuotationSettings settings) =>
        _renderer.Render(ToDocument(q, company, settings, watermark: null));

    public byte[] RenderPreview(QuotationHeader q, CompanySettings company, QuotationSettings settings, string provisionalNumber) =>
        _renderer.Render(ToDocument(q, company, settings, watermark: "DRAFT", provisionalNumber));

    public QuotationDocument ToDocument(QuotationHeader q, CompanySettings company, QuotationSettings settings, string? watermark,
        string? provisionalNumber = null)
    {
        var lines = q.Lines.OrderBy(l => l.LineNo).Select(l => new PdfLine(
            l.LineNo,
            l.ItemName,
            SplitLines(l.Description),
            l.Hsn,
            l.GstRate,
            l.DueOn,
            l.Quantity,
            l.Unit,
            l.Rate,
            l.DiscountPercent,
            l.Amount)).ToList();

        var companyState = company.StateCode;
        var placeOfSupply = q.Consignee.StateCode.Length > 0 ? q.Consignee.StateCode : q.Buyer.StateCode;
        var calc = QuotationCalculator.Calculate(new CalcInput(
            q.Lines.OrderBy(l => l.LineNo).Select(l => new CalcLine(l.Quantity, l.Rate, l.DiscountPercent, l.GstRate ?? 0, l.Unit)).ToList(),
            q.PackingForwarding, null, settings.TaxPresentation, settings.RoundOffTotal,
            companyState.Length > 0 && placeOfSupply.Length > 0 && companyState != placeOfSupply));

        var summary = new List<PdfSummaryRow>();
        if (calc.PackingForwarding != 0) summary.Add(new PdfSummaryRow("Packing & Forwarding", "", calc.PackingForwarding));
        foreach (var t in calc.Taxes) summary.Add(new PdfSummaryRow(t.Name, $"{t.RatePercent:0.##} %", t.Amount));
        if (calc.RoundOff != 0) summary.Add(new PdfSummaryRow("Round Off", "", calc.RoundOff));

        return new QuotationDocument
        {
            Company = company,
            Logo = LoadLogo(company.LogoPath),
            Number = q.Number ?? provisionalNumber ?? "(not numbered)",
            Date = q.Date,
            BuyersReference = q.BuyersReference,
            BuyersReferenceDate = q.BuyersReferenceDate,
            DispatchedThrough = q.DispatchedThrough,
            Destination = q.Destination,
            PaymentTerms = q.PaymentTerms,
            OtherReferences = q.OtherReferences,
            TermsOfDelivery = q.TermsOfDelivery,
            Buyer = ToParty(q.Buyer),
            Consignee = ToParty(q.ConsigneeSameAsBuyer ? q.Buyer : q.Consignee, includeContact: false),
            Lines = lines,
            Subtotal = calc.Subtotal,
            SummaryRows = summary,
            GrandTotal = calc.GrandTotal,
            TotalQuantity = calc.TotalQuantity,
            TotalQuantityUnit = calc.TotalQuantityUnit,
            AmountInWords = IndianFormat.AmountInWords(calc.GrandTotal, settings.CurrencyWordsPrefix),
            Remarks = q.Remarks,
            TermsAndConditions = SplitLines(q.TermsAndConditions),
            PreparedBy = q.PreparedBy,
            VerifiedBy = q.VerifiedBy,
            Watermark = watermark,
        };
    }

    private static PdfParty ToParty(PartySnapshot p, bool includeContact = true) => new(
        p.Name, SplitLines(p.Address), p.Gstin, p.StateName, p.StateCode,
        includeContact ? p.ContactPerson : "", includeContact ? p.Phone : "", includeContact ? p.Email : "");

    private static List<string> SplitLines(string? s) =>
        string.IsNullOrWhiteSpace(s) ? [] : s.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    private byte[]? LoadLogo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex)
        {
            log.LogWarning("Logo {Path} could not be read: {Message}", path, ex.Message);
            return null;
        }
    }
}
