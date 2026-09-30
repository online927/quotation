using System.Globalization;

namespace Quotation.Desktop.Services;

public static class Formatting
{
    public static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    public static string? LocalDateTime(DateTime? utc) =>
        utc is null ? null : DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).ToLocalTime().ToString("dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture);
}
