using System.Text.Json;
using Avalonia.Headless.XUnit;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

public class GmailPageTests
{
    [AvaloniaFact]
    public async Task Check_now_processes_mail_into_the_ai_inbox()
    {
        using var server = new TestServer(products: 300, customers: 100, today: DateOnly.FromDateTime(DateTime.Today));
        server.Gmail = new FakeGmail();
        server.Gmail.Add("purchase.pune@sonepar.example", "Rahul", "Quotation Required - Sonepar", "Please quote 3 core 2.5 sqmm cable");
        server.AiProvider = new ScriptedProvider(async ai =>
        {
            var p = await ai.Call("search_product", new { query = "3 core 2.5 sqmm cable" });
            await ai.Call("create_quotation_draft", ScriptedProvider.Draft("Sonepar", null,
                ScriptedProvider.Line("3 core 2.5 sqmm cable", "3 core 2.5 sqmm cable", null, null)));
        });
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi());
        await session.Api.LoginAsync("admin", "admin123");
        await session.Api.StartSyncAsync(SyncKind.Full, wait: true);
        var shell = new ShellViewModel(session, () => { });
        var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = shell } };
        window.Show();

        shell.Navigate("gmail");
        var gmail = Assert.IsType<GmailViewModel>(shell.CurrentPage);
        await UiHelpers.WaitUntil(() => gmail.Status is not null);
        Assert.NotNull(window.FindDescendantOfType<GmailView>());
        await gmail.PollCommand.ExecuteAsync(null);
        Assert.Contains("1 new", gmail.InfoMessage);
        Assert.Equal("Completed", Assert.Single(gmail.Messages).Status);

        gmail.OpenInboxCommand.Execute(null);
        var inbox = Assert.IsType<AiInboxViewModel>(shell.CurrentPage);
        await UiHelpers.WaitUntil(() => inbox.Items.Count == 1);
        var item = inbox.Items[0];
        Assert.Equal("E-mail: Quotation Required - Sonepar", item.Title);
        Assert.Equal("purchase.pune@sonepar.example", item.From);
        Assert.Equal("Needs clarification", item.StatusText);
        Assert.Equal("SONEPAR INDIA PRIVATE LIMITED", item.SelectedCustomer?.Candidate.Name); // from sender e-mail
        Assert.Equal("Select product", item.Lines[0].StatusText);
        shell.Dispose();
    }

    [Fact]
    public async Task Loopback_receiver_captures_the_authorization_code()
    {
        using var receiver = new LoopbackOAuthReceiver();
        Assert.StartsWith("http://127.0.0.1:", receiver.RedirectUri);
        var wait = receiver.WaitAsync(TimeSpan.FromSeconds(10));
        using var http = new HttpClient();
        var page = await http.GetStringAsync(receiver.RedirectUri + "?state=abc&code=4/xyz&scope=gmail.readonly");
        var (code, state, error) = await wait;
        Assert.Equal("4/xyz", code);
        Assert.Equal("abc", state);
        Assert.Null(error);
        Assert.Contains("Gmail connected", page);
    }
}
