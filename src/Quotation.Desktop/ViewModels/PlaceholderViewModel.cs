namespace Quotation.Desktop.ViewModels;

/// <summary>Placeholder for screens delivered in later phases.</summary>
public sealed class PlaceholderViewModel(string title, string phase) : ViewModelBase
{
    public string Title { get; } = title;
    public string Message { get; } = $"This screen is delivered in {phase}.";
}
