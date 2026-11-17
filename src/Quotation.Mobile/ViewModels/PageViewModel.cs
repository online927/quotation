using CommunityToolkit.Mvvm.ComponentModel;
using Quotation.ApiClient;

namespace Quotation.Mobile.ViewModels;

/// <summary>One phone screen: a title, busy while loading, and a red line when something went wrong.</summary>
public abstract partial class PageViewModel : ObservableObject
{
    public abstract string Title { get; }
    /// <summary>A back arrow instead of the bottom menu's own pages.</summary>
    public virtual bool HasBack => false;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    /// <summary>Called each time the screen is shown.</summary>
    public virtual Task LoadAsync() => Task.CompletedTask;

    protected async Task RunAsync(Func<Task> work)
    {
        IsBusy = true;
        ErrorMessage = null;
        try { await work(); }
        catch (ApiException ex) { ErrorMessage = ex.IsConnectionFailure ? "The Quotation Server cannot be reached. Check the office Wi-Fi." : ex.FullMessage; }
        catch (HttpRequestException) { ErrorMessage = "The Quotation Server cannot be reached. Check the office Wi-Fi."; }
        catch (TaskCanceledException) { ErrorMessage = "The Quotation Server did not answer in time."; }
        catch (IOException ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
