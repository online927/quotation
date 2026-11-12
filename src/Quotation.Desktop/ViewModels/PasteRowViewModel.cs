using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Quotation.Contracts;

namespace Quotation.Desktop.ViewModels;

public enum PasteRowState { Searching, Found, Choose, NotFound }

/// <summary>One line of a pasted list: what was pasted, the item found for it (or the items to choose from) and the quantity.</summary>
public sealed partial class PasteRowViewModel : ObservableObject
{
    /// <summary>The best item must score at least this multiple of the next one to be taken without asking.</summary>
    public const double DecisiveMargin = 1.6;

    public required int No { get; init; }
    public required string Raw { get; init; }
    public required string Query { get; init; }

    [ObservableProperty] private decimal? _quantity;
    [ObservableProperty] private PasteRowState _state = PasteRowState.Searching;
    [ObservableProperty] private ProductSearchHitDto? _selected;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _chosenByUser;
    [ObservableProperty] private string _searchText = "";

    public ObservableCollection<ProductSearchHitDto> Candidates { get; } = [];

    /// <summary>Called whenever the row changes in a way that affects the counts and the Add button.</summary>
    public Action? Changed { get; set; }

    public bool IsFound => State == PasteRowState.Found;
    public bool IsChoose => State == PasteRowState.Choose;
    public bool IsNotFound => State == PasteRowState.NotFound;
    public bool IsLoading => State == PasteRowState.Searching;
    public bool ShowsName => IsFound;
    public bool ShowsChoices => IsChoose;
    public bool ShowsNothingFound => IsNotFound && !IsSearching;
    public bool ShowsSearchButton => IsNotFound && !IsSearching;
    public bool ShowsSearchBox => IsNotFound && IsSearching;
    public bool IsReady => Selected is not null && Quantity is > 0 && State is PasteRowState.Found or PasteRowState.Choose;

    public string ItemName => Selected?.Product.Name ?? "";

    public string ItemDetail => State switch
    {
        PasteRowState.Choose => $"{Candidates.Count} items match — pick one",
        _ when Selected?.Product.PartNumber is { Length: > 0 } part => "Part No. " + part,
        _ => Selected?.Product.Brand ?? "",
    };

    public string StateText => State switch
    {
        PasteRowState.Found => ChosenByUser ? "✓ Chosen" : "✓ Found",
        PasteRowState.Choose => "Choose",
        PasteRowState.NotFound => "Not found",
        _ => "Searching…",
    };

    /// <summary>Decides from the search evidence: one clear match is taken; several close ones are offered; none is "not found".</summary>
    public void SetResults(IReadOnlyList<ProductSearchHitDto> hits)
    {
        Candidates.Clear();
        if (hits.Count == 0)
        {
            Selected = null;
            State = PasteRowState.NotFound;
            return;
        }
        var exact = hits.Where(h => h.Match.ExactIdentifier || h.Match.ExactName).ToList();
        if (exact.Count == 1)
        {
            Take(exact[0]);
            return;
        }
        var complete = hits.Where(h => h.Match.AllTermsMatched && !h.Match.UsedFuzzy).OrderByDescending(h => h.Score).ToList();
        if (exact.Count == 0 && complete.Count == 1)
        {
            Take(complete[0]);
            return;
        }
        if (exact.Count == 0 && complete.Count > 1 && complete[0].Score >= DecisiveMargin * Math.Max(0.01, complete[1].Score))
        {
            Take(complete[0]);
            return;
        }
        var choices = exact.Count > 1 ? exact : complete.Count > 1 ? complete : hits.ToList();
        foreach (var h in choices.Take(6)) Candidates.Add(h);
        _settingFromSearch = true;
        Selected = Candidates[0];
        _settingFromSearch = false;
        State = PasteRowState.Choose;
    }

    /// <summary>An item picked in the row's own search box (for a line that was not found).</summary>
    public void Pick(ProductSearchHitDto hit)
    {
        Candidates.Clear();
        IsSearching = false;
        ChosenByUser = true;
        Take(hit);
    }

    private bool _settingFromSearch;

    private void Take(ProductSearchHitDto hit)
    {
        _settingFromSearch = true;
        Selected = hit;
        _settingFromSearch = false;
        State = PasteRowState.Found;
    }

    partial void OnSelectedChanged(ProductSearchHitDto? value)
    {
        // Picking from the list of close matches confirms the line.
        if (!_settingFromSearch && value is not null && State == PasteRowState.Choose)
        {
            ChosenByUser = true;
            State = PasteRowState.Found;
        }
        Notify();
    }

    partial void OnStateChanged(PasteRowState value) => Notify();
    partial void OnQuantityChanged(decimal? value) => Notify();
    partial void OnIsSearchingChanged(bool value) => Notify();
    partial void OnChosenByUserChanged(bool value) => Notify();

    private void Notify()
    {
        foreach (var name in new[]
                 {
                     nameof(IsFound), nameof(IsChoose), nameof(IsNotFound), nameof(IsLoading), nameof(ShowsName), nameof(ShowsChoices),
                     nameof(ShowsNothingFound), nameof(ShowsSearchButton), nameof(ShowsSearchBox), nameof(IsReady), nameof(ItemName),
                     nameof(ItemDetail), nameof(StateText),
                 })
            OnPropertyChanged(name);
        Changed?.Invoke();
    }
}
