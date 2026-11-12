using System.Globalization;

namespace Quotation.Core.Domain;

/// <summary>The upi://pay link inside a "Pay by UPI" QR code (also used for the Test QR in Settings).</summary>
public static class UpiLink
{
    /// <summary>Notes longer than this are cut: some banks shorten long payment remarks.</summary>
    public const int MaxNoteLength = 50;

    public static string Build(string upiId, string payeeName, decimal amount, string note)
    {
        static string E(string s) => Uri.EscapeDataString(s);
        var upi = upiId.Trim();
        var name = string.IsNullOrWhiteSpace(payeeName) ? upi : payeeName.Trim();
        // The UPI ID as typed (letters, digits, . _ - and one @; checked in Settings): some UPI apps do not accept "%40".
        var plain = upi.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-' or '@');
        var link = $"upi://pay?pa={(plain ? upi : E(upi))}&pn={E(name)}&cu=INR";
        if (amount > 0) link += "&am=" + amount.ToString("0.00", CultureInfo.InvariantCulture);
        var n = note.Trim();
        if (n.Length > MaxNoteLength) n = n[..MaxNoteLength].TrimEnd();
        if (n.Length > 0) link += "&tn=" + E(n);
        return link;
    }

    /// <summary>"TSQ2627-142", "PO/4471" or "TSQ2627-142 PO/4471"; the quotation number when there is no order number.</summary>
    public static string Note(UpiNoteKind kind, string? quotationNumber, string? orderNumber)
    {
        var q = (quotationNumber ?? "").Trim();
        var po = (orderNumber ?? "").Trim();
        if (po.Length == 0) return q;
        return kind switch
        {
            UpiNoteKind.OrderNumber => po,
            UpiNoteKind.Both when q.Length > 0 => $"{q} {po}",
            UpiNoteKind.Both => po,
            _ => q.Length > 0 ? q : po,
        };
    }
}
