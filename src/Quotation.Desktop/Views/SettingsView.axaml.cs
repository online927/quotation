using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Quotation.Desktop.ViewModels;

namespace Quotation.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    /// <summary>Round 38 (PL-1): pick the product links Excel (column A product code, column B link).</summary>
    private async void OnLoadProductLinks(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Product links (Excel: column A product code, column B link)",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Excel or CSV") { Patterns = ["*.xlsx", "*.csv", "*.txt"] }],
        });
        if (files.Count == 0) return;
        await using var stream = await files[0].OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        await vm.LoadProductLinksAsync(ms.ToArray(), files[0].Name);
    }

    private async void OnChooseUserSeal(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Seal picture (PNG or JPG)",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg"] }],
        });
        if (files.Count == 0) return;
        await using var stream = await files[0].OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        await vm.UploadUserSealAsync(ms.ToArray());
    }
}
