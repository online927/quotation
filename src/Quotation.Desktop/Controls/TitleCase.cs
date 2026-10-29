using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Quotation.Desktop.Controls;

/// <summary>
/// Tally-style Title Case while typing: the first letter of each word is written as a capital. Only what is typed is
/// changed (letters typed in capitals stay, words starting with a number such as "150mm" stay as typed), and the text can
/// still be corrected afterwards. Switched on per box with <c>TitleCase.Enabled="True"</c> and for the whole app by
/// <see cref="IsOn"/> (Settings → Quotation).
/// </summary>
public static class TitleCase
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("Enabled", typeof(TitleCase));

    /// <summary>The setting "Title case in the quotation's text boxes".</summary>
    public static bool IsOn { get; set; } = true;

    static TitleCase()
    {
        EnabledProperty.Changed.AddClassHandler<TextBox>((box, e) =>
        {
            box.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
            if (e.NewValue is true) box.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        });
    }

    public static bool GetEnabled(TextBox box) => box.GetValue(EnabledProperty);
    public static void SetEnabled(TextBox box, bool value) => box.SetValue(EnabledProperty, value);

    private static void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (!IsOn || sender is not TextBox box || string.IsNullOrEmpty(e.Text)) return;
        var start = Math.Min(box.SelectionStart, box.SelectionEnd);
        e.Text = Apply(box.Text ?? "", start, e.Text);
    }

    /// <summary>The typed text with a capital first letter when it starts a word at <paramref name="caret"/>.</summary>
    public static string Apply(string current, int caret, string typed)
    {
        if (typed.Length == 0 || !char.IsLower(typed[0])) return typed;
        var before = caret <= 0 || caret > current.Length ? '\0' : current[caret - 1];
        var startsWord = before == '\0' || char.IsWhiteSpace(before) || before is '(' or '[' or '"' or '/' or '-';
        return startsWord ? char.ToUpperInvariant(typed[0]) + typed[1..] : typed;
    }
}
