using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Round 37: paste Qty / Disc % columns, bigger item rows, Settings only for the administrator.</summary>
public class Round37UiTests
{
    private static TextBox Inner(NumericUpDown box) => box.GetVisualDescendants().OfType<TextBox>().First();

    [AvaloniaFact]
    public async Task Columns_from_excel_fill_qty_and_discount_with_undo()
    {
        using var ctx = await Round33UiTests.StartAsync();
        var (editor, view) = await Round33UiTests.NewQuotationAsync(ctx);
        try
        {
            Assert.True(await editor.OpenPastedListAsync("187-901-10 x 1\n530-104 x 1\n2046S x 1"));
            await UiHelpers.WaitUntil(() => !editor.PasteStillSearching);
            editor.AddPastedLinesCommand.Execute(null);
            Assert.Equal(3, editor.Lines.Count);

            QuotationEditorView.PasteIntoColumn(editor, Inner(view.FindControl<NumericUpDown>("QtyBox")!), "2\r\n5\r\n10\r\n7\r\n", PasteColumnKind.Quantity);
            Assert.Equal([2m, 5m, 10m], editor.Lines.Select(l => l.Quantity));
            var notice = ctx.Shell.Toasts.Last();
            Assert.Equal("3 quantities pasted into Qty — lines 1 to 3", notice.Text);
            notice.Action!();
            Assert.Equal([1m, 1m, 1m], editor.Lines.Select(l => l.Quantity));

            QuotationEditorView.PasteIntoColumn(editor, Inner(view.FindControl<NumericUpDown>("DiscountBox")!), "5%\n10\n0", PasteColumnKind.Discount);
            Assert.Equal([5m, 10m, 0m], editor.Lines.Select(l => l.DiscountPercent));
            Assert.Equal("3 discounts pasted into Disc % — lines 1 to 3", ctx.Shell.Toasts.Last().Text);

            // Not a column (a discount over 100 %): nothing filled, pasted as usual
            Assert.Equal(0, editor.PasteColumn("150\n20", PasteColumnKind.Discount));
            Assert.Equal(0, editor.PasteColumn("0\n2", PasteColumnKind.Quantity));   // a quantity must be above 0
        }
        finally
        {
            editor.IsDirty = false;
        }
    }

    [AvaloniaFact]
    public async Task Item_rows_are_bigger_by_default_and_normal_when_chosen()
    {
        using var ctx = await Round33UiTests.StartAsync();
        Assert.True(new ClientConfig().BiggerItemRows);
        var (editor, view) = await Round33UiTests.NewQuotationAsync(ctx);
        Assert.Equal(ItemRowsLook.SmallText, view.FindControl<ItemsControl>("LinesList")!.Resources["FontSmall"]);
        editor.IsDirty = false;

        ctx.Shell.Navigate("mysettings");
        var my = Assert.IsType<MySettingsViewModel>(ctx.Shell.CurrentPage);
        my.BiggerItemRows = false;
        Assert.False(ctx.Session.Config.BiggerItemRows);
        var (normal, normalView) = await Round33UiTests.NewQuotationAsync(ctx);
        Assert.False(normalView.FindControl<ItemsControl>("LinesList")!.Resources.ContainsKey("FontSmall"));
        normal.IsDirty = false;
    }

    [AvaloniaFact]
    public async Task Settings_is_only_in_the_administrators_menu()
    {
        using (var user = await Round33UiTests.StartAsync())
        {
            Assert.DoesNotContain(user.Shell.NavItems, n => n.Key == "settings");
            Assert.Contains(user.Shell.NavItems, n => n.Key == "mysettings");
            user.Shell.Navigate("settings");                    // nothing happens
            Assert.IsNotType<SettingsViewModel>(user.Shell.CurrentPage);
        }
        using var admin = await Round33UiTests.StartAsync(user: "admin");
        Assert.Contains(admin.Shell.NavItems, n => n.Key == "settings");
    }
}
