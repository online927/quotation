using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Quotation.Core.Calculation;

namespace Quotation.Desktop.Controls;

/// <summary>
/// Sums in number boxes (Quantity, Rate, Disc. %, P&amp;F …): typing "=12*2" or "=3159-5%" shows the answer in a green chip
/// under the box ("= 24 · Enter"), and Enter or leaving the box puts the answer in. Switched on for every number box by
/// a style (App.axaml).
/// </summary>
public static class MathBox
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<NumericUpDown, bool>("IsEnabled", typeof(MathBox));

    public static bool GetIsEnabled(NumericUpDown box) => box.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(NumericUpDown box, bool value) => box.SetValue(IsEnabledProperty, value);

    static MathBox()
    {
        IsEnabledProperty.Changed.AddClassHandler<NumericUpDown>((box, e) =>
        {
            if (e.NewValue is true) Attach(box);
        });
    }

    private static readonly AttachedProperty<Popup?> ChipProperty =
        AvaloniaProperty.RegisterAttached<NumericUpDown, Popup?>("Chip", typeof(MathBox));

    private static void Attach(NumericUpDown box)
    {
        if (box.TextConverter is SumConverter) return;
        box.TextConverter = new SumConverter(box);
        box.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<TextBox>("PART_TextBox") is not { } text) return;
            text.TextChanged += (_, _) => ShowAnswer(box, text.Text);
            text.LostFocus += (_, _) => Hide(box);
        };
    }

    /// <summary>The answer shown under the box while a sum is typed ("" when there is none).</summary>
    public static string AnswerText(string? text, string? format = null) =>
        BoxMaths.TryEvaluate(text, out var value)
            ? "= " + value.ToString(string.IsNullOrEmpty(format) ? "#,##0.####" : "#,##" + format, CultureInfo.InvariantCulture) + " · Enter"
            : "";

    private static void ShowAnswer(NumericUpDown box, string? text)
    {
        var answer = AnswerText(text, box.FormatString);
        if (answer.Length == 0)
        {
            Hide(box);
            return;
        }
        var chip = box.GetValue(ChipProperty);
        if (chip is null)
        {
            chip = new Popup
            {
                PlacementTarget = box,
                Placement = PlacementMode.Bottom,
                VerticalOffset = 3,
                IsLightDismissEnabled = false,
                Child = new Border
                {
                    Name = "MathChip",
                    Background = Brush(box, "PillOkBrush", "#E3F6EA"),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(7, 2),
                    Child = new TextBlock
                    {
                        FontSize = 11.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Brush(box, "GreenBrush", "#0F7A3D"),
                    },
                },
            };
            box.SetValue(ChipProperty, chip);
        }
        ((TextBlock)((Border)chip.Child!).Child!).Text = answer;
        chip.IsOpen = true;
    }

    private static IBrush Brush(Control near, string key, string fallback) =>
        near.TryFindResource(key, near.ActualThemeVariant, out var b) && b is IBrush brush ? brush : new SolidColorBrush(Color.Parse(fallback));

    /// <summary>The chip under the box (tests).</summary>
    public static Popup? Chip(NumericUpDown box) => box.GetValue(ChipProperty);

    private static void Hide(NumericUpDown box)
    {
        if (box.GetValue(ChipProperty) is { } chip) chip.IsOpen = false;
    }

    /// <summary>
    /// The box's text ↔ number (NumericUpDown calls Convert for text → number and ConvertBack for number → text): its own
    /// number format both ways, and "=…" is worked out when the box takes the text.
    /// </summary>
    private sealed class SumConverter(NumericUpDown box) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var text = value as string;
            if (string.IsNullOrWhiteSpace(text)) return null;
            // (called on every key: "=12*" keeps the value so far; leaving the box hides the chip)
            if (BoxMaths.IsSum(text)) return BoxMaths.TryEvaluate(text, out var sum) ? sum : box.Value;
            var format = box.NumberFormat ?? culture.NumberFormat;
            return decimal.TryParse(text, NumberStyles.Any, format, out var d) ? d : box.Value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is decimal d ? d.ToString(box.FormatString, (IFormatProvider?)box.NumberFormat ?? culture) : "";
    }
}
