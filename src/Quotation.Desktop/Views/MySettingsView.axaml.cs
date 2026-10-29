using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Quotation.Desktop.ViewModels;

namespace Quotation.Desktop.Views;

public partial class MySettingsView : UserControl
{
    public MySettingsView() => InitializeComponent();

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MySettingsViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanPickFolder: true } storage) return;
        var picked = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Folder for saved PDFs", AllowMultiple = false });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path) vm.SetFolder(path);
    }
}
