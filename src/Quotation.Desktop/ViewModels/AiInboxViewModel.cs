using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Core.Calculation;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

/// <summary>AI Inbox: typed natural-language requests and analysed Gmail requests awaiting review.</summary>
public sealed partial class AiInboxViewModel : ViewModelBase
{
    private readonly AppSession _session;
    private readonly INavigator _navigator;

    [ObservableProperty] private string _requestText = "";
    [ObservableProperty] private bool _showAll;
    [ObservableProperty] private string _aiState = "";

    public ObservableCollection<AiRequestItemViewModel> Items { get; } = [];

    public AiInboxViewModel(AppSession session, INavigator navigator)
    {
        _session = session;
        _navigator = navigator;
        _ = LoadAsync();
    }

    partial void OnShowAllChanged(bool value) => _ = LoadAsync();

    [RelayCommand]
    public Task LoadAsync() => RunAsync(async () =>
    {
        var status = await _session.Api.StatusAsync();
        AiState = status.AiConfigured ? "" : "The AI assistant is not configured. An administrator can enable it in Settings → Claude AI.";
        var list = await _session.Api.AiRequestsAsync(take: 200);
        Items.Clear();
        foreach (var r in list.Where(r => ShowAll || r.Status is AiRequestStatus.NeedsClarification or AiRequestStatus.ReadyForReview
                     or AiRequestStatus.Failed or AiRequestStatus.Processing || (r.Status == AiRequestStatus.DraftCreated && r.QuotationNumber is null)))
        {
            Items.Add(new AiRequestItemViewModel(r, _session, _navigator, () => LoadAsync()));
        }
    });

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (string.IsNullOrWhiteSpace(RequestText))
        {
            ErrorMessage = "Type what should be quoted, e.g. \"Create quotation for Sonepar for 5 universal bevel protractors\".";
            return;
        }
        InfoMessage = "Claude is reading the request and searching the Tally catalogue…";
        AiRequestDto? result = null;
        var ok = await RunAsync(async () => result = await _session.Api.AnalyzeAsync(RequestText.Trim()));
        InfoMessage = null;
        if (!ok || result is null) return;
        RequestText = "";
        await LoadAsync();
        var item = Items.FirstOrDefault(i => i.Id == result.Id) ?? new AiRequestItemViewModel(result, _session, _navigator, () => LoadAsync());
        if (!Items.Contains(item)) Items.Insert(0, item);
        InfoMessage = result.Status switch
        {
            AiRequestStatus.DraftCreated => "Draft created — open it to review and approve.",
            AiRequestStatus.NeedsClarification => "Some details need your decision (see below).",
            AiRequestStatus.Failed => result.Error,
            AiRequestStatus.NotAQuotation => "This does not look like a quotation request.",
            _ => null,
        };
    }
}

public sealed partial class AiLineViewModel : ObservableObject
{
    public AiLineViewModel(LineResolution line)
    {
        Line = line;
        Candidates = line.Candidates.Select(c => new CandidateOption(c)).ToList();
        _selected = line.ProductId is { } id ? Candidates.FirstOrDefault(c => c.Candidate.Id == id) : null;
        _quantity = line.Quantity;
    }

    public LineResolution Line { get; }
    public List<CandidateOption> Candidates { get; }
    [ObservableProperty] private CandidateOption? _selected;
    [ObservableProperty] private decimal? _quantity;

    public string RequestedText => Line.RequestedText + (Line.Specifications.Length > 0 ? $"  ({Line.Specifications})" : "");
    public string StatusText => Line.Status switch
    {
        ResolutionStatus.Matched => "Matched",
        ResolutionStatus.NeedsSelection => "Select product",
        ResolutionStatus.NeedsQuantity => "Enter quantity",
        _ => "Not found",
    };
    public bool NeedsAttention => Line.Status != ResolutionStatus.Matched;
    public string Reason => Line.Reason;
    public bool IsComplete => Selected is not null && Quantity is > 0;

    partial void OnSelectedChanged(CandidateOption? value) => OnPropertyChanged(nameof(IsComplete));
    partial void OnQuantityChanged(decimal? value) => OnPropertyChanged(nameof(IsComplete));
}

public sealed class CandidateOption(ProductCandidate c)
{
    public ProductCandidate Candidate { get; } = c;
    public string Label { get; } = $"{c.Name}   [{c.Unit}{(c.Rate is { } r ? " · ₹" + IndianFormat.Amount(r) : " · no rate")}]";
    public override string ToString() => Label;
}

public sealed class CustomerOption(CustomerCandidate c)
{
    public CustomerCandidate Candidate { get; } = c;
    public string Label { get; } = $"{c.Name}  ({c.StateName}{(c.Gstin.Length > 0 ? ", " + c.Gstin : "")})";
    public override string ToString() => Label;
}

public sealed partial class AiRequestItemViewModel : ViewModelBase
{
    private readonly AppSession _session;
    private readonly INavigator _navigator;
    private readonly Func<Task> _reload;

    public AiRequestItemViewModel(AiRequestDto dto, AppSession session, INavigator navigator, Func<Task> reload)
    {
        Dto = dto;
        _session = session;
        _navigator = navigator;
        _reload = reload;
        var a = dto.Analysis;
        CustomerOptions = a?.Customer.Candidates.Select(c => new CustomerOption(c)).ToList() ?? [];
        _selectedCustomer = a?.Customer.CustomerId is { } cid ? CustomerOptions.FirstOrDefault(c => c.Candidate.Id == cid) : null;
        Lines = a?.Lines.Select(l => new AiLineViewModel(l)).ToList() ?? [];
        foreach (var l in Lines) l.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanCreateDraft));
    }

    public AiRequestDto Dto { get; }
    public int Id => Dto.Id;
    public string Title => Dto.Source == QuotationSource.Gmail ? $"E-mail: {Dto.Subject}" : "Typed request";
    public string From => Dto.Source == QuotationSource.Gmail ? Dto.FromAddress : Dto.CreatedBy;
    public string When => Formatting.LocalDateTime(Dto.ReceivedUtc ?? Dto.CreatedUtc) ?? "";
    public string Summary => Dto.Summary;
    public string Input => Dto.InputText.Length > 600 ? Dto.InputText[..600] + "…" : Dto.InputText;
    public string StatusText => Dto.Status switch
    {
        AiRequestStatus.ReadyForReview => "Ready for review",
        AiRequestStatus.NeedsClarification => "Needs clarification",
        AiRequestStatus.DraftCreated => Dto.QuotationNumber is null ? "Draft created — review pending" : $"Draft {Dto.QuotationNumber}",
        AiRequestStatus.NotAQuotation => "Not a quotation request",
        AiRequestStatus.Failed => "Failed",
        AiRequestStatus.Dismissed => "Dismissed",
        _ => Dto.Status.ToString(),
    };
    public bool NeedsAttention => Dto.Status is AiRequestStatus.NeedsClarification or AiRequestStatus.Failed;
    public string? Error => Dto.Error;
    public string CustomerStatus => Dto.Analysis?.Customer.Status switch
    {
        ResolutionStatus.Matched => "Customer found",
        ResolutionStatus.NeedsSelection => "Select customer",
        ResolutionStatus.NotFound => "Customer not found — select or search in the draft",
        _ => "",
    };
    public string CustomerReason => Dto.Analysis?.Customer.Reason ?? "";
    public string CustomerQuery => Dto.Analysis?.Customer.Query ?? "";
    public IReadOnlyList<string> Questions => Dto.Analysis?.Questions ?? [];
    public bool HasAnalysis => Dto.Analysis is not null && Dto.Status != AiRequestStatus.NotAQuotation;
    public bool HasDraft => Dto.QuotationId is not null;
    public bool CanDecide => HasAnalysis && !HasDraft && Dto.Status is not (AiRequestStatus.Dismissed or AiRequestStatus.Failed);

    public List<CustomerOption> CustomerOptions { get; }
    [ObservableProperty] private CustomerOption? _selectedCustomer;
    public List<AiLineViewModel> Lines { get; }

    public bool CanCreateDraft => CanDecide && SelectedCustomer is not null && Lines.Count > 0 && Lines.All(l => l.IsComplete);

    partial void OnSelectedCustomerChanged(CustomerOption? value) => OnPropertyChanged(nameof(CanCreateDraft));

    [RelayCommand]
    private async Task CreateDraftAsync()
    {
        if (!CanCreateDraft)
        {
            ErrorMessage = "Select the customer and a product and quantity for every line.";
            return;
        }
        AiRequestDto? r = null;
        if (await RunAsync(async () => r = await _session.Api.CreateDraftFromAiAsync(Id, new CreateDraftFromAiRequest
            {
                CustomerId = SelectedCustomer!.Candidate.Id,
                Lines = Lines.Select(l => new AiDraftLine { ProductId = l.Selected!.Candidate.Id, Quantity = l.Quantity!.Value }).ToList(),
            })) && r?.QuotationId is { } qid)
        {
            _navigator.OpenQuotation(qid);
        }
    }

    [RelayCommand]
    private void OpenDraft()
    {
        if (Dto.QuotationId is { } id) _navigator.OpenQuotation(id);
    }

    [RelayCommand]
    private async Task DismissAsync()
    {
        if (await RunAsync(() => _session.Api.DismissAiRequestAsync(Id))) await _reload();
    }
}
