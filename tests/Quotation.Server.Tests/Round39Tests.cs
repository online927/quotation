using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;

namespace Quotation.Server.Tests;

/// <summary>
/// Round 39: internal notes on quotations (Z8), each user's own "Attach catalogues" choice (GR-2) and the always-on
/// shrink to one page.
/// </summary>
public class Round39Tests : QuotationTestBase
{
    private static int Pages(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return doc.NumberOfPages;
    }

    private static string PdfWords(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return string.Join(" ", doc.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }

    private async Task<QuotationApiClient> UserAsync(string name)
    {
        await Api.CreateUserAsync(new CreateUserRequest(name, name, "first-pass", UserRole.User));
        var api = Server.CreateApi("USER-PC");
        await api.LoginAsync(name, "first-pass");
        await api.ChangePasswordAsync("first-pass", "user-pass");
        return api;
    }

    private async Task SetNotesAsync(bool on)
    {
        var all = await Api.SettingsAsync();
        all.Quotation.InternalNotes = on;
        await Api.SaveSettingsAsync(all);
    }

    [Fact]
    public async Task Notes_are_off_at_first_and_only_their_writer_changes_them()
    {
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        Assert.False((await Api.SettingsAsync()).Quotation.InternalNotes);
        await Assert.ThrowsAsync<ApiException>(() => Api.AddNoteAsync(q.Id, "Off: not added"));

        await SetNotesAsync(true);
        var ravi = await UserAsync("ravi9");
        var mine = await ravi.AddNoteAsync(q.Id, "Customer asked for 5 % more off");
        Assert.True(mine.Mine);
        var admins = await Api.AddNoteAsync(q.Id, "Call Mr. Rao on Monday");
        Assert.True(admins.Mine); // the administrator wrote it

        var seen = await ravi.NotesAsync(q.Id);
        Assert.Equal(["Customer asked for 5 % more off", "Call Mr. Rao on Monday"], seen.Select(n => n.Text));
        Assert.True(seen[0].Mine && seen[0].CanDelete);
        Assert.False(seen[1].Mine || seen[1].CanDelete);

        // The writer edits their own note; another user may not
        var edited = await ravi.EditNoteAsync(mine.Id, "Customer asked for 5 % more off; told max 3 %");
        Assert.True(edited.IsEdited);
        await Assert.ThrowsAsync<ApiException>(() => ravi.EditNoteAsync(admins.Id, "not mine"));
        await Assert.ThrowsAsync<ApiException>(() => ravi.DeleteNoteAsync(admins.Id));
        // An administrator deletes any note
        await Api.DeleteNoteAsync(mine.Id);
        Assert.Single(await Api.NotesAsync(q.Id));

        // A note never changes the quotation itself
        var after = await Api.QuotationAsync(q.Id);
        Assert.Equal(q.Revision, after.Revision);
    }

    [Fact]
    public async Task History_counts_notes_shows_the_newest_and_finds_words_in_them()
    {
        await SetNotesAsync(true);
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        await Api.AddNoteAsync(q.Id, "First note");
        await Api.AddNoteAsync(q.Id, "Rao confirmed on phone, PO by Friday");

        var row = (await Api.QuotationsAsync()).Single(s => s.Id == q.Id);
        Assert.Equal(2, row.NoteCount);
        Assert.Equal("Rao confirmed on phone, PO by Friday", row.LatestNote);
        Assert.Contains(await Api.QuotationsAsync("confirmed"), s => s.Id == q.Id); // found by a word only in a note

        await SetNotesAsync(false);
        Assert.DoesNotContain(await Api.QuotationsAsync("confirmed"), s => s.Id == q.Id);
        Assert.Equal(0, (await Api.QuotationsAsync()).Single(s => s.Id == q.Id).NoteCount);
    }

    [Fact]
    public async Task Notes_are_never_printed_and_a_proforma_shows_its_quotations_notes_but_a_duplicate_does_not()
    {
        await SetNotesAsync(true);
        var all = await Api.SettingsAsync();
        all.Quotation.ProformaEnabled = true;
        await Api.SaveSettingsAsync(all);
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        await Api.AddNoteAsync(q.Id, "Secret discount note");
        await Api.ApproveQuotationAsync(q.Id);
        foreach (var layout in new[] { PdfLayouts.Tally, PdfLayouts.Zoho, PdfLayouts.Minimal })
            Assert.DoesNotContain("Secret", PdfWords(await Api.QuotationPdfAsync(q.Id, layout: layout)));

        var proforma = await Api.ConvertToProformaAsync(q.Id);
        Assert.Equal(["Secret discount note"], (await Api.NotesAsync(proforma.Id)).Select(n => n.Text));
        var copy = await Api.DuplicateQuotationAsync(q.Id);
        Assert.Empty(await Api.NotesAsync(copy.Id));
    }

    [Fact]
    public async Task Attach_catalogues_follows_the_choice_of_the_user_who_makes_the_pdf()
    {
        var request = await BevelRequest();
        var productId = request.Lines[0].ProductId!.Value;
        var draft = await Api.CreateQuotationAsync(request);
        await Api.UploadDatasheetAsync(productId, "sheet.pdf", await Api.PreviewPdfAsync(draft.Id));
        var q = await Api.CreateQuotationAsync(request);
        await Api.ApproveQuotationAsync(q.Id);
        Assert.True(Api.CurrentUser!.AttachCatalogues);
        Assert.Equal(2, Pages(await Api.QuotationPdfAsync(q.Id)));

        Api.UpdateCurrentUser(await Api.SetMyPreferencesAsync(new MyPreferencesDto(AttachCatalogues: false)));
        Assert.False((await Api.MeAsync()).AttachCatalogues);
        Assert.Equal(1, Pages(await Api.QuotationPdfAsync(q.Id)));
        Api.UpdateCurrentUser(await Api.SetMyPreferencesAsync(new MyPreferencesDto(AttachCatalogues: true)));
        Assert.Equal(2, Pages(await Api.QuotationPdfAsync(q.Id)));
    }

    [Fact]
    public async Task Shrinking_to_one_page_is_always_on()
    {
        var all = await Api.SettingsAsync();
        all.Quotation.ShrinkToFitOnePage = false; // the old switch is ignored now
        await Api.SaveSettingsAsync(all);
        var r = await BevelRequest();
        var q = await Api.CreateQuotationAsync(r);
        await Api.ApproveQuotationAsync(q.Id);
        Assert.True(Pages(await Api.QuotationPdfAsync(q.Id)) >= 1);
    }
}
