using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Mobile.Services;
using Quotation.Mobile.ViewModels;
using Quotation.Mobile.Views;

namespace Quotation.Mobile.Tests;

public class PhoneAppTests
{
    [AvaloniaFact]
    public async Task Sign_in_shows_the_dashboard_and_the_bottom_menu_goes_to_each_page()
    {
        using var ctx = await PhoneContext.StartAsync(signIn: false);
        var login = ctx.Main.Login;
        Assert.False(ctx.Main.IsSignedIn);
        Assert.False(login.ShowServer); // an address is saved: the server boxes stay folded
        await ctx.ShotAsync("phone-1-login");

        login.Username = "ravi";
        login.Password = "wrong";
        await login.LoginCommand.ExecuteAsync(null);
        Assert.False(ctx.Main.IsSignedIn);
        Assert.False(string.IsNullOrEmpty(login.ErrorMessage));

        login.Password = "ravi-pass";
        await login.LoginCommand.ExecuteAsync(null);
        await Ui.WaitUntil(() => ctx.Main.Page is HomeViewModel { IsBusy: false, Data: not null });
        var home = (HomeViewModel)ctx.Main.Page!;
        Assert.Equal(3, home.Data!.TodaysQuotations); // as on the PC: the draft made today counts too
        Assert.Equal(3, home.Latest.Count);
        Assert.Equal("T.SAIFUDDIN & CO.", ctx.Settings.CompanyName); // kept for the sign-in screen
        Assert.Equal("TS", login.CompanyInitials);
        Assert.NotNull(ctx.Settings.Token); // the next start opens signed in
        await ctx.ShotAsync("phone-2-dashboard");

        foreach (var (tab, type) in new[] { (Tabs.Quotations, typeof(QuotationsViewModel)), (Tabs.Products, typeof(ProductsViewModel)),
                     (Tabs.Customers, typeof(CustomersViewModel)), (Tabs.More, typeof(MoreViewModel)), (Tabs.Home, typeof(HomeViewModel)) })
        {
            await ctx.Main.ShowTabCommand.ExecuteAsync(tab);
            Assert.IsType(type, ctx.Main.Page);
            Assert.Equal(tab, ctx.Main.Tab);
        }

        // The phone's Back on a bottom-menu page goes Home; on Home the phone may close the app.
        await ctx.Main.ShowTabCommand.ExecuteAsync(Tabs.Products);
        Assert.True(ctx.Main.GoBack());
        await Ui.WaitUntil(() => ctx.Main.Page is HomeViewModel);
        Assert.False(ctx.Main.GoBack());
    }

    [AvaloniaFact]
    public async Task Quotations_today_history_drafts_and_search_open_a_quotation_to_share_and_approve()
    {
        using var ctx = await PhoneContext.StartAsync();
        await ctx.Main.ShowTabCommand.ExecuteAsync(Tabs.Quotations);
        var list = (QuotationsViewModel)ctx.Main.Page!;
        await Ui.WaitUntil(() => !list.IsBusy);
        Assert.Equal(3, list.Items.Count); // today: the two approved and the draft
        await ctx.ShotAsync("phone-3-quotations");

        list.IsDrafts = true;
        await Ui.WaitUntil(() => !list.IsBusy && list.List == QuotationsViewModel.Drafts);
        var draftRow = Assert.Single(list.Items);
        Assert.Equal("Draft", draftRow.StatusText);

        list.IsHistory = true;
        await Ui.WaitUntil(() => !list.IsBusy && list.List == QuotationsViewModel.History);
        Assert.Equal(3, list.Items.Count);
        list.Search = "caliper";
        await Ui.WaitUntil(() => !list.IsBusy && list.Items.Count == 2);

        // An approved quotation: Preview and WhatsApp hand the PDF to the phone.
        var approved = list.Items.First(r => r.IsOk);
        await list.OpenCommand.ExecuteAsync(approved);
        var detail = Assert.IsType<QuotationDetailViewModel>(ctx.Main.Page);
        await Ui.WaitUntil(() => detail.Quotation is not null && !detail.IsBusy);
        Assert.Equal(approved.Number, detail.Title);
        Assert.False(detail.CanApprove);
        Assert.Equal(detail.Quotation!.Lines.Count, detail.Lines.Count);
        await ctx.ShotAsync("phone-4-quotation");
        await detail.WhatsAppCommand.ExecuteAsync(null);
        var shared = Assert.Single(ctx.Phone.Done);
        Assert.StartsWith("whatsapp ", shared);
        var pdf = shared["whatsapp ".Length..];
        Assert.True(File.Exists(pdf));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf), 0, 4));
        Assert.EndsWith(PhoneSession.PdfFileName(detail.Quotation), pdf);
        await detail.PreviewCommand.ExecuteAsync(null);
        Assert.StartsWith("open ", ctx.Phone.Done[^1]);

        // Back to the list, then the draft: Approve gives it the next number.
        Assert.True(ctx.Main.GoBack());
        await Ui.WaitUntil(() => ctx.Main.Page is QuotationsViewModel);
        list.IsDrafts = true;
        await Ui.WaitUntil(() => !list.IsBusy && list.List == QuotationsViewModel.Drafts && list.Items.Count == 1);
        await list.OpenCommand.ExecuteAsync(list.Items[0]);
        detail = Assert.IsType<QuotationDetailViewModel>(ctx.Main.Page);
        await Ui.WaitUntil(() => detail.Quotation is not null && !detail.IsBusy);
        Assert.True(detail.CanApprove);
        await detail.PreviewCommand.ExecuteAsync(null); // a draft's preview
        Assert.StartsWith("open ", ctx.Phone.Done[^1]);
        await detail.ApproveCommand.ExecuteAsync(null);
        Assert.Null(detail.ErrorMessage);
        Assert.Equal(QuotationStatus.Generated, detail.Quotation!.Status);
        Assert.StartsWith("Approved as ", detail.InfoMessage);
        Assert.False(detail.CanApprove);
    }

    [AvaloniaFact]
    public async Task Products_and_customers_are_found_like_on_the_pc()
    {
        using var ctx = await PhoneContext.StartAsync();
        await ctx.Main.ShowTabCommand.ExecuteAsync(Tabs.Products);
        var products = (ProductsViewModel)ctx.Main.Page!;
        Assert.True(products.NothingTyped);
        products.Search = "vernier caliper";
        await Ui.WaitUntil(() => !products.IsBusy && products.Items.Count > 0);
        var first = products.Items[0];
        await products.ToggleCommand.ExecuteAsync(first);
        Assert.True(first.IsOpen);
        Assert.Contains(first.Info, i => i.Label == "HSN / GST");
        Assert.Contains(first.Info, i => i.Label == "Rate (Tally)");
        await ctx.ShotAsync("phone-5-products");
        await products.ToggleCommand.ExecuteAsync(first);
        Assert.False(first.IsOpen);

        await ctx.Main.ShowTabCommand.ExecuteAsync(Tabs.Customers);
        var customers = (CustomersViewModel)ctx.Main.Page!;
        customers.Search = "sonepar";
        await Ui.WaitUntil(() => !customers.IsBusy && customers.Items.Count > 0);
        var sonepar = customers.Items.First(c => c.Name == "SONEPAR INDIA PRIVATE LIMITED");
        await customers.OpenCommand.ExecuteAsync(sonepar);
        var detail = Assert.IsType<CustomerDetailViewModel>(ctx.Main.Page);
        await Ui.WaitUntil(() => detail.Customer is not null && !detail.IsBusy);
        Assert.Equal(3, detail.Quotations.Count); // the two approved and the draft
        Assert.Contains(detail.Info, i => i.Label == "GSTIN");
        await ctx.ShotAsync("phone-6-customer");
        if (detail.HasPhone)
        {
            detail.CallCommand.Execute(null);
            Assert.StartsWith("call ", ctx.Phone.Done[^1]);
            detail.WhatsAppCommand.Execute(null);
            Assert.StartsWith("chat ", ctx.Phone.Done[^1]);
        }
        await detail.OpenCommand.ExecuteAsync(detail.Quotations[0]);
        Assert.IsType<QuotationDetailViewModel>(ctx.Main.Page);
        Assert.True(ctx.Main.GoBack());
        await Ui.WaitUntil(() => ctx.Main.Page is CustomerDetailViewModel);
        Assert.True(ctx.Main.GoBack());
        await Ui.WaitUntil(() => ctx.Main.Page is CustomersViewModel);
    }

    [AvaloniaFact]
    public async Task A_pasted_message_becomes_an_approved_quotation()
    {
        using var ctx = await PhoneContext.StartAsync();
        await ctx.Main.ShowTabCommand.ExecuteAsync(Tabs.More);
        var more = (MoreViewModel)ctx.Main.Page!;
        Assert.True(more.MakesDocuments);
        await more.NewFromMessageCommand.ExecuteAsync(null);
        var message = Assert.IsType<MessageViewModel>(ctx.Main.Page);
        message.Paste("""
            [17/11/26, 10:42 am] Rahul: Sonepar India Private Limited

            187-901-10, 2, 15600
            Make - Mitutoyo

            530-104, 5, 3250, 5

            Digital Caliper 150mm Special, 2, 3450
            """);
        Assert.DoesNotContain("Rahul", message.Text);
        await message.MakeCommand.ExecuteAsync(null);
        Assert.Null(message.ErrorMessage);
        Assert.True(message.HasResult);
        Assert.False(message.HasProblems);
        Assert.Equal(3, message.Lines.Count);
        Assert.True(message.Lines[0].IsTally);
        Assert.Equal("187-901-10-UNIVERSAL BEVEL PROTRACTOR", message.Lines[0].Name);
        Assert.True(message.Lines[2].IsTyped);
        Assert.True(message.CanSave);
        await ctx.ShotAsync("phone-7-message");

        await message.ApproveCommand.ExecuteAsync(null);
        Assert.Null(message.ErrorMessage);
        var detail = Assert.IsType<QuotationDetailViewModel>(ctx.Main.Page);
        await Ui.WaitUntil(() => detail.Quotation is not null && !detail.IsBusy);
        Assert.Equal(QuotationStatus.Generated, detail.Quotation!.Status);
        Assert.Equal(QuotationSource.Formater, detail.Quotation.Source);
        Assert.Equal(3, detail.Quotation.Lines.Count);
        Assert.StartsWith("Approved as ", detail.InfoMessage);

        // A message with a mistake: the problem is listed and nothing can be saved.
        Assert.True(ctx.Main.GoBack());
        message.Text = "BuyersHub\n\nDial Gauge, 2, 12O11, 10";
        await message.MakeCommand.ExecuteAsync(null);
        Assert.True(message.HasProblems);
        Assert.False(message.CanSave);
    }

    [AvaloniaFact]
    public async Task Every_theme_colours_the_phone_and_the_choice_is_saved_for_the_user()
    {
        using var ctx = await PhoneContext.StartAsync();
        await ctx.Main.ShowTabCommand.ExecuteAsync(Tabs.More);
        var more = (MoreViewModel)ctx.Main.Page!;
        Assert.Equal(AppTheme.Light, more.Theme!.Key);
        Assert.Contains(more.Themes, t => t.Name == "Same as the phone");
        try
        {
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Ocean, AppTheme.Midnight, AppTheme.GlassMaroon, AppTheme.MaroonMahal, AppTheme.ForestGold })
            {
                more.Theme = more.Themes.First(t => t.Key == theme);
                await Ui.WaitUntil(() => !more.IsBusy);
                Assert.Equal(theme, ThemeService.Current);
                await ctx.Main.ShowTabCommand.ExecuteAsync(Tabs.Home);
                await Ui.WaitUntil(() => ctx.Main.Page is HomeViewModel { IsBusy: false });
                await ctx.ShotAsync("phone-theme-" + theme);
                await ctx.Main.ShowTabCommand.ExecuteAsync(Tabs.More);
            }
            Assert.Equal(AppTheme.ForestGold, (await ctx.Main.Session.Api.MeAsync()).Theme); // the user's own theme (the PC follows)
            Assert.Equal(AppTheme.ForestGold, ctx.Settings.Theme);
        }
        finally
        {
            ThemeService.Apply(AppTheme.Light);
        }
    }

    [AvaloniaFact]
    public async Task The_app_opens_signed_in_until_signing_out()
    {
        using var ctx = await PhoneContext.StartAsync();
        var saved = PhoneSettings.Load(ctx.Settings.FilePath);
        Assert.Equal(ctx.Settings.Token, saved.Token);

        // A new start of the app with the saved sign-in.
        var again = new MainViewModel(new PhoneSession(saved, _ => ctx.Server.CreateApi("PHONE"), ctx.Phone));
        await again.StartAsync();
        Assert.True(again.IsSignedIn);
        Assert.IsType<HomeViewModel>(again.Page);

        await ctx.Main.ShowTabCommand.ExecuteAsync(Tabs.More);
        await ((MoreViewModel)ctx.Main.Page!).SignOutCommand.ExecuteAsync(null);
        Assert.False(ctx.Main.IsSignedIn);
        Assert.Null(PhoneSettings.Load(ctx.Settings.FilePath).Token);
        var third = new MainViewModel(new PhoneSession(PhoneSettings.Load(ctx.Settings.FilePath), _ => ctx.Server.CreateApi("PHONE"), ctx.Phone));
        await third.StartAsync();
        Assert.False(third.IsSignedIn);
    }

    [AvaloniaFact]
    public async Task The_tally_theme_looks_like_tally_on_the_phone_and_asks_before_approving()
    {
        using var ctx = await PhoneContext.StartAsync();
        try
        {
            await ctx.Main.ShowTabCommand.ExecuteAsync(Tabs.More);
            var more = (MoreViewModel)ctx.Main.Page!;
            more.Theme = more.Themes.First(t => t.Key == AppTheme.Tally);
            await Ui.WaitUntil(() => !more.IsBusy);
            var main = ctx.Window.GetVisualDescendants().OfType<MainView>().First();
            Assert.Contains("tally", main.Classes);
            Assert.True(ColourThemes.IsFlat);

            await ctx.Main.ShowTabCommand.ExecuteAsync(Tabs.Home);
            await Ui.WaitUntil(() => ctx.Main.Page is HomeViewModel { IsBusy: false });
            var head = ctx.Window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("pagehead") && b.IsEffectivelyVisible);
            Assert.Equal(Avalonia.Media.Color.Parse("#1F4D63"), ((Avalonia.Media.ISolidColorBrush)head.Background!).Color);
            await ctx.ShotAsync("phone-tally-dashboard");

            // A draft: Approve asks "Approve? Yes or No" first; No keeps the draft, Yes approves it.
            await ctx.Main.ShowQuotationsAsync(QuotationsViewModel.Drafts);
            var list = (QuotationsViewModel)ctx.Main.Page!;
            await Ui.WaitUntil(() => !list.IsBusy && list.Items.Count == 1);
            await ctx.ShotAsync("phone-tally-quotations");
            await list.OpenCommand.ExecuteAsync(list.Items[0]);
            var detail = (QuotationDetailViewModel)ctx.Main.Page!;
            await Ui.WaitUntil(() => detail.Quotation is not null && !detail.IsBusy);
            var approving = detail.ApproveCommand.ExecuteAsync(null);
            await Ui.WaitUntil(() => ctx.Main.Question is { Title: "Approve ?" });
            await ctx.ShotAsync("phone-tally-approve");
            ctx.Main.AnswerCommand.Execute("no");
            await approving;
            Assert.Equal(QuotationStatus.Draft, detail.Quotation!.Status);
            Assert.Null(ctx.Main.Question);
            approving = detail.ApproveCommand.ExecuteAsync(null);
            await Ui.WaitUntil(() => ctx.Main.Question is not null);
            ctx.Main.AnswerCommand.Execute("yes");
            await approving;
            Assert.Equal(QuotationStatus.Generated, detail.Quotation!.Status);
        }
        finally
        {
            ThemeService.Apply(AppTheme.Light);
        }
    }

    [Fact]
    public void Server_addresses_and_whatsapp_numbers_are_tidied()
    {
        Assert.Equal("http://192.168.1.20:5080", PhoneSettings.NormalizeServer("192.168.1.20"));
        Assert.Equal("http://server-pc:5080", PhoneSettings.NormalizeServer(" server-pc/ "));
        Assert.Equal("http://192.168.1.20:6000", PhoneSettings.NormalizeServer("192.168.1.20:6000"));
        Assert.Equal("https://q.example.com:5080", PhoneSettings.NormalizeServer("https://q.example.com"));
        Assert.Equal("", PhoneSettings.NormalizeServer("  "));
        Assert.Equal("919845012345", PhoneNumbers.ForWhatsApp("98450 12345"));
        Assert.Equal("919845012345", PhoneNumbers.ForWhatsApp("09845012345"));
        Assert.Equal("919845012345", PhoneNumbers.ForWhatsApp("+91 98450-12345"));
        Assert.Equal("98450 12345", PhoneNumbers.First("98450 12345 / 080-22223333"));
        Assert.Equal("TSQ2627-5 SONEPAR INDIA PVT LTD.pdf",
            PhoneSession.PdfFileName(new QuotationDto { Number = "TSQ2627-5", Buyer = new PartyDto { Name = "SONEPAR INDIA PVT/LTD." } }).Replace("  ", " "));
    }

    [AvaloniaFact]
    public async Task Every_page_draws_without_errors_on_a_phone_screen()
    {
        using var ctx = await PhoneContext.StartAsync();
        foreach (var tab in new[] { Tabs.Home, Tabs.Quotations, Tabs.Products, Tabs.Customers, Tabs.More })
        {
            await ctx.Main.ShowTabCommand.ExecuteAsync(tab);
            await Ui.WaitUntil(() => ctx.Main.Page is { IsBusy: false });
            var host = ctx.Window.GetVisualDescendants().OfType<ContentControl>().First(c => c.Name == "PageHost");
            Assert.NotNull(host.GetVisualDescendants().OfType<UserControl>().FirstOrDefault());
            Assert.DoesNotContain(host.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("Not found:") == true);
            // Nothing is wider than the phone.
            foreach (var c in host.GetVisualDescendants().OfType<Control>()
                         .Where(c => c.IsEffectivelyVisible && c.FindAncestorOfType<Viewbox>() is null && c is not Viewbox && c.GetType().Name != "ViewboxContainer"))
                Assert.True(c.Bounds.Width <= 412.5, $"{c.GetType().Name} {c.Name} on {tab} is {c.Bounds.Width:0} wide");
        }
    }
}
