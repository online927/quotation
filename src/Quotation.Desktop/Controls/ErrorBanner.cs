using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Quotation.Desktop.Controls;

/// <summary>A page's problem message: a red banner with an icon and ✕ (which clears the message). Hidden when there is none.</summary>
public sealed class ErrorBanner : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<ErrorBanner, string?>(nameof(Text), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private readonly TextBlock _message;

    public ErrorBanner()
    {
        Classes.Add("errorbanner");
        _message = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        _message.Bind(TextBlock.ForegroundProperty, _message.GetResourceObservable("BadBrush"));
        var icon = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M12,22 A10,10 0 1 0 12,2 A10,10 0 1 0 12,22 Z M12,7 L12,13 M12,16.5 L12,16.6"),
            StrokeThickness = 1.9, StrokeLineCap = PenLineCap.Round, Width = 16, Height = 16, Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon.Bind(Shape.StrokeProperty, icon.GetResourceObservable("BadBrush"));
        var close = new Button
        {
            Name = "CloseErrorButton", Content = "✕", Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
        };
        Avalonia.Controls.ToolTip.SetTip(close, "Close");
        close.Click += (_, _) => Text = null;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(icon);
        Grid.SetColumn(_message, 1);
        _message.Margin = new Thickness(10, 0, 8, 0);
        grid.Children.Add(_message);
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);
        var border = new Border { Padding = new Thickness(12, 8, 6, 8), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = grid };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("BadSoftBrush"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("BadBorderBrush"));
        Content = border;
        IsVisible = false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            _message.Text = Text;
            IsVisible = !string.IsNullOrWhiteSpace(Text);
        }
    }
}
