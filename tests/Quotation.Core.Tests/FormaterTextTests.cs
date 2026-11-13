using Quotation.Core.Formater;

namespace Quotation.Core.Tests;

public class FormaterTextTests
{
    [Fact]
    public void Reads_the_header_items_descriptions_and_order_number()
    {
        var p = FormaterText.Parse("""
            Company: BuyersHub
            Address: Plot 27 , PRT Colony , Karmanghat
            Address: Hyderabad Telangana 500079
            GSTIN: 36AAZFB2304B1ZQ
            Phone: +919182706754
            Email: sales@buyershub.org.in
            Order No: PO/4471 dt 28/10/2026
            Other Ref: R
            Ref: 100186

            Vernier Hook Depth Gauge, 10%, 5, 12,011
              Make - Insize
              Model : 1234-2001
            Digital Caliper 150mm, 0%, 2, 3450, Pcs, Make - Mitutoyo | 500-196
            Terms: 100% advance
            T&C: Packing extra
            GST: 12%
            """);
        Assert.True(p.IsValid);
        Assert.Equal("BuyersHub", p.BuyerName);
        Assert.Equal(2, p.Address.Count);
        Assert.Equal("36AAZFB2304B1ZQ", p.Gstin);
        Assert.Equal("36", p.StateCode);
        Assert.Equal(("PO/4471", new DateOnly(2026, 10, 28)), (p.OrderNo, p.OrderDate!.Value));
        Assert.Equal("R", p.OtherRef);
        Assert.Contains(p.Warnings, w => w.Text.Contains("given by the app")); // Ref lines are not used
        Assert.Equal(2, p.Items.Count);
        Assert.Equal((10m, 5m, 12011m, "Nos"), (p.Items[0].DiscountPercent, p.Items[0].Qty, p.Items[0].Rate, p.Items[0].Unit));
        Assert.Equal(["Make - Insize", "Model : 1234-2001"], p.Items[0].Desc);
        Assert.Equal("Pcs", p.Items[1].Unit);
        Assert.Equal(["Make - Mitutoyo", "500-196"], p.Items[1].Desc);
        Assert.Equal("100% advance", p.PaymentTerms);
        Assert.Equal(["Packing extra"], p.TermsAndConditions);
        Assert.Equal(12m, p.GstPercent);
    }

    [Fact]
    public void State_from_the_address_when_there_is_no_gstin_and_errors_for_missing_parts()
    {
        Assert.Equal("29", FormaterText.Parse("Company: X\nAddress: Bangalore, Karnataka 560002\nA, 0, 1, 10").StateCode);
        var empty = FormaterText.Parse("hello");
        Assert.False(empty.IsValid);
        Assert.Contains(empty.Errors, e => e.Text.StartsWith("No items found"));
        var broken = FormaterText.Parse("Company: X\nGauge, 10%, five, 12011");
        Assert.Contains(broken.Errors, e => e.Text.StartsWith("Could not read this item"));
        Assert.Equal(FormaterGst.None, FormaterText.Parse("Company: X\nGST: None\nA, 0, 1, 10").GstMode);
    }

    [Fact]
    public void Without_labels_the_first_line_is_the_customer_and_the_second_the_other_reference()
    {
        var p = FormaterText.Parse("Sonepar India\nYour enquiry dated 10-Nov-2026\nChakan, Pune\n27AAACS1234F1Z3\n\n187-901-10, 0%, 2, 15600");
        Assert.Equal("Sonepar India", p.BuyerName);
        Assert.Equal("Your enquiry dated 10-Nov-2026", p.OtherRef);
        Assert.Equal(["Chakan, Pune"], p.Address);
        Assert.Equal("27AAACS1234F1Z3", p.Gstin);
        // A GSTIN (or phone, e-mail) right after the customer is not the other reference.
        var q = FormaterText.Parse("BuyersHub\n36AAZFB2304B1ZQ\nA, 0, 1, 10");
        Assert.Equal("", q.OtherRef);
        Assert.Equal("36AAZFB2304B1ZQ", q.Gstin);
    }
}
