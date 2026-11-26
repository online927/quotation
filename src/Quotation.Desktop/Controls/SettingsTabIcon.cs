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
