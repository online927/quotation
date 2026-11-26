using Quotation.TestSupport;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Quotation.Mobile.ViewModels;
using Quotation.Mobile.Views;

namespace Quotation.Mobile.Tests;

/// <summary>Round 41 (Z8 phone): reading and adding internal notes on the phone, with 🎤.</summary>
public class Round41PhoneTests
{
    [AvaloniaFact]
    public async Task Notes_are_read_and_added_on_the_phone_when_switched_on()
    {
        using var ctx = await PhoneContext.StartAsync(signIn: false);
        var admin = await ctx.Server.LoginAsAdminAsync();
        var all = await admin.SettingsAsync();
        all.Quotation.InternalNotes = true;
        await admin.SaveSettingsAsync(all);
        var first = (await admin.QuotationsAsync()).First(q => q.Number is not null);
        await admin.AddNoteAsync(first.Id, "Call Mr. Rao on Monday");

        ctx.Main.Login.Username = "ravi";
        ctx.Main.Login.Password = "ravi-pass";
        await ctx.Main.Login.LoginCommand.ExecuteAsync(null);
        await Ui.WaitUntil(() => ctx.Main.Page is HomeViewModel { IsBusy: false });

        await ctx.Main.PushAsync(new QuotationDetailViewModel(ctx.Main, first.Id));
        var detail = Assert.IsType<QuotationDetailViewModel>(ctx.Main.Page);
        await Ui.WaitUntil(() => detail.Quotation is not null && !detail.IsBusy);
        Assert.True(detail.NotesOn);
        var note = Assert.Single(detail.Notes);
        Assert.Equal("Call Mr. Rao on Monday", note.Text);
        Assert.Equal("NOTES (1)", detail.NotesTitle);

        // 🎤 writes what is said into the box; Add saves it
        ctx.Phone.Speech.Enqueue("customer wants delivery by friday");
        await detail.SpeakNoteCommand.ExecuteAsync(null);
        Assert.Equal("Customer wants delivery by friday", detail.NewNoteText);
        await detail.AddNoteCommand.ExecuteAsync(null);
        Assert.Equal(2, detail.Notes.Count);
        Assert.Equal("", detail.NewNoteText);
        Assert.True(detail.Notes[1].Mine);
        Assert.Equal(2, (await admin.NotesAsync(first.Id)).Count);

        var view = ctx.Window.GetVisualDescendants().OfType<QuotationDetailView>().First();
        Assert.True(view.FindControl<Border>("NotesCard")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task No_notes_card_while_notes_are_off()
    {
        using var ctx = await PhoneContext.StartAsync();
        var first = (await (await ctx.Server.LoginAsAdminAsync()).QuotationsAsync()).First(q => q.Number is not null);
        await ctx.Main.PushAsync(new QuotationDetailViewModel(ctx.Main, first.Id));
        var detail = Assert.IsType<QuotationDetailViewModel>(ctx.Main.Page);
        await Ui.WaitUntil(() => detail.Quotation is not null && !detail.IsBusy);
        Assert.False(detail.NotesOn);
        Assert.Empty(detail.Notes);
    }
}
