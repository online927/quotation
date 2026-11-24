using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Quotation.Desktop.Services;

/// <summary>
/// Round 37, My Settings → "Item rows (this PC)": Bigger (the default) makes the item table of the quotation screen and the
/// Formater page larger: item text 12 → 14.5, small lines (Part No., brand, description, delivery) 11 → 13, table headings
/// 12 → 13 and taller rows. Normal = the look before. Only the item table changes.
/// </summary>
public static class ItemRowsLook
{
    public const double ItemText = 14.5, SmallText = 13, HeadingText = 13;
    public static readonly Thickness RowPadding = new(12, 11);

    /// <summary>Applies the look to a page (its <paramref name="lines"/> list and the headings above it).</summary>
    public static void Apply(UserControl page, Control lines, bool bigger)
    {
        if (!bigger) return;
        lines.Resources["FontSmall"] = SmallText;
        lines.Resources["RowPadding"] = RowPadding;
        lines.Styles.Add(TextStyle("td", ItemText));
        page.Styles.Add(TextStyle("th", HeadingText));
    }

    private static Style TextStyle(string cls, double size)
    {
        var style = new Style(x => x.OfType<TextBlock>().Class(cls));
        style.Setters.Add(new Setter(TextBlock.FontSizeProperty, size));
        return style;
    }
}
