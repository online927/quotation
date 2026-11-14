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
    public void Blank_lines_split_the_customer_the_other_reference_and_the_items()
    {
        var p = FormaterText.Parse("""
            Sonepar India
            Plot 12, MIDC, Chakan, Pune
            27AAACS1234F1Z3

            Your enquiry dated 10-Nov-2026

            187-901-10, 2, 15600
            Make - Mitutoyo

            530-104, 5, 3250, 5

            Digital Caliper 150mm Special, 2, 3450
            Make - Insize
            Range 0-150, 0.01mm
            """);
        Assert.True(p.IsValid, string.Join("; ", p.Errors));
        Assert.Equal("Sonepar India", p.BuyerName);
        Assert.Equal(["Plot 12, MIDC, Chakan, Pune"], p.Address);
        Assert.Equal("27AAACS1234F1Z3", p.Gstin);
        Assert.Equal("Your enquiry dated 10-Nov-2026", p.OtherRef);
        Assert.Equal(3, p.Items.Count);
        Assert.Equal(("187-901-10", 2m, 15600m, 0m), (p.Items[0].Name, p.Items[0].Qty, p.Items[0].Rate, p.Items[0].DiscountPercent));
        Assert.Equal(["Make - Mitutoyo"], p.Items[0].Desc);
        Assert.Equal((5m, 3250m, 5m), (p.Items[1].Qty, p.Items[1].Rate, p.Items[1].DiscountPercent));
        Assert.Equal(("Digital Caliper 150mm Special", 2m, 3450m), (p.Items[2].Name, p.Items[2].Qty, p.Items[2].Rate));
        Assert.Equal(["Make - Insize", "Range 0-150, 0.01mm"], p.Items[2].Desc);
    }

    [Fact]
    public void Item_lines_are_name_qty_rate_and_an_optional_discount()
    {
        var p = FormaterText.Parse("""
            X

            Enquiry 45

            Dial Gauge, 2

            530-104 5 3250 10%

            Caliper, 3, 12,011, 10%

            Gauge, 4, 1,200, 12.5

            Old order, 10%, 5, 12011

            Old order plain, 10, 5, 12011

            Digital Caliper, 150mm, 2, 3450, Pcs, Make - Mitutoyo | 500-196
            """);
        Assert.True(p.IsValid, string.Join("; ", p.Errors));
        Assert.Equal(7, p.Items.Count);
        Assert.Equal(("Dial Gauge", 2m, 0m), (p.Items[0].Name, p.Items[0].Qty, p.Items[0].Rate)); // the Tally rate
        Assert.Equal(("530-104", 5m, 3250m, 10m), (p.Items[1].Name, p.Items[1].Qty, p.Items[1].Rate, p.Items[1].DiscountPercent));
        Assert.Equal((3m, 12011m, 10m), (p.Items[2].Qty, p.Items[2].Rate, p.Items[2].DiscountPercent));
        Assert.Equal((4m, 1200m, 12.5m), (p.Items[3].Qty, p.Items[3].Rate, p.Items[3].DiscountPercent));
        Assert.Equal((5m, 12011m, 10m), (p.Items[4].Qty, p.Items[4].Rate, p.Items[4].DiscountPercent));
        Assert.Equal((5m, 12011m, 10m), (p.Items[5].Qty, p.Items[5].Rate, p.Items[5].DiscountPercent)); // 12011% cannot be a discount
        Assert.Equal(("Digital Caliper, 150mm", 2m, 3450m, "Pcs"), (p.Items[6].Name, p.Items[6].Qty, p.Items[6].Rate, p.Items[6].Unit));
        Assert.Equal(["Make - Mitutoyo", "500-196"], p.Items[6].Desc);
    }

    [Fact]
    public void Items_are_items_even_without_a_blank_line_or_an_other_reference()
    {
        var p = FormaterText.Parse("Sonepar India\nChakan, Pune\n187-901-10, 2, 15600\n530-104, 5, 3250");
        Assert.Equal(["Chakan, Pune"], p.Address);
        Assert.Equal(2, p.Items.Count);
        Assert.Equal("", p.OtherRef);

        // "-" lines in the customer part are its address lines; the "-" is dropped. After the blank line "-" is not an address.
        var d = FormaterText.Parse("Sonepar India\n- Plot 12, MIDC\n- Chakan, 2, 3\n* Pune, Maharashtra\n27AAACS1234F1Z3\n\nA, 1, 10\n- Make - Insize");
        Assert.Equal(["Plot 12, MIDC", "Chakan, 2, 3", "Pune, Maharashtra"], d.Address);
        Assert.Equal("27AAACS1234F1Z3", d.Gstin);
        Assert.Single(d.Items);
        Assert.Equal(["Make - Insize"], d.Items[0].Desc);

        var q = FormaterText.Parse("BuyersHub\n36AAZFB2304B1ZQ\n\nA, 1, 10\nB, 2, 20");
        Assert.Equal("", q.OtherRef);
        Assert.Equal("36AAZFB2304B1ZQ", q.Gstin);
        Assert.Equal(2, q.Items.Count);
        Assert.Empty(q.Address);

        // The lines under an item are its description; an item line that cannot be read is a problem.
        var r = FormaterText.Parse("X\n\nA, 1, 10\nRange 0-150mm, 0.01mm\n\nB, 2, 2O0");
        Assert.Equal(["Range 0-150mm, 0.01mm"], r.Items[0].Desc);
        Assert.Contains(r.Errors, e => e.Line == 6 && e.Text.StartsWith("Could not read this item"));

        // The first part after the customer is the items when it reads as an item ("name, qty": the Tally rate).
        var t = FormaterText.Parse("X\n\nDial Gauge, 2\nMake - Mitutoyo\n\nB, 2, 20");
        Assert.Equal("", t.OtherRef);
        Assert.Equal(2, t.Items.Count);
        Assert.Equal(["Make - Mitutoyo"], t.Items[0].Desc);
    }

    [Fact]
    public void Pasted_whatsapp_text_loses_times_names_and_greetings_but_keeps_the_quotation()
    {
        var clean = FormaterText.CleanPasted("""
            [11/11, 10:15 am] Ravi: Assalamu Alaikum
            [11/11, 10:15 am] Ravi: Good morning sir
            11/11/26, 10:16 am - Ravi: Sonepar India
            - Chakan, Pune


            530-104, 5, 3250
            Dear Sir,
            Please quote at the earliest
            Thanks & regards
            """);
        Assert.Equal("Sonepar India\n- Chakan, Pune\n\n530-104, 5, 3250", clean.Text);
        Assert.Equal(3, clean.Prefixes);
        Assert.Equal(5, clean.Lines);
        Assert.Equal("3 WhatsApp times and names and 5 greeting lines", clean.Describe());
        // A line with a number is never a greeting; nothing to clean: nothing removed.
        Assert.Equal(0, FormaterText.CleanPasted("Thanks for order 4471\n530-104, 5, 3250").Removed);
    }
}
