using System.Globalization;
using Avalonia.Data.Converters;

namespace Quotation.Mobile;

/// <summary>Line symbols of the phone screens (24 × 24 drawings, drawn with a stroke).</summary>
public static class PhoneIcons
{
    public const string Home = "M3,11 L12,3 L21,11 M5,9.5 L5,21 L19,21 L19,9.5 M10,21 L10,15 L14,15 L14,21";
    public const string List = "M8,6 L21,6 M8,12 L21,12 M8,18 L21,18 M3.5,6 L3.6,6 M3.5,12 L3.6,12 M3.5,18 L3.6,18";
    public const string Box = "M12,2.5 L21,7 L21,17 L12,21.5 L3,17 L3,7 Z M3,7 L12,11.5 L21,7 M12,11.5 L12,21.5";
    public const string Users = "M9,4 A3.5,3.5 0 1 0 9.01,4 Z M2.5,20 C3,16 5.5,14 9,14 C12.5,14 15,16 15.5,20 M16,4.5 A3,3 0 0 1 16,10.5 M18,14 C20,14.5 21.3,16.5 21.5,19.5";
    public const string More = "M5,10.6 A1.4,1.4 0 1 0 5.01,10.6 Z M12,10.6 A1.4,1.4 0 1 0 12.01,10.6 Z M19,10.6 A1.4,1.4 0 1 0 19.01,10.6 Z";
    public const string Phone = "M5,4 L9,4 L11,9 L8.5,10.5 C9.5,12.8 11.2,14.5 13.5,15.5 L15,13 L20,15 L20,19 C20,20 19,21 18,21 C10.5,20.5 3.5,13.5 3,6 C3,5 4,4 5,4 Z";
    public const string Chat = "M3.5,20.5 L4.8,16.3 C4,15 3.5,13.5 3.5,12 C3.5,7.3 7.3,3.5 12,3.5 C16.7,3.5 20.5,7.3 20.5,12 C20.5,16.7 16.7,20.5 12,20.5 C10.5,20.5 9,20.1 7.7,19.3 Z";
    public const string Mail = "M3,5 L21,5 L21,19 L3,19 Z M3.5,6.5 L12,13 L20.5,6.5";
    public const string File = "M6,2.5 L14,2.5 L19,7.5 L19,21.5 L6,21.5 Z M14,2.5 L14,7.5 L19,7.5";
    public const string Check = "M5,12 L10,17 L19,7";
    public const string Back = "M15,5 L8,12 L15,19";
    public const string Search = "M10.5,4 A6.5,6.5 0 1 0 10.51,4 Z M15.5,15.5 L20,20";
    public const string Share = "M18,3.5 A2.5,2.5 0 1 0 18.01,3.5 Z M6,9.5 A2.5,2.5 0 1 0 6.01,9.5 Z M18,15.5 A2.5,2.5 0 1 0 18.01,15.5 Z M8.2,10.8 L15.8,6.7 M8.2,13.2 L15.8,17.3";
    public const string Paste = "M9,3.5 L15,3.5 L15,6.5 L9,6.5 Z M9,5 L6,5 L6,21 L18,21 L18,5 L15,5";
    public const string Plus = "M12,5 L12,19 M5,12 L19,12";
    public const string Logout = "M15,4 L19,4 L19,20 L15,20 M10,8 L6,12 L10,16 M6,12 L15,12";
    public const string Refresh = "M20,11 A8,8 0 1 0 17.7,16.6 M20,4 L20,11 L13,11";
    public const string User = "M12,4 A4,4 0 1 0 12.01,4 Z M4,21 C5,17 8.5,15 12,15 C15.5,15 19,17 20,21";
    public const string Lock = "M5,11 L19,11 L19,21 L5,21 Z M8,11 L8,8 A4,4 0 0 1 16,8 L16,11";
    public const string Wifi = "M2,9 C7.5,4 16.5,4 22,9 M5.5,12.5 C9.5,9 14.5,9 18.5,12.5 M9,16 C10.8,14.5 13.2,14.5 15,16 M12,19.5 L12.01,19.5";
    public const string Clock = "M12,3 A9,9 0 1 0 12.01,3 Z M12,7 L12,12 L15,14";
    public const string Sparkle = "M12,3 L13.8,10.2 L21,12 L13.8,13.8 L12,21 L10.2,13.8 L3,12 L10.2,10.2 Z";
    public const string Palette = "M12,3 C7,3 3,7 3,12 C3,17 7,21 12,21 C13.2,21 13.8,20.2 13.4,19.2 C13,18.1 13.6,17 14.8,17 L17,17 C19.2,17 21,15.2 21,13 C21,7.5 17,3 12,3 Z M7.5,12 L7.51,12 M10,7.5 L10.01,7.5 M15,7.5 L15.01,7.5";
    public const string Server = "M4,4 L20,4 L20,10 L4,10 Z M4,14 L20,14 L20,20 L4,20 Z M7.5,7 L7.51,7 M7.5,17 L7.51,17";
}

public static class PhoneConverters
{
    /// <summary>True when the value is the parameter (the bottom menu's chosen page).</summary>
    public static readonly IValueConverter Is = new FuncValueConverter<string?, string?, bool>((value, parameter) => value == parameter);
}
