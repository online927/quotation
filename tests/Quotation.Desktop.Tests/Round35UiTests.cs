using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Quotation.Contracts;
using Quotation.Desktop.Controls;
using Quotation.Desktop.Views;
using Avalonia.Threading;
using Quotation.Core.Domain;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.Tests;

/// <summary>Round 35: the wrong GSTIN catch (6B); GST by the buyer's or the ship-to state (B).</summary>
public class Round35UiTests
{
    private const string Sonepar = "SONEPAR INDIA PRIVATE LIMITED";

    [AvaloniaFact]
    public async Task A_mistyped_gstin_from_tally_is_marked_red_and_approve_asks_first()
    {
        // One typing mistake in the Tally ledger: the check letter (the 15th character) should be S
        using var ctx = await Round33UiTests.StartAsync(setUp: s => s.Tally.Data.Ledgers.Single(l => l.Name == Sonepar).Gstin = "27AAACS1234F1Z3");
        var (editor, view) = await Round33UiTests.NewQuotationAsync(ctx);
        try
        {
            Assert.Equal("27AAACS1234F1Z3", editor.BuyerGstin);
            Assert.Equal("Looks mistyped: the last letter should be S", editor.BuyerGstinProblem);

            // The Buyer box: a red ring and the red note with what to do
            editor.HeaderCollapsed = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("bad", view.FindControl<TextBox>("BuyerGstinBox")!.Classes);
            Assert.True(view.FindControl<StackPanel>("BuyerGstinProblemNote")!.IsEffectivelyVisible);
            // Folded: the customer line still says so
            editor.HeaderCollapsed = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FindControl<Border>("FoldedGstinChip")!.IsEffectivelyVisible);

            var product = (await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product;
            editor.SelectProduct(product);
            editor.EntryRate = 15600;
            editor.CommitEntryCommand.Execute(null);

            // Approve asks first; "Go back" stops (nothing saved, nothing approved)
            var approving = editor.ApproveCommand.ExecuteAsync(null);
            await UiHelpers.WaitUntil(() => ctx.Shell.PendingDialog is not null);
            Assert.Contains("The buyer's GSTIN 27AAACS1234F1Z3 looks mistyped: its last letter should be S.", ctx.Shell.PendingDialog!.Message);
            Assert.Equal("Approve anyway", ctx.Shell.PendingDialog.PrimaryText);
            ctx.Shell.PendingDialog.Choose(false);
            await approving;
            Assert.False(editor.IsFinal);
            Assert.Null(editor.Id);

            // "Approve anyway" goes on
            approving = editor.ApproveCommand.ExecuteAsync(null);
            await UiHelpers.WaitUntil(() => ctx.Shell.PendingDialog is not null);
            ctx.Shell.PendingDialog!.Choose(true);
            await approving;
            Assert.True(editor.IsFinal);
        }
        finally
        {
            editor.IsDirty = false;
        }
    }

    [AvaloniaFact]
    public async Task A_right_gstin_has_no_mark_and_approve_does_not_ask()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var (editor, view) = await Round33UiTests.NewQuotationAsync(ctx);
        try
        {
            Assert.Equal("27AAACS1234F1ZS", editor.BuyerGstin);
            Assert.Null(editor.BuyerGstinProblem);
            editor.HeaderCollapsed = false;
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("bad", view.FindControl<TextBox>("BuyerGstinBox")!.Classes);
            Assert.False(view.FindControl<StackPanel>("BuyerGstinProblemNote")!.IsVisible);
            editor.HeaderCollapsed = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.FindControl<Border>("FoldedGstinChip")!.IsVisible);

            var product = (await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product;
            editor.SelectProduct(product);
            editor.EntryRate = 15600;
            editor.CommitEntryCommand.Execute(null);
            await editor.ApproveCommand.ExecuteAsync(null);
            Assert.Null(ctx.Shell.PendingDialog);
            Assert.True(editor.IsFinal);
        }
        finally
        {
            editor.IsDirty = false;
        }
    }

    [AvaloniaFact]
    public async Task Gst_by_the_buyers_or_the_ship_to_state_is_chosen_under_the_consignee()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var admin = await ctx.Server.LoginAsAdminAsync();
        var settings = await admin.SettingsAsync();
        settings.Company.StateCode = "29";                                   // our company in Karnataka
        settings.Quotation.TaxPresentation = TaxPresentation.ComputeTax;
        await admin.SaveSettingsAsync(settings);
        var (editor, view) = await Round33UiTests.NewQuotationAsync(ctx);  // the buyer Sonepar in Maharashtra
        try
        {
            var product = (await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product;
            editor.SelectProduct(product);
            editor.EntryRate = 15600;
            editor.CommitEntryCommand.Execute(null);
            editor.HeaderCollapsed = false;
            Dispatcher.UIThread.RunJobs();
            string[] Taxes() => editor.Totals!.Taxes.Select(t => t.Name).Distinct().Order().ToArray();

            // Consignee the same as the buyer: nothing to choose
            Assert.False(editor.ShowGstByChoice);
            Assert.False(view.FindControl<StackPanel>("GstByChoice")!.IsEffectivelyVisible);
            Assert.Equal(["IGST"], Taxes());

            // Goods to their Bangalore warehouse (Karnataka): the choice shows, a new quotation follows the buyer's state
            editor.SelectedShipTo = editor.ShipToOptions.First(o => o.Label.Contains("Bangalore"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.ShowGstByChoice);
            Assert.True(view.FindControl<StackPanel>("GstByChoice")!.IsEffectivelyVisible);
            Assert.Equal("Buyer's state · Maharashtra", editor.GstByBuyerText);
            Assert.Equal("IGST", editor.GstByBuyerTax);
            Assert.Equal("Ship-to state · Karnataka", editor.GstByShipToText);
            Assert.Equal("CGST + SGST", editor.GstByShipToTax);
            Assert.True(view.FindControl<RadioButton>("GstByBuyerOption")!.IsChecked);
            Assert.Equal(["IGST"], Taxes());

            // The ship-to state: the totals change at once and it is saved with the quotation
            view.FindControl<RadioButton>("GstByShipToOption")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(editor.GstByBuyer);
            Assert.Equal(["CGST", "SGST"], Taxes());
            Assert.True(editor.IsDirty);
            Assert.True(await editor.SaveAsync());
            var saved = await ctx.Session.Api.QuotationAsync(editor.Id!.Value);
            Assert.False(saved.GstByBuyer);
            Assert.Equal(["CGST", "SGST"], saved.Taxes.Select(t => t.Name).Distinct().Order());
            Assert.Equal(editor.Totals!.GrandTotal, saved.GrandTotal);

            // Back to the buyer's state
            view.FindControl<RadioButton>("GstByBuyerOption")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.GstByBuyer);
            Assert.True(await editor.SaveAsync());
            saved = await ctx.Session.Api.QuotationAsync(editor.Id!.Value);
            Assert.True(saved.GstByBuyer);
            Assert.Equal(["IGST"], saved.Taxes.Select(t => t.Name).Distinct());
            Assert.Equal(editor.Totals!.GrandTotal, saved.GrandTotal);
        }
        finally
        {
            editor.IsDirty = false;
        }
    }

    [AvaloniaFact]
    public async Task Insert_puts_the_cursor_in_the_customer_box_even_when_the_buyer_box_is_folded()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var (editor, view) = await Round33UiTests.NewQuotationAsync(ctx);
        try
        {
            editor.HeaderCollapsed = true;
            TopLevel.GetTopLevel(ctx.Window)!.FocusManager!.ClearFocus();
            Dispatcher.UIThread.RunJobs();

            ctx.Window.KeyPress(Key.Insert, RawInputModifiers.None, PhysicalKey.Insert, null);
            await UiHelpers.WaitUntil(() => view.FindControl<SearchPicker>("CustomerBox")!.InputBox.IsFocused);
            Assert.False(editor.HeaderCollapsed);
        }
        finally
        {
            editor.IsDirty = false;
        }
    }

    [AvaloniaFact]
    public async Task Delete_clears_only_an_approved_quotation_and_never_while_typing()
    {
        using var ctx = await Round33UiTests.StartAsync();
        void Press() { ctx.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null); Dispatcher.UIThread.RunJobs(); }

        // A draft: Delete does nothing to it
        var (draft, _) = await Round33UiTests.NewQuotationAsync(ctx);
        TopLevel.GetTopLevel(ctx.Window)!.FocusManager!.ClearFocus();
        Press();
        Assert.Same(draft, ctx.Shell.CurrentPage);
        draft.IsDirty = false;

        // Approved on the New quotation page: Delete while typing in a box is just Delete …
        var (approved, view) = await Round33UiTests.ApprovedOnNewPageAsync(ctx);
        var id = approved.Id!.Value;
        var typing = view.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => !t.IsReadOnly && t.IsEffectivelyEnabled && t.IsEffectivelyVisible);
        if (typing is not null)
        {
            typing.Focus();
            Press();
            Assert.Same(approved, ctx.Shell.CurrentPage);
        }

        // … anywhere else it clears: a new empty quotation; the approved one stays as it is
        TopLevel.GetTopLevel(ctx.Window)!.FocusManager!.ClearFocus();
        Press();
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationEditorViewModel e && e != approved && e.Id is null);
        Assert.Empty(((QuotationEditorViewModel)ctx.Shell.CurrentPage!).Lines);
        Assert.Contains((await ctx.Session.Api.QuotationAsync(id)).Status, new[] { QuotationStatus.Approved, QuotationStatus.Generated });
        ((QuotationEditorViewModel)ctx.Shell.CurrentPage!).IsDirty = false;

        // Opened from a list (Today's Quotations / History): Delete does nothing
        ctx.Shell.OpenQuotation(id);
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationEditorViewModel { IsFinal: true, IsBusy: false } e && e != approved);
        var opened = ctx.Shell.CurrentPage;
        TopLevel.GetTopLevel(ctx.Window)!.FocusManager!.ClearFocus();
        Press();
        Assert.Same(opened, ctx.Shell.CurrentPage);
    }

    [AvaloniaFact]
    public async Task Two_quotation_screens_at_once_keep_their_own_gst_choice()
    {
        // While one page leaves and the next one comes in, two quotation screens are in the window together: the "GST by"
        // choices of one must never tick or untick the other's (that froze the app).
        using var ctx = await Round33UiTests.StartAsync();
        var (first, _) = await Round33UiTests.NewQuotationAsync(ctx);
        first.IsDirty = false; // no "save changes?" on the way to the next one
        ctx.Shell.Navigate("new");
        await UiHelpers.WaitUntil(() => ctx.Shell.CurrentPage is QuotationEditorViewModel e && e != first);
        var second = (QuotationEditorViewModel)ctx.Shell.CurrentPage!;
        try
        {
            var leaving = new QuotationEditorView { DataContext = first };
            var host = ctx.Window.FindDescendantOfType<QuotationEditorView>()!.FindAncestorOfType<Panel>()!;
            host.Children.Add(leaving);
            Dispatcher.UIThread.RunJobs();

            first.GstByBuyer = true;
            second.GstByBuyer = false;
            Dispatcher.UIThread.RunJobs();
            Assert.True(first.GstByBuyer);
            Assert.False(second.GstByBuyer);
            second.GstByBuyer = true;
            first.GstByBuyer = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(first.GstByBuyer);
            Assert.True(second.GstByBuyer);
            host.Children.Remove(leaving);
        }
        finally
        {
            first.IsDirty = false;
            second.IsDirty = false;
        }
    }

    [AvaloniaFact]
    public async Task Settings_switches_are_named_greet_fest_and_xo_without_explanations()
    {
        using var ctx = await Round33UiTests.StartAsync(user: "admin");
        ctx.Shell.Navigate("settings");
        await UiHelpers.WaitUntil(() => ctx.Window.FindDescendantOfType<SettingsView>() is not null);
        Dispatcher.UIThread.RunJobs();
        var view = ctx.Window.FindDescendantOfType<SettingsView>()!;
        var headings = view.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("h2")).Select(t => t.Text).ToList();
        Assert.Contains("Greet", headings);
        Assert.Contains("Fest", headings);
        Assert.Contains("XO", headings);
        Assert.DoesNotContain("Greetings", headings);
        Assert.DoesNotContain("Festival looks", headings);
        Assert.DoesNotContain("XO game", headings);
        foreach (var name in new[] { "GreetingsSwitch", "FestivalLooksSwitch", "XoGameSwitch" })
        {
            var toggle = view.FindControl<ToggleSwitch>(name)!;
            Assert.Equal("On", toggle.OnContent);
            Assert.Equal("Off", toggle.OffContent);
        }
    }

    [AvaloniaFact]
    public async Task The_administrator_deletes_a_user_after_saying_yes()
    {
        using var ctx = await Round33UiTests.StartAsync(user: "admin");
        ctx.Shell.Navigate("settings");
        var vm = Assert.IsType<SettingsViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => vm.FilteredUsers.Any(u => u.Username == "ravi"));
        var view = ctx.Window.FindDescendantOfType<SettingsView>()!;
        var delete = view.FindControl<Button>("DeleteUserButton")!;

        // The administrator: no Delete button
        vm.SelectedUser = vm.FilteredUsers.First(u => u.Role == UserRole.Admin);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.CanDeleteEditingUser);
        Assert.False(delete.IsVisible);

        // A user: Delete asks first; Cancel keeps them
        vm.SelectedUser = vm.FilteredUsers.First(u => u.Username == "ravi");
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.CanDeleteEditingUser);
        Assert.True(delete.IsVisible);
        var deleting = vm.DeleteUserCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => ctx.Shell.PendingDialog is not null);
        Assert.Contains("Delete ravi", ctx.Shell.PendingDialog!.Message);
        Assert.Equal("Delete", ctx.Shell.PendingDialog.PrimaryText);
        ctx.Shell.PendingDialog.Choose(false);
        await deleting;
        Assert.Contains(await ctx.Session.Api.UsersAsync(), u => u.Username == "ravi");

        // Delete: gone from the list
        deleting = vm.DeleteUserCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => ctx.Shell.PendingDialog is not null);
        ctx.Shell.PendingDialog!.Choose(true);
        await deleting;
        Assert.DoesNotContain(await ctx.Session.Api.UsersAsync(), u => u.Username == "ravi");
        Assert.DoesNotContain(vm.FilteredUsers, u => u.Username == "ravi");
        Assert.Equal("User 'ravi' deleted.", vm.InfoMessage);
        Assert.Null(vm.EditingUser);
    }

    [AvaloniaFact]
    public async Task Settings_has_pdf_and_features_tabs_with_the_seal_pin_switch_off()
    {
        using var ctx = await Round33UiTests.StartAsync(user: "admin");
        ctx.Shell.Navigate("settings");
        await UiHelpers.WaitUntil(() => ctx.Window.FindDescendantOfType<SettingsView>() is not null);
        Dispatcher.UIThread.RunJobs();
        var view = ctx.Window.FindDescendantOfType<SettingsView>()!;
        var tabs = view.GetLogicalDescendants().OfType<TabControl>().First();
        var headers = tabs.Items.OfType<TabItem>().Select(t => t.Header as string).ToList();
        Assert.Equal(["Company", "Quotation", "PDF", "Features"], headers.Take(4));
        TabItem Tab(string h) => tabs.Items.OfType<TabItem>().First(t => t.Header as string == h);
        Assert.Contains(Tab("Features").GetLogicalDescendants().OfType<ToggleSwitch>(), t => t.Name == "XoGameSwitch");
        var pin = Tab("Features").GetLogicalDescendants().OfType<ToggleSwitch>().Single(t => t.Name == "SealPinSwitch");
        Assert.False(pin.IsChecked); // off by default
        Assert.Contains(Tab("PDF").GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "PDF layout");
        Assert.DoesNotContain(Tab("Quotation").GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "PDF layout");
        var layouts = Tab("PDF").GetLogicalDescendants().OfType<ComboBox>().First().ItemsSource!.Cast<string>().ToList();
        Assert.Equal([PdfLayouts.Tally, PdfLayouts.Zoho, PdfLayouts.Minimal], layouts);
    }

    [AvaloniaFact]
    public async Task The_gear_menu_closes_with_its_x()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var (editor, view) = await Round33UiTests.NewQuotationAsync(ctx);
        try
        {
            var gear = view.FindControl<Button>("StepGearButton")!;
            gear.Flyout!.ShowAt(gear);
            Dispatcher.UIThread.RunJobs();
            Assert.True(gear.Flyout.IsOpen);
            var close = TopLevel.GetTopLevel(ctx.Window)!.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "StepMenuCloseButton")
                        ?? ((gear.Flyout as Flyout)!.Content as Control)!.GetLogicalDescendants().OfType<Button>().First(b => b.Name == "StepMenuCloseButton");
            close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(gear.Flyout.IsOpen);
        }
        finally
        {
            editor.IsDirty = false;
        }
    }

    [AvaloniaFact]
    public async Task A_delivery_time_is_typed_or_picked_shown_as_a_chip_and_remembered()
    {
        using var ctx = await Round33UiTests.StartAsync();
        Assert.True(new ClientConfig().DeliveryTimeOnItems);     // on at first (user: "Delivery box, default on")
        ctx.Session.Config.DeliveryTimeOnItems = false;           // switched off: no box
        var (off, offView) = await Round33UiTests.NewQuotationAsync(ctx);
        Assert.False(off.ShowDeliveryBox);
        Dispatcher.UIThread.RunJobs();
        Assert.False(offView.FindControl<TextBox>("DeliveryBox")!.IsEffectivelyVisible);
        off.IsDirty = false;

        ctx.Session.Config.DeliveryTimeOnItems = true;
        ctx.Shell.Navigate("dashboard");
        var (editor, view) = await Round33UiTests.NewQuotationAsync(ctx);
        try
        {
            Assert.True(editor.ShowDeliveryBox);
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FindControl<TextBox>("DeliveryBox")!.IsEffectivelyVisible);
            Assert.Equal(QuotationEditorViewModel.NoDelivery, editor.DeliveryChoices[0]);
            Assert.Contains("Ex-stock", editor.DeliveryChoices);

            // Empty by default; picked from the list …
            editor.SelectProduct((await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product);
            Assert.Equal("", editor.EntryDueOn);
            // Clicked in the real pop-up list (the list sits outside the page, in its own pop-up)
            var pick = view.FindControl<Button>("DeliveryPickButton")!;
            pick.Flyout!.ShowAt(pick);
            Dispatcher.UIThread.RunJobs();
            var exStock = ((Flyout)pick.Flyout).Content is ItemsControl list
                ? list.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Ex-stock")
                : throw new InvalidOperationException("no list");
            exStock.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Ex-stock", editor.EntryDueOn);
            Assert.False(pick.Flyout.IsOpen);
            editor.CommitEntryCommand.Execute(null);
            Assert.Equal("Ex-stock", editor.Lines[0].DueOn);
            Assert.True(editor.Lines[0].DueOnInStock);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(view.GetVisualDescendants().OfType<Border>(), b => b.Name == "DueOnChip" && b.IsEffectivelyVisible);

            // … or typed (Round 38, DM-1: not remembered in the list); "(none)" clears it
            editor.SelectProduct((await ctx.Session.Api.SearchProductsAsync("digital caliper")).First().Product);
            editor.EntryDueOn = "10 days";
            editor.CommitEntryCommand.Execute(null);
            Assert.Equal("10 days", editor.Lines[1].DueOn);
            Assert.False(editor.Lines[1].DueOnInStock);
            Assert.DoesNotContain("10 days", editor.DeliveryChoices);
            Assert.Empty(ctx.Session.Config.RecentDeliveryTimes);
            editor.EntryDueOn = "x";
            editor.PickDeliveryCommand.Execute(QuotationEditorViewModel.NoDelivery);
            Assert.Equal("", editor.EntryDueOn);

            // Saved with the quotation
            Assert.True(await editor.SaveAsync());
            Assert.Equal(["Ex-stock", "10 days"], (await ctx.Session.Api.QuotationAsync(editor.Id!.Value)).Lines.Select(l => l.DueOn));
        }
        finally
        {
            editor.IsDirty = false;
            ctx.Session.Config.DeliveryTimeOnItems = false;
        }
    }

    [AvaloniaFact]
    public async Task Save_clicks_once_and_approve_twice()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var silent = ClickSound.Silent;
        try
        {
            ClickSound.Silent = true; // (tests only; the app is never silent since Round 39)
            var played = ClickSound.PlayedCount;
            var (editor, _) = await Round33UiTests.NewQuotationAsync(ctx);
            editor.SelectProduct((await ctx.Session.Api.SearchProductsAsync("universal bevel")).First().Product);
            editor.CommitEntryCommand.Execute(null);
            Assert.True(await editor.SaveAsync());
            Assert.Equal("click", ClickSound.LastAsked);
            Assert.Equal(played, ClickSound.PlayedCount); // silent: nothing played

            ClickSound.Silent = false;
            await editor.ApproveCommand.ExecuteAsync(null);
            await UiHelpers.WaitUntil(() => editor is { IsFinal: true, IsBusy: false });
            Assert.Equal("click-double", ClickSound.LastAsked); // two quick clicks (not the save's single click as well)
            Assert.Equal(played + 1, ClickSound.PlayedCount);
        }
        finally
        {
            ClickSound.Silent = silent;
        }
    }

    [AvaloniaFact]
    public async Task The_clear_button_takes_the_themes_own_colour()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var (approved, view) = await Round33UiTests.ApprovedOnNewPageAsync(ctx);
        var clear = view.FindControl<Button>("ClearButton")!;
        Assert.Contains("accentfill", clear.Classes);
        try
        {
            foreach (var (theme, dark) in new[] { (AppTheme.GlassMaroon, false), (AppTheme.Midnight, true) })
            {
                Quotation.Desktop.Services.ThemeService.Apply(theme);
                Dispatcher.UIThread.RunJobs();
                var accent = ((Avalonia.Media.ISolidColorBrush)ctx.Window.FindResource("AccentBrush")!).Color;
                var presenter = clear.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
                Assert.Equal(accent, ((Avalonia.Media.ISolidColorBrush)presenter.Background!).Color);
                var text = ((Avalonia.Media.ISolidColorBrush)ctx.Window.FindResource("OnAccentBrush")!).Color;
                Assert.Equal(dark, text != Avalonia.Media.Colors.White); // dark text on the gold of Midnight Stars
            }
        }
        finally
        {
            Quotation.Desktop.Services.ThemeService.Apply(AppTheme.GlassBlue);
        }
    }
}
