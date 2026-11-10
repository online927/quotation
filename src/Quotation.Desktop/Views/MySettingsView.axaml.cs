using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Quotation.Desktop.ViewModels;

namespace Quotation.Desktop.Views;

public partial class MySettingsView : UserControl
{
    public MySettingsView() => InitializeComponent();

    private void OnBrowse(object? sender, RoutedEventArgs e) => Browse(proforma: false);

    private void OnBrowseProforma(object? sender, RoutedEventArgs e) => Browse(proforma: true);

    private async void OnChooseBackground(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MySettingsViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage) return;
        var picked = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Background picture", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.bmp", "*.webp"] }],
        });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path) vm.SetBackgroundPicture(path);
    }

    private async void Browse(bool proforma)
    {
        if (DataContext is not MySettingsViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanPickFolder: true } storage) return;
        var picked = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = proforma ? "Folder for proforma invoice PDFs" : "Folder for quotation PDFs", AllowMultiple = false,
        });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path) vm.SetFolder(path, proforma);
    }
}
