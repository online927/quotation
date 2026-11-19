using System.Globalization;

namespace Quotation.Core.Calculation;

/// <summary>
/// Sums typed into a number box, starting with "=": "=12*2" → 24, "=3159-5%" → 3,001.05 (5 % off), "=1500+10%" → 1,650,
/// "=21978/1.18" → the rate without GST, "=200*10%" → 20. + − × ÷ (also x, * and /), brackets, commas in numbers.
/// </summary>
public static class BoxMaths
{
    /// <summary>A text that is a sum ("=…").</summary>
    public static bool IsSum(string? text) => text is not null && text.TrimStart().StartsWith('=');

    /// <summary>The answer of "=…", rounded to 4 places; false when it is not a sum or cannot be worked out.</summary>
    public static bool TryEvaluate(string? text, out decimal result)
    {
        result = 0;
        if (!IsSum(text)) return false;
        var s = text!.Trim()[1..];
        if (s.Length is 0 or > 200) return false;
        try
        {
            var p = new Parser(s);
            var (value, _) = p.Sum();
            if (!p.AtEnd) return false;
            result = Math.Round(value, 4, MidpointRounding.AwayFromZero);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or DivideByZeroException or OverflowException)
        {
            return false;
        }
    }

    private sealed class Parser(string text)
    {
        private int _at;

        public bool AtEnd
        {
            get
            {
                Skip();
                return _at >= text.Length;
            }
        }

        // sum := part (('+' | '-') part)* ; "x - 5%" takes 5 % off x, "x + 10%" adds 10 % of x
        public (decimal Value, bool IsPercent) Sum()
        {
            var (value, percent) = Part();
            while (true)
            {
                Skip();
                if (_at >= text.Length || (text[_at] != '+' && text[_at] != '-' && text[_at] != '−')) return (value, percent);
                var plus = text[_at++] == '+';
                var (right, rightPercent) = Part();
                var amount = rightPercent ? value * right : right;
                value = plus ? value + amount : value - amount;
                percent = false;
            }
        }

        // part := one (('*' | 'x' | '×' | '/' | '÷') one)*
        private (decimal Value, bool IsPercent) Part()
        {
            var (value, percent) = One();
            while (true)
            {
                Skip();
                if (_at >= text.Length) return (value, percent);
                var op = char.ToLowerInvariant(text[_at]);
                if (op is not ('*' or 'x' or '×' or '/' or '÷')) return (value, percent);
                _at++;
                var (right, _) = One();
                value = op is '/' or '÷' ? value / right : value * right;
                percent = false;
            }
        }

        // one := number ['%'] | '(' sum ')' | '-' one
        private (decimal Value, bool IsPercent) One()
        {
            Skip();
            if (_at >= text.Length) throw new FormatException();
            var c = text[_at];
            if (c is '-' or '−')
            {
                _at++;
                var (v, p) = One();
                return (-v, p);
            }
            if (c == '(')
            {
                _at++;
                var (v, _) = Sum();
                Skip();
                if (_at >= text.Length || text[_at] != ')') throw new FormatException();
                _at++;
                return Percent(v);
            }
            var start = _at;
            while (_at < text.Length && (char.IsDigit(text[_at]) || text[_at] is '.' or ',')) _at++;
            if (_at == start) throw new FormatException();
            var number = decimal.Parse(text[start.._at].Replace(",", ""), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            return Percent(number);
        }

        private (decimal, bool) Percent(decimal value)
        {
            Skip();
            if (_at < text.Length && text[_at] == '%')
            {
                _at++;
                return (value / 100m, true);
            }
            return (value, false);
        }

        private void Skip()
        {
            while (_at < text.Length && char.IsWhiteSpace(text[_at])) _at++;
        }
    }
}
