using Avalonia;
using Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;
using Shape = Avalonia.Controls.Shapes.Shape;
using Avalonia.Layout;
using Avalonia.Media;
using Quotation.Desktop.ViewModels;

namespace Quotation.Desktop.Controls;

/// <summary>A page title with its sidebar icon in a soft blue square (the same look on every screen).</summary>
public sealed class PageTitle : UserControl
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<PageTitle, string?>(nameof(Text));
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<PageTitle, string?>(nameof(Icon));

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    /// <summary>The sidebar key of the icon ("dashboard", "products", "settings" …).</summary>
    public string? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }

    private readonly TextBlock _text = new() { Classes = { "h1" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly Path _icon = new()
    {
        Width = 20, Height = 20, Stretch = Stretch.Uniform, StrokeThickness = 1.8,
        StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Border _badge;

    public PageTitle()
    {
        _icon[!Shape.StrokeProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AccentBrush");
        _badge = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Child = _icon, VerticalAlignment = VerticalAlignment.Center };
        _badge[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AccentSoftBrush");
        Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { _badge, _text } };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) _text.Text = Text;
        if (change.Property == IconProperty)
        {
            _icon.Data = Icon is null ? null : NavIcons.For(Icon);
            _badge.IsVisible = _icon.Data is not null;
        }
    }
}
