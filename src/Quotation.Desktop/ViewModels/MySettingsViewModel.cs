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

    public string Title => "My Settings";
    public string UserName => _session.Api.CurrentUser?.DisplayName ?? "";
    public string DefaultPdfFolder => new DocumentLauncher(new ClientConfig()).Folder;

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
        PdfFolder = _session.Config.PdfFolder;
        _loading = false;
    });

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

    [RelayCommand]
    private void SavePdfFolder()
    {
        var folder = PdfFolder.Trim();
        if (folder.Length > 0 && !Path.IsPathRooted(folder))
        {
            ErrorMessage = "Enter a full folder path, e.g. D:\\Quotations.";
            return;
        }
        try
        {
            if (folder.Length > 0) Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Cannot use that folder: " + ex.Message;
            return;
        }
        ErrorMessage = null;
        _session.Config.PdfFolder = folder;
        _session.Config.Save();
        InfoMessage = "PDFs will be saved in " + _session.Documents.Folder;
    }

    /// <summary>Called by the view after the user picked a folder.</summary>
    public void SetFolder(string path)
    {
        PdfFolder = path;
        SavePdfFolder();
    }
}
