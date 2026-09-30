using System.Text.Json;
using Avalonia.Headless.XUnit;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

public class AiInboxTests
{
    private static int FirstId(JsonElement results, string field) => results.EnumerateArray().First().GetProperty(field).GetInt32();

    [AvaloniaFact]
    public async Task Ambiguous_request_is_resolved_by_the_user_and_opens_the_draft()
    {
        using var server = new TestServer(products: 300, customers: 100, today: DateOnly.FromDateTime(DateTime.Today));
        server.AiProvider = new ScriptedProvider(async ai =>
        {
            var c = await ai.Call("search_customer", new { query = "Bharat Precision" });
            var p = await ai.Call("search_product", new { query = "3 core 2.5 sqmm cable" });
            await ai.Call("create_quotation_draft", ScriptedProvider.Draft("Bharat Precision", FirstId(c, "customer_id"),
                ScriptedProvider.Line("3 core 2.5 sqmm cable", "3 core 2.5 sqmm cable", FirstId(p, "product_id"), 10)));
        });
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        var shell = new ShellViewModel(session, () => { });
        var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = shell } };
        window.Show();

        shell.Navigate("inbox");
        var inbox = Assert.IsType<AiInboxViewModel>(shell.CurrentPage);
        await UiHelpers.WaitUntil(() => !inbox.IsBusy);
        Assert.NotNull(window.FindDescendantOfType<AiInboxView>());

        inbox.RequestText = "Please quote 10 mtr 3 core 2.5 sqmm cable for Bharat Precision";
        await inbox.AnalyzeCommand.ExecuteAsync(null);
        var item = Assert.Single(inbox.Items);
        Assert.Equal("Needs clarification", item.StatusText);
        Assert.NotNull(item.SelectedCustomer);
        var line = Assert.Single(item.Lines);
        Assert.Null(line.Selected);          // AI's pick was not accepted
        Assert.Equal("Select product", line.StatusText);
        Assert.False(item.CanCreateDraft);

        line.Selected = line.Candidates.First(c => c.Candidate.Name == "HAVELLS 3 CORE 2.5 SQMM FLEXIBLE CABLE");
        Assert.True(item.CanCreateDraft);
        await item.CreateDraftCommand.ExecuteAsync(null);

        var editor = Assert.IsType<QuotationEditorViewModel>(shell.CurrentPage);
        await UiHelpers.WaitUntil(() => editor.Lines.Count == 1);
        Assert.Equal(QuotationStatus.PendingReview, editor.Status);
        Assert.Equal(QuotationSource.Ai, editor.Source);
        Assert.Equal("HAVELLS 3 CORE 2.5 SQMM FLEXIBLE CABLE", editor.Lines[0].ItemName);
        Assert.Null(editor.Number);
        await editor.SaveAsync();                 // human review → number assigned
        Assert.NotNull(editor.Number);
        shell.Dispose();
    }
}
