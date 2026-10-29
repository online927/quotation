using Quotation.Core.Calculation;
using Quotation.Core.Domain;
using Quotation.Data.Entities;
using Quotation.Pdf;

namespace Quotation.Server.Services;

/// <summary>Maps a saved quotation to the print model and renders the Tally-style PDF.</summary>
public sealed class PdfQuotationRenderer(ILogger<PdfQuotationRenderer> log) : IQuotationDocumentRenderer
{
    private readonly QuotationPdfRenderer _renderer = new();

    public byte[] Render(QuotationHeader q, CompanySettings company, QuotationSettings settings, PrintStamp? printed = null, byte[]? seal = null) =>
        _renderer.Render(ToDocument(q, company, settings, watermark: null, printed: printed) with { Seal = seal });

    public byte[] RenderPreview(QuotationHeader q, CompanySettings company, QuotationSettings settings, string provisionalNumber,
        PrintStamp? printed = null) =>
        _renderer.Render(ToDocument(q, company, settings, watermark: "DRAFT", provisionalNumber, printed));

    public QuotationDocument ToDocument(QuotationHeader q, CompanySettings company, QuotationSettings settings, string? watermark,
        string? provisionalNumber = null, PrintStamp? printed = null)
    {
        var lines = q.Lines.OrderBy(l => l.LineNo).Select(l => new PdfLine(
            l.LineNo,
            l.ItemName,
            SplitLines(l.Description),
            l.Hsn,
            q.WithoutGst ? 0m : l.GstRate,
            l.DueOn,
            l.Quantity,
            l.Unit,
            l.Rate,
            l.DiscountPercent,
            l.Amount,
            l.PartNumber)).ToList();

        var ordered = q.Lines.OrderBy(l => l.LineNo).ToList();
        // The quotation's own GST presentation and round off (fixed when it was saved), so reprints match its totals.
        var input = QuotationService.CalcInputOf(q, company.StateCode, usePercent: false);
        var interState = input.InterState;
        var calc = QuotationCalculator.Calculate(input);

        // As in Tally: one line per tax ledger (SGST, CGST or IGST), then the round off ("Less :" when negative).
        // A "Without GST" quotation has no tax lines and says so on the packing & forwarding line.
        var summary = new List<PdfSummaryRow>();
        if (calc.PackingForwarding != 0)
            summary.Add(new PdfSummaryRow(q.WithoutGst ? "Packing & Forwarding - Without GST" : "Packing & Forwarding", "", calc.PackingForwarding));
        foreach (var name in new[] { "SGST", "CGST", "IGST" })
        {
            var amount = calc.Taxes.Where(t => t.Name == name).Sum(t => t.Amount);
            if (amount != 0) summary.Add(new PdfSummaryRow(name, "", amount));
        }
        if (calc.RoundOff != 0) summary.Add(new PdfSummaryRow("Round Off", "", calc.RoundOff, Less: calc.RoundOff < 0));
        var hsn = q.DocType == DocumentType.ProformaInvoice && calc.Taxes.Count > 0 ? HsnSummary(ordered, calc, interState) : [];

        return new QuotationDocument
        {
            Title = q.DocType == DocumentType.ProformaInvoice ? "PROFORMA INVOICE" : "QUOTATION",
            Kind = q.DocType,
            Printed = printed,
            DeliveryNote = q.DeliveryNote,
            DispatchDocNo = q.DispatchDocNo,
            DeliveryNoteDate = q.DeliveryNoteDate,
            HsnSummary = hsn,
            InterState = interState,
            TaxAmountInWords = hsn.Count == 0 ? "" : IndianFormat.AmountInWords(hsn.Sum(h => h.TotalTax), settings.CurrencyWordsPrefix),
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

    /// <summary>
    /// HSN/SAC-wise taxable value and tax, P&amp;F apportioned like the calculator does; the totals per GST rate are made
    /// to equal the calculator's tax lines exactly (the paise difference, if any, goes to the largest row of that rate).
    /// </summary>
    internal static List<PdfHsnRow> HsnSummary(List<QuotationLine> lines, CalcResult calc, bool interState)
    {
        var amounts = lines.Select(l => QuotationCalculator.LineAmount(l.Quantity, l.Rate, l.DiscountPercent)).ToList();
        var groups = lines.Select((l, i) => (Hsn: l.Hsn.Length > 0 ? l.Hsn : "—", Rate: l.GstRate ?? 0, Amount: amounts[i]))
            .GroupBy(x => (x.Hsn, x.Rate))
            .Select(g => (g.Key.Hsn, g.Key.Rate, Value: g.Sum(x => x.Amount)))
            .ToList();
        var rows = new List<PdfHsnRow>();
        foreach (var byRate in groups.GroupBy(g => g.Rate))
        {
            var taxLines = calc.Taxes.Where(t => t.RatePercent == (interState ? byRate.Key : byRate.Key / 2)).ToList();
            var rateTaxable = taxLines.Count > 0 ? taxLines[0].TaxableValue : byRate.Sum(g => g.Value);
            var rateValue = byRate.Sum(g => g.Value);
            var items = byRate.ToList();
            var start = rows.Count;
            var allocated = 0m;
            for (var i = 0; i < items.Count; i++)
            {
                var taxable = i == items.Count - 1 ? rateTaxable - allocated
                    : rateValue == 0 ? items[i].Value : QuotationCalculator.Round2(rateTaxable * items[i].Value / rateValue);
                allocated += taxable;
                var half = byRate.Key / 2;
                rows.Add(interState
                    ? new PdfHsnRow(items[i].Hsn, taxable, 0, 0, 0, 0, byRate.Key, QuotationCalculator.Round2(taxable * byRate.Key / 100))
                    : new PdfHsnRow(items[i].Hsn, taxable, half, QuotationCalculator.Round2(taxable * half / 100), half,
                        QuotationCalculator.Round2(taxable * half / 100), 0, 0));
            }
            if (byRate.Key <= 0 || rows.Count == start) continue;
            // Make the rows of this rate add up to the calculator's tax lines.
            var largest = rows.Skip(start).Select((r, i) => (r, i: i + start)).MaxBy(x => x.r.TaxableValue).i;
            var central = taxLines.Where(t => t.Name == "CGST").Sum(t => t.Amount) - rows.Skip(start).Sum(r => r.CentralAmount);
            var state = taxLines.Where(t => t.Name == "SGST").Sum(t => t.Amount) - rows.Skip(start).Sum(r => r.StateAmount);
            var integrated = taxLines.Where(t => t.Name == "IGST").Sum(t => t.Amount) - rows.Skip(start).Sum(r => r.IntegratedAmount);
            rows[largest] = rows[largest] with
            {
                CentralAmount = rows[largest].CentralAmount + central,
                StateAmount = rows[largest].StateAmount + state,
                IntegratedAmount = rows[largest].IntegratedAmount + integrated,
            };
        }
        return rows.Where(r => r.TaxableValue != 0 || r.TotalTax != 0).ToList();
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
