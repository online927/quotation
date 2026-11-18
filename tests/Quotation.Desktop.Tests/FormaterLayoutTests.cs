using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>The Formater page as it is used: the typing box in its card, typed text read after the usual pause (every theme).</summary>
public class FormaterLayoutTests
{
    [AvaloniaFact]
    public async Task The_typing_box_is_in_its_card_and_typed_text_is_read()
    {
        var delay = FormaterViewModel.RefreshDelay;
        FormaterViewModel.RefreshDelay = TimeSpan.FromMilliseconds(350); // as in the app
        var server = new TestServer(products: 100, customers: 60, today: DateOnly.FromDateTime(DateTime.Today)) { AdministratorsMakeDocuments = false };
        try
        {
            var admin = await server.LoginAsAdminAsync();
            await admin.StartSyncAsync(SyncKind.Full, wait: true);
            await server.LoginAsStandardAsync("ravi");
            var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
            var session = new AppSession(config, _ => server.CreateApi()) { Documents = new RecordingLauncher(config) };
            await session.Api.LoginAsync("ravi", "ravi-pass");
            using var shell = new ShellViewModel(session, () => { });
            var window = new MainWindow { DataContext = new MainWindowViewModel(session) { Content = shell }, Width = 1366, Height = 768 };
            window.Show();
            await UiHelpers.WaitUntil(() => shell.CurrentPage is DashboardViewModel && session.Settings is not null);
            foreach (var theme in new[] { AppTheme.Light, AppTheme.Tally })
            {
                await session.SetThemeAsync(theme);
                shell.Navigate("formater");
                await UiHelpers.WaitUntil(() => shell.CurrentPage is FormaterViewModel { NumberText.Length: > 0 });
                var vm = (FormaterViewModel)shell.CurrentPage!;
                Dispatcher.UIThread.RunJobs();
                var view = window.FindDescendantOfType<FormaterView>()!;
                var box = view.FindControl<TextBox>("FormaterTextBox")!;
                var card = box.FindAncestorOfType<Border>(includeSelf: false)!;
                while (!card.Classes.Contains("card")) card = card.FindAncestorOfType<Border>()!;
                var customerCard = view.FindControl<Border>("CustomerCard")!;
                var boxRect = new Rect(box.TranslatePoint(new Point(), window)!.Value, box.Bounds.Size);
                var cardRect = new Rect(card.TranslatePoint(new Point(), window)!.Value, card.Bounds.Size);
                var customerLeft = customerCard.TranslatePoint(new Point(), window)!.Value.X;
                Assert.True(boxRect.Width > 250 && boxRect.Height > 250, $"{theme}: typing box is {boxRect.Size}");
                Assert.True(cardRect.Contains(boxRect), $"{theme}: typing box {boxRect} outside its card {cardRect}");
                Assert.True(boxRect.Right < customerLeft, $"{theme}: typing box runs under the Customer card");

                // Typed like a user (click, type, the usual pause): the customer and items are read.
                var at = box.TranslatePoint(new Point(60, 30), window)!.Value;
                window.MouseDown(at, MouseButton.Left);
                window.MouseUp(at, MouseButton.Left);
                window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
                window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
                foreach (var line in new[] { "Sonepar India Private Limited", "", "187-901-10, 2, 15600", "", "530-104, 5, 3250, 5" })
                {
                    if (line.Length > 0) window.KeyTextInput(line);
                    window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                }
                await UiHelpers.WaitUntil(() => vm.Lines.Count == 2 && !vm.IsRefreshing, 15000);
                Assert.True(vm.Lines.All(l => l.IsTally));
                Assert.Equal("Sonepar India Private Limited", vm.CustomerName);
            }
            window.Close();
        }
        finally
        {
            FormaterViewModel.RefreshDelay = delay;
            ThemeService.Apply(AppTheme.Light);
            server.Dispose();
        }
    }
}
