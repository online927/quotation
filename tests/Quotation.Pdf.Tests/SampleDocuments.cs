using Quotation.Core.Calculation;
using Quotation.Core.Domain;

namespace Quotation.Pdf.Tests;

public static class SampleDocuments
{
    public static CompanySettings Company() => new()
    {
        CompanyName = "T.SAIFUDDIN & CO.",
        AddressLines = ["No. 72, N.R.Road, Bangalore", "INDIA"],
        Mobile = "98450 00000",
        Gstin = "29AAAFT0000A1Z0",
        StateName = "Karnataka",
        StateCode = "29",
        Email = "sales@tsaifuddin.example",
        Pan = "AAAFT0000A",
        MsmeNumber = "UDYAM-KR-03-0000000",
        Iec = "0000000000",
        BankAccountHolder = "T.SAIFUDDIN & CO.",
        BankName = "Example Bank Ltd",
        BankAccountNumber = "000000000000",
        BankBranch = "N.R. Road, Bangalore",
        BankIfsc = "EXMP0000001",
        QuotationValidityDays = 30,
        TermsAndConditions = ["Prices are ex-godown Bangalore.", "GST extra as applicable.", "Delivery: 2-3 weeks from receipt of PO."],
    };

    public static PdfLine Bevel(int sl = 1, decimal qty = 2) => new(sl, "187-901-10-UNIVERSAL BEVEL PROTRACTOR",
        ["Brand: Mitutoyo", "Model: 187-901", "Manufacturer: Mitutoyo Corporation, Japan", "MOQ: 1 No."],
        "90172020", 18, "", qty, "NOS", 15600, 0, QuotationCalculator.LineAmount(qty, 15600, 0));

    public static QuotationDocument Build(IReadOnlyList<PdfLine> lines, decimal pf = 500, string? watermark = null,
        IReadOnlyList<string>? buyerAddress = null, string? remarks = null)
    {
        var subtotal = lines.Sum(l => l.Amount);
        var total = subtotal + pf;
        return new QuotationDocument
        {
            Company = Company(),
            Number = "TSQ2526-3247",
            Date = new DateOnly(2026, 9, 30),
            BuyersReference = "PO/4471",
            BuyersReferenceDate = new DateOnly(2026, 9, 28),
            DispatchedThrough = "By Road",
            Destination = "Pune",
            PaymentTerms = "30 days from date of invoice",
            OtherReferences = "Your enquiry dated 28-Sep-2026",
            TermsOfDelivery = "Ex-godown Bangalore. Freight extra.",
            Buyer = new PdfParty("Sonepar India Private Limited", buyerAddress ?? ["Plot No. 12, MIDC Industrial Area", "Chakan, Pune", "PIN: 410501"],
                "27AAACS1234F1Z3", "Maharashtra", "27", "Mr. Rahul Deshmukh", "9822012345", "purchase.pune@sonepar.example"),
            Consignee = new PdfParty("Sonepar India Private Limited", ["Plot No. 12, MIDC Industrial Area", "Chakan, Pune", "PIN: 410501"],
                "27AAACS1234F1Z3", "Maharashtra", "27"),
            Lines = lines,
            Subtotal = subtotal,
            SummaryRows = pf > 0 ? [new PdfSummaryRow("Packing & Forwarding", "", pf)] : [],
            GrandTotal = total,
            TotalQuantity = lines.Sum(l => l.Quantity),
            TotalQuantityUnit = lines.Select(l => l.Unit).Distinct().Count() == 1 ? lines[0].Unit : "",
            AmountInWords = IndianFormat.AmountInWords(total),
            Remarks = remarks ?? "",
            TermsAndConditions = Company().TermsAndConditions,
            PreparedBy = "Imran",
            VerifiedBy = "Saifuddin",
            Watermark = watermark,
        };
    }

    public static List<PdfLine> ManyLines(int count)
    {
        var list = new List<PdfLine>();
        for (var i = 1; i <= count; i++)
        {
            var desc = i % 3 == 0
                ? new[] { "Brand: Polycab", "Conductor: Annealed bare copper, Class 5 flexible", "Insulation: FR PVC, 1100V grade", "MOQ: 100 Mtrs" }
                : i % 3 == 1 ? new[] { "Brand: Mitutoyo" } : Array.Empty<string>();
            var qty = 1 + i % 7;
            var rate = 100m + i * 37.5m;
            list.Add(new PdfLine(i, $"{100 + i}-{200 + i}-ITEM NUMBER {i} WITH A FAIRLY LONG DESCRIPTIVE NAME FOR WRAPPING TESTS",
                desc, "85444999", 18, i % 5 == 0 ? "2 weeks" : "", qty, "NOS", rate, i % 4 == 0 ? 5 : 0,
                QuotationCalculator.LineAmount(qty, rate, i % 4 == 0 ? 5 : 0)));
        }
        return list;
    }
}
