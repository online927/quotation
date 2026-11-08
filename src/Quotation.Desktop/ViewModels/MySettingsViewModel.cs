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
    [ObservableProperty] private string _pdfFolder = "";
    [ObservableProperty] private string _proformaPdfFolder = "";
    /// <summary>Index in <see cref="ThemeChoices"/> (Default Light, Default Dark, Same as Windows, then the colour themes).</summary>
    [ObservableProperty] private int _themeIndex;

    public IReadOnlyList<string> ThemeChoices { get; } =
        ["Default Light", "Default Dark", "Same as Windows", "Sunset", "Lagoon", "Graphite", "Frost", "Glass (Colourful)", "Glass (Blue)", "Glass (Black)", "Glass (Green)", "Glass (Maroon)", "Neon Night", "Cyberpunk"];

    /// <summary>0 Normal, 1 Large.</summary>
    [ObservableProperty] private int _textSizeIndex;
    public IReadOnlyList<string> TextSizeChoices { get; } = ["Normal", "Large"];
    /// <summary>0 Comfortable, 1 Compact.</summary>
    [ObservableProperty] private int _spacingIndex;
    public IReadOnlyList<string> SpacingChoices { get; } = ["Comfortable", "Compact"];
    [ObservableProperty] private bool _reduceMotion;

    /// <summary>The layout of the PDFs this user makes (saved with the login).</summary>
    [ObservableProperty] private string _pdfLayout = Core.Domain.PdfLayouts.Tally;
    public IReadOnlyList<string> PdfLayoutChoices { get; } = [Core.Domain.PdfLayouts.Tally, Core.Domain.PdfLayouts.Zoho];
    private static readonly string[] Themes =
        [AppTheme.Light, AppTheme.Dark, AppTheme.System, AppTheme.Sunset, AppTheme.Lagoon, AppTheme.Graphite, AppTheme.Frost, AppTheme.Glass, AppTheme.GlassBlue, AppTheme.GlassBlack, AppTheme.GlassGreen, AppTheme.GlassMaroon, AppTheme.NeonNight, AppTheme.Cyberpunk];

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
        ThemeIndex = Math.Max(0, Array.IndexOf(Themes, me.Theme));
        TextSizeIndex = me.TextSize == AppDisplay.Large ? 1 : 0;
        SpacingIndex = me.Spacing == AppDisplay.Compact ? 1 : 0;
        ReduceMotion = me.ReduceMotion;
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

    partial void OnThemeIndexChanged(int value)
    {
        if (_loading || value < 0 || value >= Themes.Length) return;
        _ = RunAsync(async () =>
        {
            await _session.SetThemeAsync(Themes[value]);
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

    partial void OnReduceMotionChanged(bool value)
    {
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

    /// <summary>Called by the view after the user picked a folder.</summary>
    public void SetFolder(string path, bool proforma = false)
    {
        if (proforma) ProformaPdfFolder = path;
        else PdfFolder = path;
        SavePdfFolder();
    }
}
