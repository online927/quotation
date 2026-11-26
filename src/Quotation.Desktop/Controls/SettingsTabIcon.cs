using System.Globalization;
using Avalonia.Data.Converters;

namespace Quotation.Desktop.Controls;

/// <summary>Round 39 (RD-5): the symbol beside each Settings section name in the left menu.</summary>
public sealed class SettingsTabIcon : IValueConverter
{
    public static readonly SettingsTabIcon Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as string) switch
    {
        "Company" => "db",
        "Quotation" => "fileplus",
        "PDF" => "open",
        "Features" => "sparkle",
        "Backup & updates" => "restore",
        "Party Details" => "userplus",
        "Tally" => "sync",
        "Claude AI" => "sparkle",
        "Gmail" => "mailcheck",
        "Users" => "key",
        _ => "check",
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Round 40 (RD-5b): each Settings section's own icon colour (the same in every theme).</summary>
public sealed class SettingsTabColor : IValueConverter
{
    public static readonly SettingsTabColor Instance = new();

    private static Avalonia.Media.IBrush Brush(string hex) => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(hex));

    private static readonly Dictionary<string, Avalonia.Media.IBrush> Colours = new()
    {
        ["Company"] = Brush("#2F6FE4"),
        ["Quotation"] = Brush("#0E9F9A"),
        ["PDF"] = Brush("#E0453A"),
        ["Features"] = Brush("#E39B0B"),
        ["Backup & updates"] = Brush("#2E9E4F"),
        ["Party Details"] = Brush("#8B4FD8"),
        ["Tally"] = Brush("#EA7A1A"),
        ["Claude AI"] = Brush("#9B59E6"),
        ["Gmail"] = Brush("#D93A3A"),
        ["Users"] = Brush("#4F55D9"),
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string name && Colours.TryGetValue(name, out var brush) ? brush : Avalonia.AvaloniaProperty.UnsetValue;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
