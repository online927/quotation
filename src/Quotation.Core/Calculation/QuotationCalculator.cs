using Quotation.Core.Domain;

namespace Quotation.Core.Calculation;

public sealed record CalcLine(decimal Quantity, decimal Rate, decimal DiscountPercent, decimal GstRate, string Unit = "");

public sealed record CalcInput(
    IReadOnlyList<CalcLine> Lines,
    decimal PackingForwardingAmount = 0,
    decimal? PackingForwardingPercent = null,
    TaxPresentation TaxPresentation = TaxPresentation.RateOnly,
    bool RoundOff = false,
    /// <summary>True when the buyer's place of supply is in another state (IGST instead of CGST+SGST).</summary>
    bool InterState = false);

public sealed record TaxLine(string Name, decimal RatePercent, decimal TaxableValue, decimal Amount);

public sealed record CalcResult(
    IReadOnlyList<decimal> LineAmounts,
    decimal Subtotal,
    decimal PackingForwarding,
    decimal TaxableValue,
    IReadOnlyList<TaxLine> Taxes,
    decimal TaxTotal,
    decimal RoundOff,
    decimal GrandTotal,
    decimal TotalQuantity,
    string TotalQuantityUnit);

/// <summary>
/// Deterministic quotation arithmetic (never delegated to AI). Amounts are rounded to paise with
/// MidpointRounding.AwayFromZero, as Tally does.
/// </summary>
public static class QuotationCalculator
{
    public static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Amount = Qty × Rate less discount %, rounded to paise.</summary>
    public static decimal LineAmount(decimal quantity, decimal rate, decimal discountPercent)
    {
        var gross = quantity * rate;
        var discount = gross * discountPercent / 100m;
        return Round2(gross - discount);
    }

    public static CalcResult Calculate(CalcInput input)
    {
        var amounts = input.Lines.Select(l => LineAmount(l.Quantity, l.Rate, l.DiscountPercent)).ToList();
        var subtotal = amounts.Sum();

        var pf = input.PackingForwardingPercent is { } pct && pct > 0
            ? Round2(subtotal * pct / 100m)
            : Round2(input.PackingForwardingAmount);
        var taxable = subtotal + pf;

        var taxes = new List<TaxLine>();
        if (input.TaxPresentation == TaxPresentation.ComputeTax && input.Lines.Count > 0)
        {
            // Packing & forwarding is part of the taxable value; it is apportioned to the GST rates
            // in proportion to the line values (the rate of the principal supply when there is one rate).
            var byRate = input.Lines.Select((l, i) => (l.GstRate, Amount: amounts[i]))
                .GroupBy(x => x.GstRate)
                .Select(g => (Rate: g.Key, Value: g.Sum(x => x.Amount)))
                .OrderBy(x => x.Rate)
                .ToList();
            var pfAllocated = 0m;
            for (var i = 0; i < byRate.Count; i++)
            {
                var share = i == byRate.Count - 1
                    ? pf - pfAllocated
                    : subtotal == 0 ? 0 : Round2(pf * byRate[i].Value / subtotal);
                pfAllocated += share;
                var value = byRate[i].Value + share;
                var rate = byRate[i].Rate;
                if (rate <= 0) continue;
                if (input.InterState)
                {
                    taxes.Add(new TaxLine("IGST", rate, value, Round2(value * rate / 100m)));
                }
                else
                {
                    var half = rate / 2m;
                    taxes.Add(new TaxLine("CGST", half, value, Round2(value * half / 100m)));
                    taxes.Add(new TaxLine("SGST", half, value, Round2(value * half / 100m)));
                }
            }
        }
        var taxTotal = taxes.Sum(t => t.Amount);
        var beforeRound = taxable + taxTotal;
        var grand = input.RoundOff ? Math.Round(beforeRound, 0, MidpointRounding.AwayFromZero) : beforeRound;

        var units = input.Lines.Select(l => l.Unit.Trim().ToUpperInvariant()).Where(u => u.Length > 0).Distinct().ToList();
        return new CalcResult(
            amounts,
            subtotal,
            pf,
            taxable,
            taxes,
            taxTotal,
            grand - beforeRound,
            grand,
            input.Lines.Sum(l => l.Quantity),
            units.Count == 1 ? units[0] : "");
    }
}
