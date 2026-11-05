using Avalonia;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Renders the folded and open buyer/references section when SCREENSHOT_DIR is set.</summary>
public class HeaderFoldPreviewTests
{
    [AvaloniaFact]
    public async Task Capture_folded_header()
    {
        var dir = Environment.GetEnvironmentVariable("SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);

        using var server = new TestServer(products: 300, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        var customer = (await session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer;
        var product = (await session.Api.SearchProductsAsync("universal bevel")).First().Product;

        var main = new MainWindowViewModel(session);
        var shell = new ShellViewModel(session, () => { });
        main.Content = shell;
        var window = new MainWindow { DataContext = main, Width = 1366, Height = 768 };
        window.Show();

        async Task Shot(string name)
        {
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png"));
        }

        shell.Navigate("new");
        var editor = (QuotationEditorViewModel)shell.CurrentPage!;
        await UiHelpers.WaitUntil(() => editor.NumberText.Contains("provisional"));
        await editor.SelectCustomerAsync(customer.Id);
        editor.OtherReferences = "Your enquiry dated 02-Nov-2026";
        await Shot("1-before-enter");
        editor.FoldHeader();
        editor.SelectProduct(product);
        editor.EntryQuantity = 2;
        editor.CommitEntryCommand.Execute(null);
        editor.SelectProduct((await session.Api.SearchProductsAsync("digital caliper")).First().Product);
        editor.CommitEntryCommand.Execute(null);
        editor.Lines[1].IsDragging = true; // as while dragging its grip
        editor.Lines[1].IsDragging = false;
        await Shot("2-folded");
        // Mouse over the first item line: Edit and remove appear.
        var lines = window.GetVisualDescendants().OfType<Avalonia.Controls.ItemsControl>().First(c => c.Name == "LinesList");
        if (lines.ContainerFromIndex(0) is Avalonia.Controls.Control row && row.TranslatePoint(new Avalonia.Point(400, 10), window) is { } p)
        {
            window.MouseMove(new Avalonia.Point(p.X - 30, p.Y));
            Dispatcher.UIThread.RunJobs();
            window.MouseMove(p);
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
            await Shot("2b-mouse-over-line");
            window.MouseMove(new Avalonia.Point(5, 5));
        }
        editor.ToggleHeaderCommand.Execute(null);
        await Shot("3-shown-again");
        editor.FoldHeader();
        editor.Remarks = "Prices valid for 15 days";
        editor.ToggleFooterCommand.Execute(null);
        await Shot("4-footer-shown");
        foreach (var sv in window.GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>()
                     .Where(v => v.FindAncestorOfType<QuotationEditorView>() is not null))
            sv.ScrollToEnd();
        await Shot("5-scrolled-to-totals");
        editor.IsDirty = false;
        shell.Dispose();
        window.Close();
    }
}
