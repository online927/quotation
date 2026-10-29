using Avalonia.Headless.XUnit;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Desktop.Controls;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Save as PDF (with seal), Without GST, delete and restore, Title Case.</summary>
public class OfficeFeaturesUiTests
{
    private const string Png =
        "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAJUlEQVR4nGM4ICDwn5aYYdSCUQtGLRi1YNSCUQtGLRi1YGhYAAC4lDX9H0TkSwAAAABJRU5ErkJggg==";

    private sealed class Ctx : IDisposable
    {
        public required TestServer Server;
        public required AppSession Session;
        public required RecordingLauncher Files;
        public required MainWindow Window;
        public required ShellViewModel Shell;
        public void Dispose()
        {
            Shell.Dispose();
            Window.Close();
            Server.Dispose();
        }
    }

    private static async Task<Ctx> SetupAsync()
    {
        var server = new TestServer(products: 300, customers: 100, today: DateOnly.FromDateTime(DateTime.Today));
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var files = new RecordingLauncher(config);
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = files };
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        var shell = new ShellViewModel(session, () => { });
        var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = shell } };
        window.Show();
        return new Ctx { Server = server, Session = session, Files = files, Window = window, Shell = shell };
    }

    private static async Task<QuotationEditorViewModel> DraftAsync(Ctx ctx)
    {
        ctx.Shell.Navigate("new");
        var editor = Assert.IsType<QuotationEditorViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => editor.NumberText.Contains("provisional"));
        await editor.SelectCustomerAsync((await ctx.Session.Api.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer.Id);
        editor.SelectProduct((await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product);
        editor.CommitEntryCommand.Execute(null);
        return editor;
    }

    [Fact]
    public void Title_case_capitalises_only_the_first_typed_letter_of_a_word()
    {
        Assert.Equal("T", TitleCase.Apply("", 0, "t"));
        Assert.Equal("N", TitleCase.Apply("Taparia ", 8, "n"));
        Assert.Equal("a", TitleCase.Apply("Taparia N", 9, "a"));      // inside a word
        Assert.Equal("m", TitleCase.Apply("150", 3, "m"));            // 150mm stays
        Assert.Equal("M", TitleCase.Apply("laser-", 6, "m"));          // laser-Makita, as in Tally
        Assert.Equal("S", TitleCase.Apply("Make Delta", 10, "S"));     // capitals typed stay
        Assert.Equal("B", TitleCase.Apply("Line one\n", 9, "b"));      // new description line
    }

    [Fact]
    public void Pdf_file_name_is_number_and_customer_without_forbidden_characters()
    {
        Assert.Equal("TSQ2526-3403 TATA Electronics System Solutions (117-1).pdf",
            QuotationEditorViewModel.PdfFileName("TSQ2526-3403", "TATA Electronics System Solutions (117/1)"));
        Assert.Equal("TSQ2526-3403 A-B- Co.pdf", QuotationEditorViewModel.PdfFileName("TSQ2526-3403", "A:B* Co."));
        Assert.True(QuotationEditorViewModel.PdfFileName("TSPI2526-13", new string('X', 200)).Length < 90);
    }

    [AvaloniaFact]
    public async Task Save_as_pdf_approves_a_draft_and_saves_number_and_customer_with_the_seal()
    {
        using var ctx = await SetupAsync();
        await ctx.Session.Api.UploadSealAsync(Convert.FromBase64String(Png));
        var settings = await ctx.Session.Api.SettingsAsync();
        settings.SealPin = "2020";
        await ctx.Session.Api.SaveSettingsAsync(settings);
        ctx.Session.Settings = null;

        var editor = await DraftAsync(ctx);
        Assert.True(editor.CanSaveAsPdf);
        await editor.SaveAsPdfCommand.ExecuteAsync(null);
        Assert.True(editor.IsFinal);                                  // approved with all checks
        var name = Assert.Single(ctx.Files.Saved);
        Assert.Equal($"{editor.Number} Sonepar India Private Limited.pdf", name);
        Assert.Contains("Saved to", editor.InfoMessage);

        // With the seal: asks for the PIN in the window; a wrong PIN saves nothing.
        await UiHelpers.WaitUntil(() => editor.CanSeal);
        editor.SealPdf = true;
        var saving = editor.SaveAsPdfCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => ctx.Shell.PendingDialog is { HasInput: true });
        ctx.Shell.PendingDialog!.Input = "1234";
        ctx.Shell.AnswerDialogCommand.Execute(true);
        await saving;
        Assert.Contains("Wrong seal PIN", editor.ErrorMessage);
        Assert.Single(ctx.Files.Saved);

        saving = editor.SaveAsPdfCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => ctx.Shell.PendingDialog is { HasInput: true });
        ctx.Shell.PendingDialog!.Input = "2020";
        ctx.Shell.AnswerDialogCommand.Execute(true);
        await saving;
        Assert.Equal(2, ctx.Files.Saved.Count);
        Assert.Contains("Sealed and saved", editor.InfoMessage);
    }

    [AvaloniaFact]
    public async Task Without_gst_from_my_settings_applies_to_the_next_quotation_only()
    {
        using var ctx = await SetupAsync();
        ctx.Shell.Navigate("mysettings");
        var mine = Assert.IsType<MySettingsViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => !mine.IsBusy);
        mine.WithoutGstNext = true;
        await UiHelpers.WaitUntil(() => ctx.Session.Api.CurrentUser!.WithoutGstNext);

        var editor = await DraftAsync(ctx);
        Assert.True(editor.WithoutGst);
        Assert.False(editor.ShowsTaxes);
        Assert.Equal("0%", editor.Lines[0].GstText);
        Assert.True(await editor.SaveAsync());
        Assert.Equal(0m, editor.Totals!.TaxTotal);
        Assert.False(ctx.Session.Api.CurrentUser!.WithoutGstNext);  // switched itself off

        var next = await DraftAsync(ctx);
        Assert.False(next.WithoutGst);
        Assert.True(next.ShowsTaxes);
    }

    [AvaloniaFact]
    public async Task Administrator_deletes_and_restores_from_the_deleted_list()
    {
        using var ctx = await SetupAsync();
        var editor = await DraftAsync(ctx);
        Assert.True(await editor.SaveAsync());
        var number = editor.Number;
        Assert.True(editor.CanDelete);

        var deleting = editor.DeleteCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => ctx.Shell.PendingDialog is not null);
        Assert.Contains(number!, ctx.Shell.PendingDialog!.Message);
        ctx.Shell.AnswerDialogCommand.Execute(true);
        await deleting;
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is not QuotationEditorViewModel);

        ctx.Shell.Navigate("deleted");
        var deleted = Assert.IsType<DeletedQuotationsViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => deleted.Rows.Count == 1);
        Assert.Equal(number, deleted.Rows[0].Number);
        Assert.NotNull(ctx.Window.FindDescendantOfType<DeletedQuotationsView>());

        var restoring = deleted.RestoreCommand.ExecuteAsync(deleted.Rows[0]);
        await UiHelpers.WaitUntil(() => ctx.Shell.PendingDialog is not null);
        ctx.Shell.AnswerDialogCommand.Execute(true);
        await restoring;
        Assert.Empty(deleted.Rows);
        Assert.Contains("restored", deleted.InfoMessage);
        Assert.Contains(await ctx.Session.Api.QuotationsAsync(), s => s.Number == number);
    }
}
