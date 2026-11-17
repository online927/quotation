using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Quotation.Mobile.ViewModels;

namespace Quotation.Mobile.Views;

public partial class MessageView : UserControl
{
    public MessageView() => InitializeComponent();

    /// <summary>Paste: the phone's copied text into the box (WhatsApp times and names taken out).</summary>
    private async void OnPaste(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MessageViewModel vm || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        try { vm.Paste(await clipboard.TryGetTextAsync()); }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { }
    }
}
