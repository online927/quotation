using CommunityToolkit.Mvvm.ComponentModel;
using Quotation.Contracts;
using Quotation.Core.Calculation;

namespace Quotation.Desktop.ViewModels;

/// <summary>One quotation row as shown in the item table.</summary>
public sealed partial class QuotationLineViewModel : ObservableObject
{
    [ObservableProperty] private int _lineNo;
    [ObservableProperty] private int? _productId;
    [ObservableProperty] private string _itemName = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _hsn = "";
    [ObservableProperty] private decimal? _gstRate;
    [ObservableProperty] private string _dueOn = "";
    [ObservableProperty] private decimal _quantity;
    [ObservableProperty] private string _unit = "";
    [ObservableProperty] private decimal _rate;
    [ObservableProperty] private decimal? _tallyRate;
    [ObservableProperty] private decimal _discountPercent;
    [ObservableProperty] private bool _isEditing;

    public decimal Amount => QuotationCalculator.LineAmount(Quantity, Rate, DiscountPercent);
    public bool RateOverridden => TallyRate is not null && Rate != TallyRate;
    public bool HasDescription => Description.Length > 0;
    public string QuantityText => IndianFormat.Quantity(Quantity);
    public string RateText => IndianFormat.Amount(Rate);
    public string AmountText => IndianFormat.Amount(Amount);
    public string GstText => GstRate is null ? "?" : $"{GstRate:0.##}%";
    public string DiscountText => DiscountPercent == 0 ? "" : $"{DiscountPercent:0.##}%";
    public string TallyRateHint => RateOverridden ? $"Tally rate {IndianFormat.Amount(TallyRate!.Value)}" : "";

    partial void OnQuantityChanged(decimal value) => Changed();
    partial void OnRateChanged(decimal value) => Changed();
    partial void OnDiscountPercentChanged(decimal value) => Changed();
    partial void OnTallyRateChanged(decimal? value) => Changed();
    partial void OnDescriptionChanged(string value) => OnPropertyChanged(nameof(HasDescription));
    partial void OnGstRateChanged(decimal? value) => OnPropertyChanged(nameof(GstText));

    private void Changed()
    {
        OnPropertyChanged(nameof(Amount));
        OnPropertyChanged(nameof(AmountText));
        OnPropertyChanged(nameof(RateText));
        OnPropertyChanged(nameof(QuantityText));
        OnPropertyChanged(nameof(DiscountText));
        OnPropertyChanged(nameof(RateOverridden));
        OnPropertyChanged(nameof(TallyRateHint));
    }

    public static QuotationLineViewModel From(QuotationLineDto d) => new()
    {
        LineNo = d.LineNo, ProductId = d.ProductId, ItemName = d.ItemName, Description = d.Description, Hsn = d.Hsn,
        GstRate = d.GstRate, DueOn = d.DueOn, Quantity = d.Quantity, Unit = d.Unit, Rate = d.Rate, TallyRate = d.TallyRate,
        DiscountPercent = d.DiscountPercent,
    };

    public QuotationLineDto ToDto() => new()
    {
        LineNo = LineNo, ProductId = ProductId, ItemName = ItemName, Description = Description, Hsn = Hsn, GstRate = GstRate,
        DueOn = DueOn, Quantity = Quantity, Unit = Unit, Rate = Rate, TallyRate = TallyRate, DiscountPercent = DiscountPercent,
    };
}
