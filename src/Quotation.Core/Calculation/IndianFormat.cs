using System.Globalization;
using System.Text;

namespace Quotation.Core.Calculation;

/// <summary>Indian number formatting (1,23,45,678.00) and amount in words (lakh/crore).</summary>
public static class IndianFormat
{
    /// <summary>"1,23,456.00". Negative values get a leading minus.</summary>
    public static string Amount(decimal value, int decimals = 2)
    {
        var negative = value < 0;
        var abs = Math.Round(Math.Abs(value), decimals, MidpointRounding.AwayFromZero);
        var text = abs.ToString("F" + decimals, CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.');
        var integer = dot < 0 ? text : text[..dot];
        var fraction = dot < 0 ? "" : text[dot..];

        var sb = new StringBuilder();
        if (integer.Length <= 3)
        {
            sb.Append(integer);
        }
        else
        {
            var last3 = integer[^3..];
            var rest = integer[..^3];
            var groups = new List<string>();
            while (rest.Length > 2)
            {
                groups.Insert(0, rest[^2..]);
                rest = rest[..^2];
            }
            if (rest.Length > 0) groups.Insert(0, rest);
            sb.Append(string.Join(",", groups)).Append(',').Append(last3);
        }
        return (negative ? "-" : "") + sb + fraction;
    }

    /// <summary>Quantity without trailing zeros: 5 → "5", 2.50 → "2.5", 1.125 → "1.125".</summary>
    public static string Quantity(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static readonly string[] Ones =
    [
        "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen",
        "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen",
    ];

    private static readonly string[] Tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    /// <summary>
    /// Tally-style amount in words: "INR Eighteen Thousand Four Hundred Eight and Fifty paise Only".
    /// </summary>
    public static string AmountInWords(decimal amount, string currencyPrefix = "INR")
    {
        var abs = Math.Round(Math.Abs(amount), 2, MidpointRounding.AwayFromZero);
        var rupees = (long)Math.Floor(abs);
        var paise = (int)((abs - rupees) * 100);

        var words = rupees == 0 ? "Zero" : NumberToWords(rupees);
        var sb = new StringBuilder();
        if (amount < 0) sb.Append("Minus ");
        if (!string.IsNullOrWhiteSpace(currencyPrefix)) sb.Append(currencyPrefix.Trim()).Append(' ');
        sb.Append(words);
        if (paise > 0) sb.Append(" and ").Append(NumberToWords(paise)).Append(" paise");
        sb.Append(" Only");
        return sb.ToString();
    }

    /// <summary>Indian system: crore (10^7), lakh (10^5), thousand, hundred.</summary>
    public static string NumberToWords(long n)
    {
        if (n == 0) return "Zero";
        var parts = new List<string>();
        var crore = n / 10_000_000;
        n %= 10_000_000;
        if (crore > 0) parts.Add(NumberToWords(crore) + " Crore"); // supports amounts above 99 crore
        var lakh = n / 100_000;
        n %= 100_000;
        if (lakh > 0) parts.Add(TwoDigits((int)lakh) + " Lakh");
        var thousand = n / 1000;
        n %= 1000;
        if (thousand > 0) parts.Add(TwoDigits((int)thousand) + " Thousand");
        var hundred = n / 100;
        n %= 100;
        if (hundred > 0) parts.Add(Ones[hundred] + " Hundred");
        if (n > 0) parts.Add(TwoDigits((int)n));
        return string.Join(" ", parts);
    }

    private static string TwoDigits(int n) =>
        n < 20 ? Ones[n] : Tens[n / 10] + (n % 10 > 0 ? " " + Ones[n % 10] : "");
}
