using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Round 31: the quotation number as the heading, the date lined up with the References box.</summary>
public class Round31UiTests
{
    [AvaloniaTheory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Tally)]
    public async Task The_number_is_the_heading_and_the_date_sits_over_ref_dated(string theme)
    {
        using var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today));
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        using var shell = new ShellViewModel(session, () => { });
        var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = shell }, Width = 1366, Height = 768 };
        window.Show();
        try
        {
            ThemeService.Apply(theme);
            shell.Navigate("new");
            var editor = Assert.IsType<QuotationEditorViewModel>(shell.CurrentPage);
            await UiHelpers.WaitUntil(() => editor.NumberText.Contains("provisional"));
            Dispatcher.UIThread.RunJobs();
            var view = window.FindDescendantOfType<QuotationEditorView>()!;

            // The heading is the number (not "New Quotation"), marked provisional until it is saved.
            var number = editor.NumberText.Replace(" (provisional)", "");
            Assert.Equal(number, view.FindControl<TextBlock>("HeadingText")!.Text);
            Assert.Equal("(provisional)", editor.HeadingNote);
            Assert.True(view.FindControl<TextBlock>("HeadingNote")!.IsVisible);
            Assert.Equal("New Quotation", editor.Title); // the back button still names the page

            // The date box is right above "Ref. dated": the same left and right edges, inside the References box.
            Rect InWindow(Control c) => new(c.TranslatePoint(new Point(0, 0), window)!.Value, c.Bounds.Size);
            var date = InWindow(view.FindControl<CalendarDatePicker>("DateBox")!);
            var refDated = InWindow(view.FindControl<TextBox>("RefDateBox")!);
            Assert.True(view.FindControl<CalendarDatePicker>("DateBox")!.IsVisible);
            Assert.InRange(date.Right, refDated.Right - 1, refDated.Right + 1);
            if (refDated.Width >= 140) Assert.InRange(date.X, refDated.X - 1, refDated.X + 1);
            else Assert.InRange(date.Width, 139, 141); // narrow window: never too small for the date
            // Nothing of the title line sticks out past the boxes.
            var references = refDated.Right + 14;
            Assert.True(InWindow(view.FindControl<Grid>("DatePanel")!).Right <= references + 1);

            // Saved: the number without the note.
            await editor.SelectCustomerAsync((await session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
            editor.SelectProduct((await session.Api.SearchProductsAsync("universal bevel")).First().Product);
            editor.EntryRate = 100;
            editor.CommitEntryCommand.Execute(null);
            Assert.True(await editor.SaveAsync());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(editor.Number, view.FindControl<TextBlock>("HeadingText")!.Text);
            Assert.False(view.FindControl<TextBlock>("HeadingNote")!.IsVisible);
        }
        finally
        {
            ThemeService.Apply(AppTheme.Light);
            window.Close();
        }
    }
}
