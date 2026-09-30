using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

public sealed partial class ChangePasswordViewModel(AppSession session, bool forced, Action onDone) : ViewModelBase
{
    [ObservableProperty] private string _currentPassword = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private string _confirmPassword = "";

    public bool Forced { get; } = forced;
    public string Heading => Forced ? "Please set a new password before continuing" : "Change password";

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (NewPassword != ConfirmPassword)
        {
            ErrorMessage = "New passwords do not match.";
            return;
        }
        if (await RunAsync(() => session.Api.ChangePasswordAsync(CurrentPassword, NewPassword)))
        {
            onDone();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!Forced) onDone();
    }
}
