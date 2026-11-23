using Avalonia.Platform.Storage;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Quotation.Desktop.Views;

public partial class ProductsView : UserControl
{
    public ProductsView()
    {
        InitializeComponent();
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && ResultList.ItemCount > 0)
            {
                ResultList.Focus();
                e.Handled = true;
            }
        };
    }

    /// <summary>Round 36: pick a PDF datasheet for the selected item (administrators).</summary>
    private async void OnUploadDatasheet(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.ProductsViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Datasheet (PDF)",
            AllowMultiple = false,
            FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("PDF") { Patterns = ["*.pdf"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) await vm.UploadDatasheetAsync(path);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        SearchBox.Focus();
    }
}
