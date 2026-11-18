using System.Text;

namespace Quotation.Contracts;

/// <summary>
/// The Quotation Server's finder: a phone or a Mac asks the office network "is the PC with this name here?" (a UDP
/// broadcast to the server's port, 5080) and the server PC with that name answers, so they can connect by the PC name.
/// </summary>
public static class FinderMessages
{
    public const string FindWord = "TSQUOTATION?FIND";
    public const string HereWord = "TSQUOTATION!HERE";

    /// <summary>"tsserver.local" → "tsserver".</summary>
    public static string BareName(string host)
    {
        var h = host.Trim().TrimEnd('.');
        return h.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ? h[..^6] : h;
    }

    public static byte[] FindRequest(string name) => Encoding.ASCII.GetBytes(FindWord + "\n" + BareName(name));

    /// <summary>The server's answer: "TSQUOTATION!HERE\n&lt;PC name&gt;\n&lt;port&gt;".</summary>
    public static byte[] HereReply(string pcName, int port) => Encoding.ASCII.GetBytes(HereWord + "\n" + pcName + "\n" + port);

    /// <summary>The question asked of the server: true with the name asked for ("" = any Quotation Server).</summary>
    public static bool TryReadFind(byte[] data, out string name)
    {
        name = "";
        if (data.Length is < 16 or > 200) return false;
        var text = Encoding.ASCII.GetString(data).Split('\n');
        if (text[0] != FindWord) return false;
        name = text.Length > 1 ? text[1].Trim() : "";
        return true;
    }

    /// <summary>A server's answer for this name.</summary>
    public static bool TryReadHere(byte[] data, string name, out int port)
    {
        port = 0;
        if (data.Length is < 16 or > 200) return false;
        var text = Encoding.ASCII.GetString(data).Split('\n');
        return text.Length >= 3 && text[0] == HereWord && text[1].Trim().Equals(BareName(name), StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(text[2], out port);
    }
}
