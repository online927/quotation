using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Core.Domain;

namespace Quotation.Mobile.ViewModels;

/// <summary>Today · History · Drafts, with the same search as the PC (number, customer, GSTIN, item).</summary>
public sealed partial class QuotationsViewModel(MainViewModel main) : PageViewModel
{
    public const string Today = "today", History = "history", Drafts = "drafts";
    private CancellationTokenSource? _typing;

    public override string Title => "Quotations";
    public ObservableCollection<QuotationRow> Items { get; } = [];
    public string Initial => (main.Session.User?.DisplayName is { Length: > 0 } n ? n[..1] : "?").ToUpperInvariant();
    public bool MakesDocuments => main.Session.User?.MakesDocuments == true;

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _list = Today;
    [ObservableProperty] private bool _isEmpty;

    public bool IsToday { get => List == Today; set { if (value) _ = ChooseAsync(Today); } }
    public bool IsHistory { get => List == History; set { if (value) _ = ChooseAsync(History); } }
    public bool IsDrafts { get => List == Drafts; set { if (value) _ = ChooseAsync(Drafts); } }

    public async Task ChooseAsync(string list)
    {
        if (List == list && Items.Count > 0) return;
        List = list;
        OnPropertyChanged(nameof(IsToday));
        OnPropertyChanged(nameof(IsHistory));
        OnPropertyChanged(nameof(IsDrafts));
        await LoadAsync();
    }

    /// <summary>Typing in the search box: the list follows after a short pause.</summary>
    partial void OnSearchChanged(string value)
    {
        _typing?.Cancel();
        var cts = _typing = new CancellationTokenSource();
        _ = Task.Delay(TypingPause, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = LoadAsync());
        }, TaskScheduler.Default);
    }

    public static TimeSpan TypingPause { get; set; } = TimeSpan.FromMilliseconds(350);

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var q = Search.Trim();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var found = List switch
        {
            Drafts => await main.Session.Api.QuotationsAsync(q.Length > 0 ? q : null, QuotationStatus.Draft, take: 100),
            // A search on Today looks through everything (as on the PC: a number from last week is still found).
            Today when q.Length == 0 => await main.Session.Api.QuotationsAsync(null, from: today, to: today, take: 200),
            _ => await main.Session.Api.QuotationsAsync(q.Length > 0 ? q : null, take: 100),
        };
        Items.Clear();
        foreach (var s in found) Items.Add(new QuotationRow(s));
        IsEmpty = Items.Count == 0;
    });

    [RelayCommand]
    private Task OpenAsync(QuotationRow? row) => row is null ? Task.CompletedTask : main.PushAsync(new QuotationDetailViewModel(main, row.Id));

    [RelayCommand]
    private Task NewFromMessageAsync() => main.PushAsync(new MessageViewModel(main));

    [RelayCommand]
    private Task MoreAsync() => main.ShowTabAsync(Tabs.More);
}
