namespace Quotation.Core.Gst;

/// <summary>GSTIN format and checksum validation (15 characters, mod-36 check digit).</summary>
public static class Gstin
{
    private const string Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static bool IsWellFormed(string? gstin)
    {
        if (gstin is null || gstin.Length != 15) return false;
        var g = gstin.ToUpperInvariant();
        if (!char.IsDigit(g[0]) || !char.IsDigit(g[1])) return false;
        for (var i = 2; i < 7; i++) if (g[i] is < 'A' or > 'Z') return false;
        for (var i = 7; i < 11; i++) if (!char.IsDigit(g[i])) return false;
        if (g[11] is < 'A' or > 'Z') return false;
        return g.All(c => Chars.Contains(c));
    }

    /// <summary>True when the format is valid and the 15th character matches the checksum.</summary>
    public static bool IsValid(string? gstin) =>
        IsWellFormed(gstin) && ComputeCheckDigit(gstin![..14].ToUpperInvariant()) == char.ToUpperInvariant(gstin[14]);

    public static char ComputeCheckDigit(string first14)
    {
        var sum = 0;
        for (var i = 0; i < 14; i++)
        {
            var value = Chars.IndexOf(first14[i]);
            var product = value * (i % 2 == 0 ? 1 : 2);
            sum += product / 36 + product % 36;
        }
        return Chars[(36 - sum % 36) % 36];
    }

    public static string Pan(string gstin) => IsWellFormed(gstin) ? gstin.Substring(2, 10).ToUpperInvariant() : "";
}
