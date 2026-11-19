using System.Globalization;
using System.Text.RegularExpressions;

namespace Quotation.Core.Formater;

/// <summary>
/// An item spoken into the phone, written as a message-box line "name, qty, rate": "two bevel protractors at fifteen thousand
/// six hundred" → "Bevel protractor, 2, 15600"; "vernier caliper 300 mm quantity 2 rate 3159" → "Vernier caliper 300 mm, 2,
/// 3159"; "5 digital calipers" → "Digital caliper, 5". Numbers may be spoken in words (with lakh) or come as digits.
/// </summary>
public static partial class SpokenItem
{
    private static readonly Dictionary<string, long> Small = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
        ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14,
        ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19, ["twenty"] = 20,
        ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
        ["a"] = 1, ["an"] = 1, ["single"] = 1, ["dozen"] = 12,
    };

    private static readonly Dictionary<string, long> Scale = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hundred"] = 100, ["thousand"] = 1000, ["lakh"] = 100_000, ["lakhs"] = 100_000, ["lac"] = 100_000, ["lacs"] = 100_000,
        ["crore"] = 10_000_000, ["k"] = 1000,
    };

    private static readonly HashSet<string> RateWords = new(StringComparer.OrdinalIgnoreCase)
        { "at", "@", "for", "rate", "price", "rs", "rs.", "rupees", "rupee", "₹", "inr", "each", "costing" };

    private static readonly HashSet<string> QtyWords = new(StringComparer.OrdinalIgnoreCase)
        { "quantity", "qty", "nos", "nos.", "no", "no.", "numbers", "number", "pieces", "piece", "pcs", "pc", "units", "unit", "sets", "set", "of" };

    /// <summary>"Bevel protractor, 2, 15600", or null when nothing like an item was said.</summary>
    public static string? ToLine(string? spoken)
    {
        if (string.IsNullOrWhiteSpace(spoken)) return null;
        var tokens = Tokens(spoken);
        if (tokens.Count == 0) return null;

        // Numbers (in words or digits) with where they stand
        var parts = new List<(bool IsNumber, decimal Value, string Text, bool AfterRateWord, bool AfterQtyWord)>();
        var afterRate = false;
        var afterQty = false;
        for (var i = 0; i < tokens.Count;)
        {
            if (TryNumber(tokens, ref i, out var value, out var text))
            {
                parts.Add((true, value, text, afterRate, afterQty));
                afterRate = afterQty = false;
                continue;
            }
            var word = tokens[i++];
            if (RateWords.Contains(word)) { afterRate = true; parts.Add((false, 0, word, false, false)); continue; }
            if (QtyWords.Contains(word)) { afterQty = true; parts.Add((false, 0, word, false, false)); continue; }
            parts.Add((false, 0, word, false, false));
        }

        int? qtyAt = null, rateAt = null;
        for (var i = 0; i < parts.Count; i++)
        {
            if (!parts[i].IsNumber) continue;
            if (parts[i].AfterRateWord && rateAt is null) rateAt = i;
            else if (parts[i].AfterQtyWord && qtyAt is null) qtyAt = i;
        }
        // "2 bevel protractors …": a number at the start is the quantity
        if (qtyAt is null && parts[0].IsNumber && rateAt != 0) qtyAt = 0;
        // "… protractor 2 nos …": a whole number followed by nos / pieces
        for (var i = 0; qtyAt is null && i < parts.Count - 1; i++)
            if (parts[i].IsNumber && i != rateAt && IsQtyUnit(parts[i + 1].Text)) qtyAt = i;
        // "vernier caliper 2 3159": the last two numbers are the quantity and the rate
        var numbers = Enumerable.Range(0, parts.Count).Where(i => parts[i].IsNumber && i != qtyAt && i != rateAt).ToList();
        if (rateAt is null && qtyAt is not null && numbers.Count > 0 && numbers[^1] == parts.Count - 1 && numbers[^1] > qtyAt) rateAt = numbers[^1];
        if (qtyAt is null && rateAt is null && numbers.Count >= 2 && numbers[^1] == parts.Count - 1 && numbers[^2] == parts.Count - 2)
        {
            qtyAt = numbers[^2];
            rateAt = numbers[^1];
        }
        if (qtyAt is null && numbers.Count >= 1 && numbers[^1] == parts.Count - 1 && rateAt is not null && numbers[^1] < rateAt) qtyAt = numbers[^1];

        // The name: everything that is not the quantity, the rate or their words
        var name = new List<string>();
        for (var i = 0; i < parts.Count; i++)
        {
            if (i == qtyAt || i == rateAt) continue;
            var t = parts[i].Text;
            var nextIsPicked = i + 1 < parts.Count && (i + 1 == qtyAt || i + 1 == rateAt);
            if ((RateWords.Contains(t) || QtyWords.Contains(t)) && (nextIsPicked || i > (rateAt ?? -1) && i > (qtyAt ?? -1) && rateAt is not null && i < parts.Count && IsTrailing(parts, i)))
                continue;
            if (i - 1 >= 0 && (i - 1 == qtyAt) && IsQtyUnit(t)) continue; // "2 nos"
            if (i - 1 >= 0 && i - 1 == rateAt && t.Equals("each", StringComparison.OrdinalIgnoreCase)) continue;
            if (i - 1 >= 0 && i - 1 == rateAt && t.StartsWith("rupee", StringComparison.OrdinalIgnoreCase)) continue;
            name.Add(t);
        }
        while (name.Count > 0 && (RateWords.Contains(name[^1]) || QtyWords.Contains(name[^1]) || name[^1] is "and" or "please")) name.RemoveAt(name.Count - 1);
        while (name.Count > 0 && name[0] is "and" or "please" or "add" or "item" or "next") name.RemoveAt(0);
        if (name.Count == 0) return null;

        var qty = qtyAt is { } q ? parts[q].Value : 1m;
        if (qty > 1 && name.Count > 0 && !name.Contains("of", StringComparer.OrdinalIgnoreCase)) name[^1] = Singular(name[^1]); // "4 boxes of drill bits" stays
        var itemName = string.Join(' ', name);
        itemName = char.ToUpper(itemName[0], CultureInfo.InvariantCulture) + itemName[1..];
        var line = itemName + ", " + Plain(qty);
        if (rateAt is { } r) line += ", " + Plain(parts[r].Value);
        return line;
    }

    private static bool IsTrailing(List<(bool IsNumber, decimal Value, string Text, bool AfterRateWord, bool AfterQtyWord)> parts, int i) =>
        parts.Skip(i + 1).All(p => p.IsNumber || RateWords.Contains(p.Text) || QtyWords.Contains(p.Text));

    private static bool IsQtyUnit(string word) => word.ToLowerInvariant() is "nos" or "nos." or "no" or "no." or "numbers" or "pieces" or "piece"
        or "pcs" or "pc" or "units" or "unit" or "sets" or "set";

    private static string Plain(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>"protractors" → "protractor", "boxes" → "box"; "brass", "gauss", "cutters" handled.</summary>
    private static string Singular(string word)
    {
        if (word.Length < 4 || !word.EndsWith('s') || word.EndsWith("ss", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("us", StringComparison.OrdinalIgnoreCase) || word.EndsWith("is", StringComparison.OrdinalIgnoreCase) ||
            word.Any(char.IsDigit)) return word;
        if (word.EndsWith("xes", StringComparison.OrdinalIgnoreCase) || word.EndsWith("ches", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("shes", StringComparison.OrdinalIgnoreCase)) return word[..^2];
        return word[..^1];
    }

    private static List<string> Tokens(string spoken)
    {
        var s = spoken.Replace("₹", " ₹ ").Replace("@", " @ ").Replace("/-", " ");
        s = MoneyComma().Replace(s, m => m.Value.Replace(",", ""));
        return s.Split([' ', '\t', '\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim('.', '!', '?', ';', ':', '"', '“', '”')).Where(t => t.Length > 0).ToList();
    }

    [GeneratedRegex(@"\d{1,3}(,\d{2,3})+(\.\d+)?")]
    private static partial Regex MoneyComma();

    /// <summary>A number at <paramref name="i"/>: "15600", "15.5", "15k", "fifteen thousand six hundred", "2 lakh".</summary>
    private static bool TryNumber(List<string> tokens, ref int i, out decimal value, out string text)
    {
        value = 0;
        text = "";
        var start = i;
        var t = tokens[i];
        if (decimal.TryParse(t.TrimStart('₹').Replace("rs", "", StringComparison.OrdinalIgnoreCase), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var d) && t.Any(char.IsDigit))
        {
            i++;
            // "15 thousand", "2 lakh", "1.5 lakh"
            if (i < tokens.Count && Scale.TryGetValue(tokens[i], out var scale))
            {
                d *= scale;
                i++;
                // "15 thousand 600"
                if (i < tokens.Count && decimal.TryParse(tokens[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rest) && rest < scale)
                {
                    d += rest;
                    i++;
                }
            }
            value = d;
            text = string.Join(' ', tokens.Skip(start).Take(i - start));
            return true;
        }
        if (t.EndsWith('k') && decimal.TryParse(t[..^1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var kilo))
        {
            i++;
            value = kilo * 1000;
            text = t;
            return true;
        }

        // In words
        long total = 0, current = 0;
        var any = false;
        while (i < tokens.Count)
        {
            var w = tokens[i];
            if (Small.TryGetValue(w, out var small) && !(w is "a" or "an" && (i + 1 >= tokens.Count || !Scale.ContainsKey(tokens[i + 1]) && !tokens[i + 1].Equals("dozen", StringComparison.OrdinalIgnoreCase))))
            {
                if (w.Equals("dozen", StringComparison.OrdinalIgnoreCase)) current = Math.Max(1, current) * 12;
                else current += small;
                any = true;
                i++;
            }
            else if (any && Scale.TryGetValue(w, out var scale) && scale != 1000 || any && w.Equals("thousand", StringComparison.OrdinalIgnoreCase))
            {
                var sc = Scale[w];
                if (sc == 100) current = Math.Max(1, current) * 100;
                else
                {
                    total += Math.Max(1, current) * sc;
                    current = 0;
                }
                i++;
            }
            else if (any && w.Equals("and", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count && Small.ContainsKey(tokens[i + 1]) &&
                     !(tokens[i + 1] is "a" or "an"))
            {
                i++;
            }
            else break;
        }
        if (!any)
        {
            i = start;
            return false;
        }
        value = total + current;
        text = string.Join(' ', tokens.Skip(start).Take(i - start));
        return true;
    }
}
