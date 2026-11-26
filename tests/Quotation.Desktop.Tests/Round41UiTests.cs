using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.Desktop.Controls;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Round 41: Settings menu names and coloured icons (RD-5b), a delivery column pasted from Excel (PD-2).</summary>
public class Round41UiTests
{
    private static async Task Pump(int rounds = 10)
    {
        for (var i = 0; i < rounds; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20); }
    }

    [AvaloniaFact]
    public async Task Settings_menu_names_are_plain_text_with_coloured_icons()
    {
        using var ctx = await Round33UiTests.StartAsync(user: "admin");
        ctx.Shell.Navigate("settings");
        await Pump(20);
        var view = ctx.Window.FindDescendantOfType<SettingsView>()!;
        var names = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("tabname")).ToList();
        Assert.Contains(names, t => t.Text == "Company");
        var muted = (ISolidColorBrush)view.FindResource("MutedBrush")!;
        var plain = names.First(t => t.Text == "Tally"); // not the chosen section
        Assert.NotEqual(muted.Color, ((ISolidColorBrush)plain.Foreground!).Color);
        var icons = view.GetVisualDescendants().OfType<StrokeIcon>().Where(i => i.FindAncestorOfType<TabItem>() is not null).ToList();
        var colours = icons.Select(i => ((ISolidColorBrush)i.Foreground!).Color).Distinct().Count();
        Assert.True(colours >= 8); // each section its own colour
        Assert.IsType<SolidColorBrush>(SettingsTabColor.Instance.Convert("PDF", typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [AvaloniaFact]
    public async Task A_delivery_column_from_excel_fills_the_lines_with_undo()
    {
        using var ctx = await Round33UiTests.StartAsync();
        ctx.Session.Config.DeliveryTimeOnItems = true;
        var (editor, _) = await Round33UiTests.NewQuotationAsync(ctx);
        Assert.True(await editor.OpenPastedListAsync("Part No\tQty\tDelivery\n187-901-10\t1\tEx-stock\n530-104\t2\t2-3 weeks"));
        await UiHelpers.WaitUntil(() => !editor.PasteStillSearching);
        editor.AddPastedLinesCommand.Execute(null);
        await Pump();
        Assert.Equal(["Ex-stock", "2-3 weeks"], editor.Lines.Select(l => l.DueOn));

        var view = ctx.Window.FindDescendantOfType<QuotationEditorView>()!;
        var box = view.FindControl<TextBox>("DeliveryBox")!;
        QuotationEditorView.PasteDelivery(editor, box, "1 week\r\n\r\n");
        Assert.Equal("1 week", box.Text); // one cell: a normal paste
        Assert.Equal("Ex-stock", editor.Lines[0].DueOn);
        QuotationEditorView.PasteDelivery(editor, box, "4 weeks\r\nEx-stock\r\n");
        Assert.Equal(["4 weeks", "Ex-stock"], editor.Lines.Select(l => l.DueOn));
        ctx.Shell.Toasts.Single(t => t.Text.StartsWith("2 deliveries pasted into Delivery", StringComparison.Ordinal)).Action!();
        Assert.Equal(["Ex-stock", "2-3 weeks"], editor.Lines.Select(l => l.DueOn));
    }
}
