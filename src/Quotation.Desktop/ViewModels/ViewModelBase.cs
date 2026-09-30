using CommunityToolkit.Mvvm.ComponentModel;
using Quotation.ApiClient;

namespace Quotation.Desktop.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    /// <summary>Runs an async operation, converting server/network errors into a visible message.</summary>
    protected async Task<bool> RunAsync(Func<Task> action)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await action();
            return true;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.FullMessage;
            return false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
