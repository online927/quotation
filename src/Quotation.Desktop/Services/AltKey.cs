using Avalonia.Input;
using Avalonia.Interactivity;

namespace Quotation.Desktop.Services;

/// <summary>
/// Alt shortcuts (Alt+P, Alt+C, Alt+I, Alt+← / →) with either Alt key: the right Alt key is "AltGr" on many keyboard
/// layouts (English (India), US-International …), and Windows reports it as Ctrl+Alt.
/// </summary>
public static class AltKey
{
    public static bool IsAlt(KeyModifiers modifiers) => modifiers is KeyModifiers.Alt or (KeyModifiers.Alt | KeyModifiers.Control);

    /// <summary>
    /// After an AltGr shortcut the layout may still type a letter (AltGr+P is "ö" on US-International): the letter that
    /// follows straight away is dropped, so it does not land in the box the shortcut moved to.
    /// </summary>
    public static void SwallowTypedLetter(Interactive within, KeyModifiers modifiers)
    {
        if (modifiers != (KeyModifiers.Alt | KeyModifiers.Control)) return;
        var until = DateTime.UtcNow.AddMilliseconds(250);
        void Drop(object? sender, TextInputEventArgs e)
        {
            within.RemoveHandler(InputElement.TextInputEvent, (EventHandler<TextInputEventArgs>)Drop);
            if (DateTime.UtcNow <= until) e.Handled = true;
        }
        within.AddHandler(InputElement.TextInputEvent, (EventHandler<TextInputEventArgs>)Drop, RoutingStrategies.Tunnel);
    }
}
