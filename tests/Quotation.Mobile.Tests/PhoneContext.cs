using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Mobile.Services;
using Quotation.Mobile.ViewModels;
using Quotation.Mobile.Views;

namespace Quotation.Mobile.Tests;

/// <summary>A test server with Tally data and a few quotations, and the phone app in a phone-sized window.</summary>
public sealed class PhoneContext : IDisposable
{
    public required TestServer Server { get; init; }
    public required MainViewModel Main { get; init; }
    public required Window Window { get; init; }
    public required RecordingPhoneActions Phone { get; init; }
    public required PhoneSettings Settings { get; init; }

    public static async Task<PhoneContext> StartAsync(bool signIn = true, string user = "ravi")
    {
        QuotationsViewModel.TypingPause = TimeSpan.Zero;
        var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today)) { AdministratorsMakeDocuments = false };
        var admin = await server.LoginAsAdminAsync();
        await admin.StartSyncAsync(SyncKind.Full, wait: true);
        var all = await admin.SettingsAsync();
        all.Company.CompanyName = "T.SAIFUDDIN & CO.";
        all.Company.StateName = "Karnataka";
        all.Company.StateCode = "29";
        await admin.SaveSettingsAsync(all);
        var ravi = await server.LoginAsStandardAsync("ravi");
        await MakeQuotationsAsync(ravi);

        var phone = new RecordingPhoneActions { FilesFolder = Path.Combine(server.DataDirectory, "phone-files") };
        var settings = new PhoneSettings { FilePath = Path.Combine(server.DataDirectory, "phone.json"), ServerUrl = "http://server-pc:5080" };
        var session = new PhoneSession(settings, _ => server.CreateApi("PHONE"), phone);
        var main = new MainViewModel(session);
        var window = new Window { Width = 412, Height = 892, Content = new MainView { DataContext = main } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var ctx = new PhoneContext { Server = server, Main = main, Window = window, Phone = phone, Settings = settings };
        if (signIn)
        {
            main.Login.Username = user;
            main.Login.Password = user + "-pass";
            await main.Login.LoginCommand.ExecuteAsync(null);
            await Ui.WaitUntil(() => main.Page is HomeViewModel { IsBusy: false, Data: not null });
        }
        return ctx;
    }

    /// <summary>Two approved quotations and a draft, made today by Ravi.</summary>
    private static async Task MakeQuotationsAsync(QuotationApiClient api)
    {
        var customer = (await api.SearchCustomersAsync("SONEPAR INDIA PRIVATE LIMITED", 5)).First(h => h.Customer.Name == "SONEPAR INDIA PRIVATE LIMITED").Customer;
        var bevel = (await api.SearchProductsAsync("187-901-10-UNIVERSAL BEVEL PROTRACTOR", 5)).First().Product;
        var caliper = (await api.SearchProductsAsync("530-104-VERNIER CALIPER 0-150MM", 5)).First().Product;
        SaveQuotationRequest Request(params (ProductSummaryDto P, decimal Qty)[] lines) => new()
        {
            Date = DateOnly.FromDateTime(DateTime.Today), CustomerId = customer.Id,
            Lines = lines.Select(l => new QuotationLineDto { ProductId = l.P.Id, Quantity = l.Qty, Rate = l.P.Rate ?? 100, TallyRate = l.P.Rate }).ToList(),
        };
        foreach (var r in new[] { Request((bevel, 2)), Request((caliper, 5), (bevel, 1)) })
        {
            var q = await api.CreateQuotationAsync(r);
            await api.ApproveQuotationAsync(q.Id, revision: q.Revision);
        }
        await api.CreateQuotationAsync(Request((caliper, 3)));
    }

    public async Task ShotAsync(string name)
    {
        var dir = Environment.GetEnvironmentVariable("SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        Window.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png"));
    }

    public void Dispose()
    {
        Window.Close();
        Server.Dispose();
    }
}

public static class Ui
{
    /// <summary>Pumps the UI dispatcher until the condition holds (async page loads complete).</summary>
    public static async Task WaitUntil(Func<bool> condition, int timeoutMs = 15000)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > timeoutMs) throw new TimeoutException("Condition not met in time.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        Dispatcher.UIThread.RunJobs();
    }
}
