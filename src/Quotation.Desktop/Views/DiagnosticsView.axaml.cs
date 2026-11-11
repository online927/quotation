using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Quotation.Desktop.ViewModels;

namespace Quotation.Desktop.Views;

public partial class DiagnosticsView : UserControl
{
    public DiagnosticsView() => InitializeComponent();

    private async void OnRestoreClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DiagnosticsViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Restore from backup file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Quotations backup") { Patterns = ["*.tsq", "*.db"] },
            ],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) await vm.RestoreAsync(path);
    }
}
