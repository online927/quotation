using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.Contracts;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>Round 42 (DB-1): the new dashboard: heading, periods, top customers by value / times quoted, team, activity.</summary>
public class Round42UiTests
{
    private static async Task Pump(int rounds = 10)
    {
        for (var i = 0; i < rounds; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20); }
    }

    [AvaloniaFact]
    public async Task The_dashboard_shows_the_periods_numbers_top_customers_and_activity()
    {
        using var ctx = await Round33UiTests.StartAsync(user: "admin");
        var api = ctx.Server.CreateApi();
        await api.LoginAsync("ravi", "ravi-pass");
        var customers = await api.SearchCustomersAsync("", 3);
        var product = (await api.SearchProductsAsync("caliper", 1))[0].Product;
        for (var i = 0; i < 3; i++)
        {
            var q = await api.CreateQuotationAsync(new SaveQuotationRequest
            {
                Date = DateOnly.FromDateTime(DateTime.Today), CustomerId = customers[i % 2].Customer.Id,
                Lines = [new QuotationLineDto { ProductId = product.Id, Quantity = 1 + i, Rate = 1000 * (i + 1), TallyRate = product.Rate }],
            });
            await api.ApproveQuotationAsync(q.Id, revision: q.Revision);
        }
        ctx.Shell.Navigate("history");
        ctx.Shell.Navigate("dashboard");
        var dash = Assert.IsType<DashboardViewModel>(ctx.Shell.CurrentPage);
        await UiHelpers.WaitUntil(() => dash.Stats is { Count: 3 });
        await Pump(20);
        var view = ctx.Window.FindDescendantOfType<DashboardView>()!;
        Assert.Equal("Dashboard", view.FindControl<TextBlock>("DashboardTitle")!.Text);
        Assert.DoesNotContain("Good morning", dash.Summary);
        Assert.StartsWith("Quoted ₹", dash.Summary);
        Assert.Equal("3", dash.CountText);
        Assert.Equal(2, dash.TopCustomers.Count);
        Assert.Equal(1, dash.TopCustomers[0].Rank);
        Assert.True(dash.TopCustomers[0].Gold);
        Assert.Single(dash.TopItems);
        Assert.Equal("3×", dash.TopItems[0].Value);
        Assert.Single(dash.Team);
        Assert.Equal(7 * dash.HeatColumns, dash.Heat.Count);
        Assert.Contains(dash.Heat, c => c.Date == DateOnly.FromDateTime(DateTime.Today) && c.Count == 3);
        Assert.False(dash.ShowOutcomes); // Won / Lost is off

        // Times quoted: the customer with two quotations first
        dash.CustomersByCount = true;
        Assert.Equal("2×", dash.TopCustomers[0].Value);
        Assert.Contains("times quoted", dash.CustomersSubtitle);

        // Another period reloads the numbers
        dash.PeriodToday = true;
        await UiHelpers.WaitUntil(() => dash.Stats?.Period == "today");
        Assert.Equal("Quoted today", dash.ValueLabel);
        Assert.Equal(3, dash.Stats!.Count);
        Assert.True(view.FindControl<RadioButton>("PeriodTodayRadio")!.IsChecked);
    }
}
