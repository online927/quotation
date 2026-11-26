using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.Desktop.Controls;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>
/// Round 40: Settings rows and switch texts removed (ST-1, TC-1, FT-1, PD-1), the Zoho seal's own numbers (SL-1), no list
/// position text (PG-1), Update / Cancel in the list (UB-1), staying on the changed line and Home (ED-1).
/// </summary>
public class Round40UiTests
{
    private static async Task Pump(int rounds = 10)
    {
        for (var i = 0; i < rounds; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20); }
    }

    private static void Press(Window window, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPress(key, mods, PhysicalKey.None, null);
        window.KeyRelease(key, mods, PhysicalKey.None, null);
    }

    private static async Task<QuotationEditorViewModel> WithTwoLinesAsync(Round33UiTests.Ctx ctx)
    {
        var (editor, _) = await Round33UiTests.NewQuotationAsync(ctx);
        Assert.True(await editor.OpenPastedListAsync("187-901-10 x 1\n530-104 x 2"));
        await UiHelpers.WaitUntil(() => !editor.PasteStillSearching);
        editor.AddPastedLinesCommand.Execute(null);
        await Pump();
        return editor;
    }

    [AvaloniaFact]
    public async Task Settings_rows_and_long_switch_texts_are_gone()
    {
        using var ctx = await Round33UiTests.StartAsync(user: "admin");
        ctx.Shell.Navigate("settings");
        await Pump(20);
        var view = ctx.Window.FindDescendantOfType<SettingsView>()!;
        var texts = view.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.DoesNotContain("GST presentation", texts);
        Assert.DoesNotContain("Default packing & forwarding (₹)", texts);
        Assert.DoesNotContain("Title Case while typing", texts);
        Assert.Null(view.FindControl<ToggleSwitch>("TitleCaseSwitch"));
        foreach (var name in new[] { "ProformaSwitch", "WhatsAppSwitch", "ShowContactBox", "ShowEmailBox" })
        {
            var toggle = view.FindControl<ToggleSwitch>(name)!;
            Assert.Equal("On", toggle.OnContent);
            Assert.Equal("Off", toggle.OffContent);
        }
        Assert.DoesNotContain(view.GetLogicalDescendants().OfType<ToggleSwitch>(),
            t => t.OnContent is string on && (on.StartsWith("PAN/IT", StringComparison.Ordinal) || on == "GSTIN/UIN" || on.StartsWith("Contact :", StringComparison.Ordinal)));
    }

    [AvaloniaFact]
    public async Task Tally_and_zoho_seal_numbers_are_kept_apart_and_saved()
    {
        using var ctx = await Round33UiTests.StartAsync(user: "admin");
        ctx.Shell.Navigate("settings");
        await Pump(20);
        var vm = Assert.IsType<SettingsViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => vm.Settings is not null);
        Assert.True(vm.SealForTally);
        var tallyWidth = vm.SealWidth;
        vm.SealForZoho = true;
        Assert.Equal(tallyWidth, vm.SealWidth); // starts from the Tally numbers
        Assert.Equal("Preview on a Zoho page", vm.SealPreviewText);
        vm.SealWidth = 90;
        vm.SealOffsetX = 250;
        vm.SealMoveUp = 20;
        vm.SealForTally = true;
        Assert.Equal(tallyWidth, vm.SealWidth); // the Tally numbers are back
        vm.SealWidth = 170;
        vm.SealForZoho = true;
        Assert.Equal(90, vm.SealWidth);
        await vm.SaveCommand.ExecuteAsync(null);
        var company = (await ctx.Session.Api.SettingsAsync()).Company;
        Assert.Equal(170, company.SealWidth);
        Assert.Equal((90d, 250d, -20d), company.ZohoSeal());
    }

    [AvaloniaFact]
    public async Task A_changed_line_keeps_its_place_and_home_goes_to_the_product_box()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var editor = await WithTwoLinesAsync(ctx);
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        Assert.Null(view.FindControl<TextBlock>("ListPositionText")); // PG-1
        var product = view.FindControl<SearchPicker>("ProductBox")!;

        editor.EditLineCommand.Execute(editor.Lines[0]);
        Assert.Equal("Update", editor.EntryButtonText);
        editor.EntryQuantity = 4;
        editor.SaveLineCommand.Execute(null);
        await Pump();
        Assert.Equal(4, editor.Lines[0].Quantity);
        Assert.Same(editor.Lines[0], editor.SelectedLine);       // stays on the changed line
        Assert.False(product.InputBox.IsFocused);                 // not sent down to the Product box
        Press(ctx.Window, Key.Down);
        await Pump();
        Assert.Same(editor.Lines[1], editor.SelectedLine);       // ↓ goes on from it

        // Cancel (the button) also stays on the line
        editor.EditLineCommand.Execute(editor.Lines[1]);
        editor.CancelEntryCommand.Execute(null);
        await Pump();
        Assert.Null(editor.EditingLine);
        Assert.Same(editor.Lines[1], editor.SelectedLine);

        // Home: the Product box
        view.Focus();
        await Pump();
        Press(ctx.Window, Key.Home);
        await Pump();
        Assert.True(product.InputBox.IsFocused);
    }

    [AvaloniaFact]
    public async Task Changing_a_line_in_the_list_shows_update_and_cancel()
    {
        using var ctx = await Round33UiTests.StartAsync();
        ctx.Session.Config.EditLineInList = true;
        var editor = await WithTwoLinesAsync(ctx);
        editor.EditLineCommand.Execute(editor.Lines[0]);
        await Pump();
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        var update = view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "InlineUpdateButton");
        var cancel = view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "InlineCancelButton");
        Assert.Equal("Update", update.Content);
        Assert.Equal("Cancel", cancel.Content);
        editor.EntryQuantity = 3;
        update.Command!.Execute(null);
        await Pump();
        Assert.Equal(3, editor.Lines[0].Quantity);
        Assert.Same(editor.Lines[0], editor.SelectedLine);
    }
}
