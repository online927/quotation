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
    /// <summary>0 Light, 1 Dark, 2 Same as Windows.</summary>
    [ObservableProperty] private int _themeIndex;

    public IReadOnlyList<string> ThemeChoices { get; } = ["Light", "Dark", "Same as Windows"];
    private static readonly string[] Themes = [AppTheme.Light, AppTheme.Dark, AppTheme.System];

    public string Title => "My Settings";
    public string UserName => _session.Api.CurrentUser?.DisplayName ?? "";
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
        PdfFolder = _session.Config.PdfFolder;
        ProformaPdfFolder = _session.Config.ProformaPdfFolder;
        _loading = false;
    });

    partial void OnThemeIndexChanged(int value)
    {
        if (_loading || value < 0 || value >= Themes.Length) return;
        _ = RunAsync(async () =>
        {
            await _session.SetThemeAsync(Themes[value]);
            InfoMessage = $"Screens: {ThemeChoices[value]}. This is saved with your login and used on every PC.";
        });
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
