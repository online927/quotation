using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Quotation.Contracts;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>
/// Round 39: the gear before the date (GR-1/GR-2), line keys (KL-1, K5, K2), notes (Z8), the description column paste (DX-1),
/// History F2 (K4), editing a line in the list (IE-1), drag dots (DG-1), switches instead of tick boxes (TB-1).
/// </summary>
public class Round39UiTests
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
    public async Task GST_HSN_and_per_are_hidden_on_the_screen_at_first_and_the_gear_before_the_date_shows_them()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var (editor, view) = await Round33UiTests.NewQuotationAsync(ctx);
        Assert.True(editor.HideGst && editor.HideHsn && editor.HidePer);
        Assert.False(editor.ShowPer);
        Assert.NotNull(view.FindControl<Button>("ViewGearButton"));
        editor.HidePer = false;
        Assert.False(ctx.Session.Config.HidePer);
        Assert.True(editor.ShowPer);
        // An old settings file is switched to "hidden" once
        var path = Path.Combine(Path.GetTempPath(), $"cfg-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{\"HideGst\":false,\"HideHsn\":false,\"HidePer\":false}");
        var loaded = ClientConfig.Load(path);
        Assert.True(loaded.HideGst && loaded.HideHsn && loaded.HidePer);
        loaded.HideGst = false;
        loaded.Save();
        Assert.False(ClientConfig.Load(path).HideGst); // and can be switched off again
        // The old switches are gone from the gear next to Items
        Assert.Null(view.FindControl<CheckBox>("ShowProductLinksBox"));
        Assert.DoesNotContain(view.GetLogicalDescendants().OfType<ToggleSwitch>(), t => t.Name is "StepUpSwitch" or "HideHsnGstSwitch");
    }

    [AvaloniaFact]
    public async Task Product_links_and_catalogues_are_each_users_own_choice()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var (editor, _) = await Round33UiTests.NewQuotationAsync(ctx);
        Assert.True(editor.ProductLinksMine && editor.AttachCataloguesMine);
        editor.AttachCataloguesMine = false;
        editor.ProductLinksMine = false;
        for (var i = 0; i < 50 && (await ctx.Session.Api.MeAsync()) is { AttachCatalogues: true } or { ProductLinksOnPdf: true }; i++) await Pump(2);
        var me = await ctx.Session.Api.MeAsync();
        Assert.False(me.AttachCatalogues);
        Assert.False(me.ProductLinksOnPdf);
        Assert.False(editor.ProductLinksMine);
    }

    [AvaloniaFact]
    public async Task Arrows_pick_a_line_Enter_edits_it_Alt_D_removes_it_with_undo_and_Esc_lets_go()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var editor = await WithTwoLinesAsync(ctx);
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        view.Focus();
        await Pump();
        Press(ctx.Window, Key.Down);
        await Pump();
        Assert.Same(editor.Lines[0], editor.SelectedLine);
        Assert.True(editor.Lines[0].IsHighlighted);
        Press(ctx.Window, Key.Down);
        await Pump();
        Assert.Same(editor.Lines[1], editor.SelectedLine);

        var second = editor.Lines[1];
        Press(ctx.Window, Key.D, RawInputModifiers.Alt);
        await Pump();
        Assert.Single(editor.Lines);
        Assert.DoesNotContain(second, editor.Lines);
        ctx.Shell.Toasts.Single(t => t.Text.StartsWith("Line 2 removed", StringComparison.Ordinal)).Action!();
        Assert.Equal(2, editor.Lines.Count);

        view.Focus();
        editor.SelectedLine = editor.Lines[0];
        Press(ctx.Window, Key.Enter);
        await Pump();
        Assert.Same(editor.Lines[0], editor.EditingLine);
        editor.CancelEntryCommand.Execute(null);
        view.Focus();
        editor.SelectedLine = editor.Lines[0];
        Press(ctx.Window, Key.Escape);
        await Pump();
        Assert.Null(editor.SelectedLine);
        Assert.IsType<QuotationEditorViewModel>(ctx.Shell.CurrentPage); // the first Esc did not go Back
    }

    [AvaloniaFact]
    public async Task Enter_on_an_empty_product_box_goes_on_to_the_packing_box()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var editor = await WithTwoLinesAsync(ctx);
        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        var product = view.FindControl<Quotation.Desktop.Controls.SearchPicker>("ProductBox")!;
        product.InputBox.Focus();
        await Pump();
        Press(ctx.Window, Key.Enter);
        await Pump();
        var packing = view.FindControl<NumericUpDown>("PackingBox")!;
        Assert.True(packing.IsKeyboardFocusWithin || view.FindControl<Button>("ApproveButton")!.IsFocused);
        Assert.Equal(2, editor.Lines.Count);
    }

    [AvaloniaFact]
    public async Task Notes_card_adds_a_note_when_switched_on()
    {
        using var ctx = await Round33UiTests.StartAsync(setUp: s => { });
        var admin = await ctx.Server.LoginAsAdminAsync();
        var all = await admin.SettingsAsync();
        all.Quotation.InternalNotes = true;
        await admin.SaveSettingsAsync(all);
        var editor = await WithTwoLinesAsync(ctx);
        Assert.True(await editor.SaveAsync());
        await UiHelpers.WaitUntil(() => editor.NotesOn && editor.CanWriteNotes);
        ctx.Window.FindDescendantOfType<QuotationEditorView>()!.Focus();
        Press(ctx.Window, Key.N, RawInputModifiers.Alt);
        await Pump();
        Assert.True(editor.NotesOpen);
        editor.NewNoteText = "Call Mr. Rao on Monday";
        await editor.AddNoteCommand.ExecuteAsync(null);
        Assert.Single(editor.Notes);
        Assert.True(editor.Notes[0].Mine);
        Assert.Equal("", editor.NewNoteText);
        var row = (await ctx.Session.Api.QuotationsAsync()).Single(s => s.Id == editor.Id);
        Assert.Equal(1, row.NoteCount);
    }

    [AvaloniaFact]
    public async Task A_description_column_from_Excel_goes_one_cell_per_line_with_undo()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var editor = await WithTwoLinesAsync(ctx);
        Assert.True(QuotationEditorView.IsSpreadsheet(["Text", "XML Spreadsheet"]));
        Assert.False(QuotationEditorView.IsSpreadsheet(["Text", "UnicodeText"]));
        Assert.True(QuotationEditorView.PasteDescriptions(editor, "With magnifier\r\n\"Carbide jaws\nWith case\"\r\n"));
        Assert.Equal("With magnifier", editor.Lines[0].Description);
        Assert.Equal("Carbide jaws\nWith case", editor.Lines[1].Description);
        ctx.Shell.Toasts.Single(t => t.Text.StartsWith("Descriptions pasted into lines 1–2", StringComparison.Ordinal)).Action!();
        Assert.NotEqual("With magnifier", editor.Lines[0].Description);
        Assert.False(QuotationEditorView.PasteDescriptions(editor, "Only one cell"));
    }

    [AvaloniaFact]
    public async Task F2_in_history_opens_the_date_filter_and_a_day_filters_the_list()
    {
        using var ctx = await Round33UiTests.StartAsync();
        ctx.Shell.Navigate("history");
        var list = Assert.IsType<QuotationListViewModel>(ctx.Shell.CurrentPage);
        await Pump();
        var view = ctx.Window.FindDescendantOfType<QuotationListView>()!;
        var picker = view.FindControl<CalendarDatePicker>("DateFilterBox")!;
        Assert.True(list.ShowDateFilter);
        Press(ctx.Window, Key.F2);
        await Pump();
        Assert.True(picker.IsDropDownOpen);
        list.DateFilter = DateTime.Today.AddDays(-400);
        await UiHelpers.WaitUntil(() => !list.IsBusy);
        Assert.Empty(list.Rows);
        Assert.False(ctx.Shell.ShowGlobalSearch); // the page has its own search
    }

    [AvaloniaFact]
    public async Task A_line_can_be_changed_in_the_list_itself()
    {
        using var ctx = await Round33UiTests.StartAsync();
        ctx.Session.Config.EditLineInList = true;
        var editor = await WithTwoLinesAsync(ctx);
        Assert.True(editor.ShowLineEntry);
        editor.EditLineCommand.Execute(editor.Lines[0]);
        await Pump();
        Assert.True(editor.InListEditing);
        Assert.True(editor.Lines[0].IsInline);
        Assert.False(editor.ShowLineEntry);   // the boxes below are only for new lines
        editor.EntryQuantity = 5;
        editor.SaveLineCommand.Execute(null);
        await Pump();
        Assert.Equal(5, editor.Lines[0].Quantity);
        Assert.False(editor.Lines[0].IsInline);

        // Change the item: the typed values stay
        editor.EditLineCommand.Execute(editor.Lines[0]);
        var other = (await ctx.Session.Api.SearchProductsAsync("530-104")).First().Product;
        editor.EntryRate = 1234;
        editor.ChangeEditingItem(other);
        editor.SaveLineCommand.Execute(null);
        await Pump();
        Assert.Equal(other.Name, editor.Lines[0].ItemName);
        Assert.Equal(5, editor.Lines[0].Quantity);
        Assert.Equal(1234, editor.Lines[0].Rate);
    }

    [AvaloniaFact]
    public async Task Settings_use_switches_and_the_removed_rows_are_gone()
    {
        using var ctx = await Round33UiTests.StartAsync(user: "admin");
        ctx.Shell.Navigate("settings");
        await Pump(20);
        var view = ctx.Window.FindDescendantOfType<SettingsView>()!;
        Assert.Empty(view.GetLogicalDescendants().OfType<CheckBox>());
        Assert.NotNull(view.FindControl<ToggleSwitch>("InternalNotesSwitch"));
        Assert.Null(view.FindControl<ToggleSwitch>("ProductLinksOnPdfBox"));
        Assert.DoesNotContain(view.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Fit on one page");
        ctx.Shell.Navigate("mysettings");
        await Pump(20);
        var mine = ctx.Window.FindDescendantOfType<MySettingsView>()!;
        Assert.NotNull(mine.FindControl<RadioButton>("EditInListRadio"));
        Assert.DoesNotContain(mine.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("Only for you", StringComparison.Ordinal) == true);
    }
}
