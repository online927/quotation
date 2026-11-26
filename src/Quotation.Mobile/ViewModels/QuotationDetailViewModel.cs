using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Desktop.Services;
using Quotation.Core.Calculation;
using Quotation.Core.Domain;

namespace Quotation.Mobile.ViewModels;

/// <summary>One quotation: customer, items and total; Preview (PDF), WhatsApp, Share; Approve on a draft.</summary>
public sealed partial class QuotationDetailViewModel(MainViewModel main, Guid id) : PageViewModel
{
    public Guid Id { get; } = id;
    public override string Title => Quotation?.Number ?? "Draft";
    public override bool HasBack => true;

    [ObservableProperty] private QuotationDto? _quotation;
    public ObservableCollection<LineRow> Lines { get; } = [];
    public ObservableCollection<TotalRow> Totals { get; } = [];

    public string Subtitle => Quotation is { } q ? $"{q.Date:dd-MMM-yyyy} · by {q.CreatedBy}" : "";
    public string StatusText => Quotation is { } q ? QuotationRow.Status(q.Status, q.DocType) : "";
    public bool IsOk => Quotation is { Status: QuotationStatus.Approved or QuotationStatus.Generated, DocType: DocumentType.Quotation };
    public bool IsWarn => Quotation is { DocType: DocumentType.ProformaInvoice } or { Status: QuotationStatus.PendingReview };
    public bool IsBad => Quotation is { Status: QuotationStatus.Cancelled };
    public string CustomerName => Quotation?.Buyer.Name is { Length: > 0 } n ? n : "(no customer yet)";
    public string CustomerLine => Quotation is { } q
        ? string.Join(" · ", new[] { q.Buyer.Address.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "", q.Buyer.Gstin }
            .Where(s => s.Length > 0))
        : "";
    public string ItemsTitle => $"ITEMS ({Lines.Count})";
    public string GrandTotal => Quotation is { } q ? Currencies.Money(q.GrandTotal, q.Currency) : "";
    public string AmountInWords => Quotation?.AmountInWords ?? "";
    /// <summary>A draft (or one to review) of a user who makes quotations: Approve gives it the next number.</summary>
    public bool CanApprove => Quotation is { Status: QuotationStatus.Draft or QuotationStatus.PendingReview } && main.Session.User?.MakesDocuments == true;
    public bool HasCustomerPhone => (Quotation?.Buyer.Phone ?? "").Any(char.IsDigit);

    public override Task LoadAsync() => RunAsync(async () =>
    {
        Show(await main.Session.Api.QuotationAsync(Id));
        await LoadNotesAsync();
    });

    // ================================================================= Round 41 (Z8 phone): internal notes

    /// <summary>Notes are on (Settings → Features "Notes"): the card shows; they are never printed or shared.</summary>
    public bool NotesOn => main.Session.Company?.Quotation.InternalNotes == true;
    public ObservableCollection<NoteRow> Notes { get; } = [];
    public string NotesTitle => Notes.Count == 0 ? "NOTES" : $"NOTES ({Notes.Count})";
    public bool HasNoNotes => Notes.Count == 0;
    [ObservableProperty] private string _newNoteText = "";
    [ObservableProperty] private bool _isListeningNote;

    private async Task LoadNotesAsync()
    {
        Notes.Clear();
        if (NotesOn)
            foreach (var n in await main.Session.Api.NotesAsync(Id)) Notes.Add(new NoteRow(n));
        NotesChanged();
    }

    private void NotesChanged()
    {
        OnPropertyChanged(nameof(NotesOn));
        OnPropertyChanged(nameof(NotesTitle));
        OnPropertyChanged(nameof(HasNoNotes));
    }

    /// <summary>Adds the note typed (or spoken); the quotation itself does not change.</summary>
    [RelayCommand]
    private Task AddNoteAsync() => !NotesOn || NewNoteText.Trim().Length == 0 ? Task.CompletedTask : RunAsync(async () =>
    {
        var added = await main.Session.Api.AddNoteAsync(Id, NewNoteText.Trim());
        Notes.Add(new NoteRow(added));
        NewNoteText = "";
        NotesChanged();
    });

    /// <summary>🎤: what is said is written into the note box (add it with Add note).</summary>
    [RelayCommand]
    private async Task SpeakNoteAsync()
    {
        if (IsListeningNote) return;
        IsListeningNote = true;
        try
        {
            var said = await main.Session.Phone.ListenAsync();
            if (!string.IsNullOrWhiteSpace(said))
            {
                var text = said.Trim();
                text = char.ToUpperInvariant(text[0]) + text[1..];
                NewNoteText = NewNoteText.Trim().Length == 0 ? text : NewNoteText.TrimEnd() + " " + text;
            }
        }
        finally
        {
            IsListeningNote = false;
        }
    }

    private void Show(QuotationDto q)
    {
        Quotation = q;
        Lines.Clear();
        foreach (var l in q.Lines) Lines.Add(new LineRow(l, q.IsUsd));
        Totals.Clear();
        if (q.PackingForwarding > 0) Totals.Add(new TotalRow("Packing & Forwarding", IndianFormat.Amount(q.PackingForwarding)));
        foreach (var t in q.Taxes) Totals.Add(new TotalRow($"{t.Name} {t.RatePercent:0.##}%", IndianFormat.Amount(t.Amount)));
        if (q.RoundOff != 0) Totals.Add(new TotalRow("Round off", IndianFormat.Amount(q.RoundOff)));
        foreach (var name in new[] { nameof(Title), nameof(Subtitle), nameof(StatusText), nameof(IsOk), nameof(IsWarn), nameof(IsBad), nameof(CustomerName),
                     nameof(CustomerLine), nameof(ItemsTitle), nameof(GrandTotal), nameof(AmountInWords), nameof(CanApprove), nameof(HasCustomerPhone) })
            OnPropertyChanged(name);
    }

    /// <summary>The PDF in the phone's files: the quotation's own PDF, or a preview of a draft.</summary>
    private async Task<string> PdfAsync()
    {
        var q = Quotation!;
        if (q.HasPdf) return await main.Session.SavePdfAsync(q);
        var bytes = await main.Session.Api.PreviewPdfAsync(q.Id);
        Directory.CreateDirectory(main.Session.Phone.FilesFolder);
        var path = Path.Combine(main.Session.Phone.FilesFolder, Services.PhoneSession.PdfFileName(q));
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    [RelayCommand]
    private Task PreviewAsync() => Quotation is null ? Task.CompletedTask : RunAsync(async () => main.Session.Phone.OpenPdf(await PdfAsync()));

    [RelayCommand]
    private Task WhatsAppAsync() => Quotation is null ? Task.CompletedTask
        : RunAsync(async () => main.Session.Phone.SharePdf(await PdfAsync(), Services.PhoneSession.PdfFileName(Quotation!), whatsApp: true));

    [RelayCommand]
    private Task ShareAsync() => Quotation is null ? Task.CompletedTask
        : RunAsync(async () => main.Session.Phone.SharePdf(await PdfAsync(), Services.PhoneSession.PdfFileName(Quotation!), whatsApp: false));

    [RelayCommand]
    private Task ApproveAsync() => !CanApprove ? Task.CompletedTask : RunAsync(async () =>
    {
        if (ThemeService.Current == AppTheme.Tally && !await main.AskAsync("Approve ?", "Yes or No")) return; // like Tally's "Accept?"
        var approved = await main.Session.Api.ApproveQuotationAsync(Quotation!.Id, revision: Quotation.Revision);
        Show(approved);
        InfoMessage = $"Approved as {approved.Number}.";
    });

    [RelayCommand]
    private void CallCustomer()
    {
        if (HasCustomerPhone) main.Session.Phone.Dial(Services.PhoneNumbers.First(Quotation!.Buyer.Phone));
    }
}
