using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

/// <summary>
/// Settings every user can change for themselves: "Without GST" for their next quotation (stored on the server,
/// switches itself off after that quotation is saved) and the folder "Save as PDF" writes to on this PC.
/// </summary>
public sealed partial class MySettingsViewModel : ViewModelBase, ITitledPage, IReturnablePage
{
    private readonly AppSession _session;
    private bool _loading;

    [ObservableProperty] private bool _withoutGstNext;
    /// <summary>Round 36: the next quotation in US dollars (turns itself off once it is saved).</summary>
    [ObservableProperty] private bool _usdNext;
    /// <summary>Round 36: "1 USD = ₹ …" as typed.</summary>
    [ObservableProperty] private string _usdRateText = "";
    [ObservableProperty] private string _pdfFolder = "";
    [ObservableProperty] private string _proformaPdfFolder = "";
    /// <summary>Index in <see cref="ThemeChoices"/> (Glass (Blue) first, then the other themes).</summary>
    [ObservableProperty] private int _themeIndex;

    public IReadOnlyList<string> ThemeChoices { get; } = ThemeNames.All.Select(t => t.Name).ToList();

    /// <summary>0 Normal, 1 Large.</summary>
    [ObservableProperty] private int _textSizeIndex;
    public IReadOnlyList<string> TextSizeChoices { get; } = ["Normal", "Large"];
    /// <summary>0 Comfortable, 1 Compact.</summary>
    [ObservableProperty] private int _spacingIndex;
    public IReadOnlyList<string> SpacingChoices { get; } = ["Comfortable", "Compact"];
    [ObservableProperty] private bool _reduceMotion;

    /// <summary>0 = "TSQ2627-5 Customer.pdf", 1 = "Customer TSQ2627-5.pdf" (saved with the login).</summary>
    [ObservableProperty] private int _pdfFileOrderIndex;
    /// <summary>Save as PDF also opens the PDF (saved with the login; on by default).</summary>
    [ObservableProperty] private bool _openPdfAfterSave = true;
    public IReadOnlyList<string> PdfFileOrderChoices { get; } =
        ["Number then customer:  TSQ2627-5 Sonepar India.pdf", "Customer then number:  Sonepar India TSQ2627-5.pdf"];

    /// <summary>The layout of the PDFs this user makes (saved with the login).</summary>
    [ObservableProperty] private string _pdfLayout = Core.Domain.PdfLayouts.Tally;
    public IReadOnlyList<string> PdfLayoutChoices { get; } = Core.Domain.PdfLayouts.All;
    private static readonly string[] Themes = ThemeNames.All.Select(t => t.Theme).ToArray();

    // Background picture (this PC)
    public bool HasBackgroundPicture => _session.BackgroundPicturePath is not null;
    public string BackgroundPictureText => HasBackgroundPicture ? "A picture is shown behind the pages on this PC." : "No picture (the theme's own background).";

    /// <summary>Called with the file chosen in "Choose picture…".</summary>
    public void SetBackgroundPicture(string path)
    {
        ErrorMessage = _session.SetBackgroundPicture(path);
        OnPropertyChanged(nameof(HasBackgroundPicture));
        OnPropertyChanged(nameof(BackgroundPictureText));
    }

    [RelayCommand]
    private void RemoveBackgroundPicture()
    {
        _session.RemoveBackgroundPicture();
        OnPropertyChanged(nameof(HasBackgroundPicture));
        OnPropertyChanged(nameof(BackgroundPictureText));
    }

    public string Title => "My Settings";
    public string UserName => _session.Api.CurrentUser?.DisplayName ?? "";
    /// <summary>The Quotation Server this PC uses (changed in the server box on the sign-in screen).</summary>
    public string ServerAddress => _session.Config.ServerUrl;
    public string ComputerName => Environment.MachineName;
    /// <summary>The active financial year (from Tally), e.g. "2026-27".</summary>
    [ObservableProperty] private string _financialYear = "…";
    public string DefaultPdfFolder => new DocumentLauncher(new ClientConfig()).Folder;
    /// <summary>Shown in the empty proforma box: "Proforma Invoices" inside the quotation folder.</summary>
    public string DefaultProformaPdfFolder => new DocumentLauncher(new ClientConfig { PdfFolder = PdfFolder.Trim() }).ProformaFolder;

    partial void OnPdfFolderChanged(string value) => OnPropertyChanged(nameof(DefaultProformaPdfFolder));

    public MySettingsViewModel(AppSession session)
    {
        _session = session;
        _ = LoadAsync();
    }

    public void OnReturnedTo() => _ = LoadAsync();

    private Task LoadAsync() => RunAsync(async () =>
    {
        var me = await _session.Api.MeAsync();
        _session.Api.UpdateCurrentUser(me);
        _loading = true;
        WithoutGstNext = me.WithoutGstNext;
        UsdNext = me.UsdNext;
        UsdRateText = me.UsdRate > 0 ? me.UsdRate.ToString("0.00##", System.Globalization.CultureInfo.InvariantCulture) : "";
        ThemeIndex = Math.Max(0, Array.IndexOf(Themes, AppTheme.Normalize(me.Theme)));
        TextSizeIndex = me.TextSize == AppDisplay.Large ? 1 : 0;
        SpacingIndex = me.Spacing == AppDisplay.Compact ? 1 : 0;
        ReduceMotion = me.ReduceMotion;
        PdfFileOrderIndex = me.PdfFileOrder == PdfFileOrders.CustomerFirst ? 1 : 0;
        OpenPdfAfterSave = me.OpenPdfAfterSave;
        var settings = await _session.GetSettingsAsync(refresh: true);
        PdfLayout = Core.Domain.PdfLayouts.IsValid(me.PdfLayout) ? me.PdfLayout! : settings.Quotation.DefaultPdfLayout;
        PdfFolder = _session.Config.PdfFolder;
        ProformaPdfFolder = _session.Config.ProformaPdfFolder;
        _loading = false;
        FinancialYear = (await _session.Api.StatusAsync()).ActiveFinancialYear ?? "not known yet";
    });

    partial void OnPdfLayoutChanged(string value)
    {
        if (_loading || !Core.Domain.PdfLayouts.IsValid(value)) return;
        _ = RunAsync(async () =>
        {
            _session.Api.UpdateCurrentUser(await _session.Api.SetMyPreferencesAsync(new MyPreferencesDto(PdfLayout: value)));
            InfoMessage = $"Your PDFs will be made in the {value} layout (Save as PDF, Approve, Open PDF, Preview). Saved with your login.";
        });
    }

    partial void OnOpenPdfAfterSaveChanged(bool value)
    {
        if (_loading) return;
        _ = RunAsync(async () =>
        {
            _session.Api.UpdateCurrentUser(await _session.Api.SetMyPreferencesAsync(new MyPreferencesDto(OpenPdfAfterSave: value)));
            InfoMessage = value ? "Save as PDF will also open the PDF." : "Save as PDF will only save the PDF in your folder.";
        });
    }

    partial void OnPdfFileOrderIndexChanged(int value)
    {
        if (_loading || value < 0) return;
        _ = RunAsync(async () =>
        {
            var order = value == 1 ? PdfFileOrders.CustomerFirst : PdfFileOrders.NumberFirst;
            _session.Api.UpdateCurrentUser(await _session.Api.SetMyPreferencesAsync(new MyPreferencesDto(PdfFileOrder: order)));
            InfoMessage = value == 1
                ? "Your PDF files will be named customer first, e.g. 'Sonepar India TSQ2627-5.pdf'."
                : "Your PDF files will be named number first, e.g. 'TSQ2627-5 Sonepar India.pdf'.";
        });
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (_loading || value < 0 || value >= Themes.Length) return;
        _ = RunAsync(async () =>
        {
            await _session.SetThemeAsync(Themes[value]);
            OnSelectionChanged(); // "theme colour" follows the theme
            InfoMessage = $"Screens: {ThemeChoices[value]}. This is saved with your login and used on every PC.";
        });
    }

    partial void OnTextSizeIndexChanged(int value)
    {
        if (_loading) return;
        _ = RunAsync(() => _session.SetDisplayAsync(textSize: value == 1 ? AppDisplay.Large : AppDisplay.Normal));
    }

    partial void OnSpacingIndexChanged(int value)
    {
        if (_loading) return;
        _ = RunAsync(() => _session.SetDisplayAsync(spacing: value == 1 ? AppDisplay.Compact : AppDisplay.Comfortable));
    }

    /// <summary>The Animations switch reads the right way round: On = animations on (it is "reduce animations" inside).</summary>
    public bool AnimationsOn
    {
        get => !ReduceMotion;
        set => ReduceMotion = !value;
    }

    partial void OnReduceMotionChanged(bool value)
    {
        OnPropertyChanged(nameof(AnimationsOn));
        if (_loading) return;
        _ = RunAsync(() => _session.SetDisplayAsync(reduceMotion: value));
    }

    partial void OnWithoutGstNextChanged(bool value)
    {
        if (_loading) return;
        _ = RunAsync(async () =>
        {
            _session.Api.UpdateCurrentUser(await _session.Api.SetMyPreferencesAsync(new MyPreferencesDto(value)));
            InfoMessage = value
                ? "Your next quotation or proforma invoice will be made without GST (0 %, no tax lines). The switch turns itself off after you save it."
                : "Without GST is off.";
        });
    }

    partial void OnUsdNextChanged(bool value)
    {
        if (_loading) return;
        _ = SaveUsdAsync();
    }

    /// <summary>Round 36: saves the USD switch and the rupees for 1 USD (the switch needs a rate).</summary>
    [RelayCommand]
    public Task SaveUsdAsync() => RunAsync(async () =>
    {
        var text = (UsdRateText ?? "").Trim().TrimStart('₹').Trim();
        decimal? rate = null;
        if (text.Length > 0)
        {
            if (!decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var r) || r <= 0)
            {
                ErrorMessage = "Type the rupees for 1 USD, e.g. 84.20.";
                return;
            }
            rate = r;
        }
        if (UsdNext && rate is null)
        {
            ErrorMessage = "Type the rupees for 1 USD first (e.g. 84.20).";
            _loading = true;
            UsdNext = false;
            _loading = false;
            return;
        }
        _session.Api.UpdateCurrentUser(await _session.Api.SetMyPreferencesAsync(new MyPreferencesDto(UsdNext: UsdNext, UsdRate: rate)));
        InfoMessage = UsdNext
            ? $"Your next quotation will be in US dollars (1 USD = ₹ {rate:0.00}), without GST. The switch turns itself off after you save it."
            : "USD quotation is off.";
    });

    /// <summary>Saves both folders (quotations, proforma invoices) for this PC.</summary>
    [RelayCommand]
    private void SavePdfFolder()
    {
        var folder = PdfFolder.Trim();
        var proforma = ProformaPdfFolder.Trim();
        foreach (var f in new[] { folder, proforma })
        {
            if (f.Length > 0 && !Path.IsPathRooted(f))
            {
                ErrorMessage = "Enter a full folder path, e.g. D:\\Quotations.";
                return;
            }
            try
            {
                if (f.Length > 0) Directory.CreateDirectory(f);
            }
            catch (Exception ex)
            {
                ErrorMessage = "Cannot use that folder: " + ex.Message;
                return;
            }
        }
        ErrorMessage = null;
        _session.Config.PdfFolder = folder;
        _session.Config.ProformaPdfFolder = proforma;
        _session.Config.Save();
        InfoMessage = $"Quotations will be saved in {_session.Documents.Folder}, proforma invoices in {_session.Documents.ProformaFolder}";
    }

    /// <summary>My Settings → WhatsApp (this PC): the WhatsApp app, WhatsApp Web (browser) or the app's own WhatsApp window.</summary>
    private void SetWhatsApp(string mode)
    {
        if (string.Equals(_session.Config.WhatsAppOpensIn, mode, StringComparison.OrdinalIgnoreCase)) return;
        _session.Config.WhatsAppOpensIn = mode;
        try
        {
            _session.Config.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept for this session; only this PC's choice.
        }
        OnPropertyChanged(nameof(WhatsAppOnWeb));
        OnPropertyChanged(nameof(WhatsAppOnApp));
        OnPropertyChanged(nameof(WhatsAppInsideApp));
        InfoMessage = mode switch
        {
            DocumentLauncher.WhatsAppWeb => "WhatsApp opens in WhatsApp Web (your browser) on this PC.",
            DocumentLauncher.WhatsAppInApp => DocumentLauncher.AppWindowBrowser() is null
                ? "WhatsApp opens in its own window. Edge or Chrome is needed for that; without it WhatsApp Web opens in your browser."
                : "WhatsApp opens in its own window on this PC. Scan the QR code with your phone once; it stays signed in.",
            _ => "WhatsApp opens in the WhatsApp app on this PC.",
        };
    }

    private bool Is(string mode) => string.Equals(_session.Config.WhatsAppOpensIn, mode, StringComparison.OrdinalIgnoreCase);

    public bool WhatsAppOnWeb
    {
        get => Is(DocumentLauncher.WhatsAppWeb);
        set { if (value) SetWhatsApp(DocumentLauncher.WhatsAppWeb); }
    }

    /// <summary>Round 36: "Inside this app".</summary>
    public bool WhatsAppInsideApp
    {
        get => Is(DocumentLauncher.WhatsAppInApp);
        set { if (value) SetWhatsApp(DocumentLauncher.WhatsAppInApp); }
    }

    public bool WhatsAppOnApp
    {
        get => !WhatsAppOnWeb && !WhatsAppInsideApp;
        set { if (value) SetWhatsApp(DocumentLauncher.WhatsAppApp); }
    }

    /// <summary>Called by the view after the user picked a folder.</summary>
    public void SetFolder(string path, bool proforma = false)
    {
        if (proforma) ProformaPdfFolder = path;
        else PdfFolder = path;
        SavePdfFolder();
    }

    // ------------------------------------------------------------------ selected text (this PC)

    public bool SelectionSoftBlue { get => IsSelection(TextSelectionLook.SoftBlue); set { if (value) SetSelection(TextSelectionLook.SoftBlue); } }
    public bool SelectionSolidBlue { get => IsSelection(TextSelectionLook.SolidBlue); set { if (value) SetSelection(TextSelectionLook.SolidBlue); } }
    public bool SelectionMarker { get => IsSelection(TextSelectionLook.Marker); set { if (value) SetSelection(TextSelectionLook.Marker); } }
    public bool SelectionThemeColour { get => IsSelection(TextSelectionLook.ThemeColour); set { if (value) SetSelection(TextSelectionLook.ThemeColour); } }

    /// <summary>The sample under the choices: the highlight and the letters as they look in a box.</summary>
    public Avalonia.Media.IBrush SelectionSampleBack =>
        new Avalonia.Media.SolidColorBrush(TextSelectionLook.Colours(_session.Config.TextSelection, TextSelectionLook.Accent()).Back);
    public Avalonia.Media.IBrush? SelectionSampleLetters =>
        TextSelectionLook.Colours(_session.Config.TextSelection, TextSelectionLook.Accent()).Letters is { } letters
            ? new Avalonia.Media.SolidColorBrush(letters)
            : Avalonia.Application.Current?.Resources.TryGetResource("TextBrush", Avalonia.Application.Current.ActualThemeVariant, out var t) == true
                ? t as Avalonia.Media.IBrush
                : null;

    public static string SelectionName(string look) => TextSelectionLook.Normalize(look) switch
    {
        TextSelectionLook.SolidBlue => "Blue sketch",
        TextSelectionLook.Marker => "Yellow highlighter",
        TextSelectionLook.ThemeColour => "Theme colour",
        _ => "Soft blue",
    };

    /// <summary>Delivery time on items (this PC, Round 35): the Delivery box next to Description. Off at first.</summary>
    public bool DeliveryTimeOnItems
    {
        get => _session.Config.DeliveryTimeOnItems;
        set
        {
            if (value == _session.Config.DeliveryTimeOnItems) return;
            _session.Config.DeliveryTimeOnItems = value;
            try
            {
                _session.Config.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Kept for this session; only this PC's choice.
            }
            OnPropertyChanged();
            InfoMessage = value ? "Delivery box on: new quotation screens show it next to Description." : "Delivery box off.";
        }
    }

    /// <summary>Silent (this PC, Round 35): on = no click when a quotation is saved or approved. On at first.</summary>
    public bool Silent
    {
        get => _session.Config.Silent;
        set
        {
            if (value == _session.Config.Silent) return;
            _session.Config.Silent = value;
            try
            {
                _session.Config.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Kept for this session; only this PC's choice.
            }
            ClickSound.Silent = value;
            OnPropertyChanged();
            if (!value) ClickSound.Saved(); // hear it once
        }
    }

    /// <summary>Sharp text (this PC): ClearType-style letters and slightly bigger small text; off = the old look.</summary>
    public bool SharpText
    {
        get => _session.Config.SharpText;
        set
        {
            if (value == _session.Config.SharpText) return;
            _session.Config.SharpText = value;
            try
            {
                _session.Config.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Kept for this session; only this PC's choice.
            }
            SharpTextLook.Apply(value);
            OnPropertyChanged();
            InfoMessage = value ? "Sharp text on for this PC." : "Sharp text off: the old look on this PC.";
        }
    }

    private bool IsSelection(string look) => TextSelectionLook.Normalize(_session.Config.TextSelection) == look;

    private void SetSelection(string look)
    {
        if (IsSelection(look)) return;
        _session.Config.TextSelection = look;
        try
        {
            _session.Config.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept for this session; only this PC's choice.
        }
        TextSelectionLook.Apply(look);
        OnSelectionChanged();
        InfoMessage = $"Selected text: {SelectionName(look)} on this PC.";
    }

    private void OnSelectionChanged()
    {
        foreach (var name in new[] { nameof(SelectionSoftBlue), nameof(SelectionSolidBlue), nameof(SelectionMarker), nameof(SelectionThemeColour),
                     nameof(SelectionSampleBack), nameof(SelectionSampleLetters) })
            OnPropertyChanged(name);
    }
}
