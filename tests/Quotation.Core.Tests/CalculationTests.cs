using Quotation.Core.Calculation;
using Quotation.Core.Domain;
using Quotation.Core.Validation;

namespace Quotation.Core.Tests;

public class CalculationTests
{
    [Theory]
    [InlineData(2, 15600, 0, 31200)]
    [InlineData(5, 3250, 10, 14625)]
    [InlineData(3, 33.33, 0, 99.99)]
    [InlineData(1, 0.125, 0, 0.13)]        // midpoint rounds away from zero
    [InlineData(7, 118, 12.5, 722.75)]
    [InlineData(2.5, 76, 0, 190)]           // fractional quantity (metres)
    public void Line_amounts(decimal qty, decimal rate, decimal disc, decimal expected)
    {
        Assert.Equal(expected, QuotationCalculator.LineAmount(qty, rate, disc));
    }

    [Fact]
    public void Rate_only_totals_like_tally_quotation()
    {
        var r = QuotationCalculator.Calculate(new CalcInput(
            [new(2, 15600, 0, 18, "NOS"), new(1, 5400, 5, 18, "NOS")],
            PackingForwardingAmount: 500));
        Assert.Equal([31200m, 5130m], r.LineAmounts);
        Assert.Equal(36330m, r.Subtotal);
        Assert.Equal(500m, r.PackingForwarding);
        Assert.Empty(r.Taxes);
        Assert.Equal(36830m, r.GrandTotal);
        Assert.Equal(3m, r.TotalQuantity);
        Assert.Equal("NOS", r.TotalQuantityUnit);
    }

    [Fact]
    public void Packing_forwarding_percent()
    {
        var r = QuotationCalculator.Calculate(new CalcInput([new(1, 1234.56m, 0, 18)], PackingForwardingPercent: 2));
        Assert.Equal(24.69m, r.PackingForwarding);
        Assert.Equal(1259.25m, r.GrandTotal);
    }

    [Fact]
    public void Intra_state_gst_split_into_cgst_and_sgst()
    {
        var r = QuotationCalculator.Calculate(new CalcInput([new(2, 15600, 0, 18)], 0, null, TaxPresentation.ComputeTax, InterState: false));
        Assert.Equal(2, r.Taxes.Count);
        Assert.Equal(("CGST", 9m, 2808m), (r.Taxes[0].Name, r.Taxes[0].RatePercent, r.Taxes[0].Amount));
        Assert.Equal(("SGST", 9m, 2808m), (r.Taxes[1].Name, r.Taxes[1].RatePercent, r.Taxes[1].Amount));
        Assert.Equal(36816m, r.GrandTotal);
    }

    [Fact]
    public void Inter_state_gst_is_igst_and_pf_is_apportioned_across_rates()
    {
        var r = QuotationCalculator.Calculate(new CalcInput(
            [new(1, 1000, 0, 18), new(1, 3000, 0, 12)], PackingForwardingAmount: 100, TaxPresentation: TaxPresentation.ComputeTax, InterState: true));
        var igst12 = r.Taxes.Single(t => t.RatePercent == 12);
        var igst18 = r.Taxes.Single(t => t.RatePercent == 18);
        Assert.Equal(3075m, igst12.TaxableValue); // 3000 + 75% of 100
        Assert.Equal(1025m, igst18.TaxableValue); // 1000 + 25% of 100
        Assert.Equal(369m, igst12.Amount);
        Assert.Equal(184.5m, igst18.Amount);
        Assert.Equal(4100m + 553.5m, r.GrandTotal);
    }

    [Fact]
    public void Round_off()
    {
        var r = QuotationCalculator.Calculate(new CalcInput([new(3, 33.33m, 0, 18)], RoundOff: true));
        Assert.Equal(99.99m, r.Subtotal);
        Assert.Equal(100m, r.GrandTotal);
        Assert.Equal(0.01m, r.RoundOff);
    }

    [Fact]
    public void Mixed_units_have_no_total_unit()
    {
        var r = QuotationCalculator.Calculate(new CalcInput([new(1, 10, 0, 18, "NOS"), new(10, 5, 0, 18, "MTR")]));
        Assert.Equal("", r.TotalQuantityUnit);
    }

    [Theory]
    [InlineData(0, "0.00")]
    [InlineData(999, "999.00")]
    [InlineData(1000, "1,000.00")]
    [InlineData(123456.5, "1,23,456.50")]
    [InlineData(12345678.9, "1,23,45,678.90")]
    [InlineData(-15600, "-15,600.00")]
    public void Indian_number_format(decimal value, string expected) => Assert.Equal(expected, IndianFormat.Amount(value));

    [Theory]
    [InlineData(15600, "INR Fifteen Thousand Six Hundred Only")]
    [InlineData(18408.50, "INR Eighteen Thousand Four Hundred Eight and Fifty paise Only")]
    [InlineData(100000, "INR One Lakh Only")]
    [InlineData(1234567.89, "INR Twelve Lakh Thirty Four Thousand Five Hundred Sixty Seven and Eighty Nine paise Only")]
    [InlineData(10000000, "INR One Crore Only")]
    [InlineData(2500000000, "INR Two Hundred Fifty Crore Only")]
    [InlineData(0.75, "INR Zero and Seventy Five paise Only")]
    [InlineData(101, "INR One Hundred One Only")]
    [InlineData(36830, "INR Thirty Six Thousand Eight Hundred Thirty Only")]
    public void Amount_in_words(decimal amount, string expected) => Assert.Equal(expected, IndianFormat.AmountInWords(amount));

    [Theory]
    [InlineData(5, "5")]
    [InlineData(2.5, "2.5")]
    [InlineData(1.125, "1.125")]
    public void Quantity_format(decimal q, string expected) => Assert.Equal(expected, IndianFormat.Quantity(q));

    private static ValidationInput ValidQuote(params ValidationLine[] lines) => new(
        1, "SONEPAR INDIA PRIVATE LIMITED", new DateOnly(2026, 9, 30), new FinancialYear(2026),
        lines.Length > 0 ? lines : [new(1, 10, "BEVEL PROTRACTOR", "90172020", 18, 2, "NOS", 15600, 0)], 0, true);

    [Fact]
    public void Valid_quotation_has_no_issues() => Assert.Empty(QuotationValidator.Validate(ValidQuote()));

    [Fact]
    public void Validation_reports_every_problem()
    {
        var q = ValidQuote(new ValidationLine(1, null, "", "", null, 0, "", 0, 120)) with
        {
            CustomerId = null,
            BuyerName = "",
            Date = new DateOnly(2027, 4, 2),
            PackingForwarding = -1,
        };
        var fields = QuotationValidator.Validate(q).Select(i => i.Field).ToHashSet();
        Assert.Superset(new HashSet<string> { "Customer", "Buyer", "Date", "PackingForwarding", "Product", "Quantity", "Rate", "Discount", "GST", "HSN", "Unit" }, fields);
        Assert.Equal(11, fields.Count);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(-5)]
    public void Invalid_gst_rates_rejected(decimal rate)
    {
        var issues = QuotationValidator.Validate(ValidQuote(new ValidationLine(1, 10, "X", "90172020", rate, 1, "NOS", 10, 0)));
        Assert.Contains(issues, i => i.Field == "GST");
    }

    [Theory]
    [InlineData("9017", true)]
    [InlineData("901720", true)]
    [InlineData("90172020", true)]
    [InlineData("9017202", false)]
    [InlineData("90A7", false)]
    public void Hsn_format(string hsn, bool ok) => Assert.Equal(ok, QuotationValidator.IsValidHsn(hsn));

    [Fact]
    public void Financial_year_boundary_dates()
    {
        Assert.Empty(QuotationValidator.Validate(ValidQuote() with { Date = new DateOnly(2027, 3, 31) }));
        Assert.Contains(QuotationValidator.Validate(ValidQuote() with { Date = new DateOnly(2026, 3, 31) }), i => i.Field == "Date");
    }
}
