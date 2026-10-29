using CommunityToolkit.Mvvm.ComponentModel;

namespace Quotation.Desktop.Services;

/// <summary>Lets pages open other pages without knowing the shell.</summary>
public interface INavigator
{
    void Navigate(string key);
    void OpenQuotation(Guid id);
    void NewQuotation();
    /// <summary>Returns to the previous page (like a browser's back button).</summary>
    void GoBack();
    /// <summary>Asks a Yes/No question in the app window; true for <paramref name="yes"/>.</summary>
    Task<bool> ConfirmAsync(string title, string message, string yes = "Yes", string no = "No");
    /// <summary>Asks for a value (e.g. the 4-digit seal PIN); null when cancelled.</summary>
    Task<string?> AskAsync(string title, string message, string inputLabel, bool secret = false, string ok = "OK");
}

/// <summary>A question shown over the app window (Yes/No, or with an input box).</summary>
public sealed partial class ShellDialog(string title, string message, string primary, string secondary, string? inputLabel = null, bool secret = false)
    : ObservableObject
{
    private readonly TaskCompletionSource<bool> _answer = new();

    public string Title { get; } = title;
    public string Message { get; } = message;
    public string PrimaryText { get; } = primary;
    public string SecondaryText { get; } = secondary;
    public string? InputLabel { get; } = inputLabel;
    public bool HasInput => InputLabel is not null;
    public char SecretChar => secret ? '●' : '\0';
    [ObservableProperty] private string _input = "";

    public Task<bool> Answer => _answer.Task;
    public void Choose(bool primaryChosen) => _answer.TrySetResult(primaryChosen);
}

/// <summary>A page with a title shown in the breadcrumbs and on the Back button.</summary>
public interface ITitledPage
{
    string Title { get; }
}

/// <summary>A page that may hold unsaved work; the shell asks Save / Don't save / Cancel before leaving it.</summary>
public interface IUnsavedChangesPage
{
    bool HasUnsavedChanges { get; }
    /// <summary>Saves; false when saving failed (the page then shows why and the user stays on it).</summary>
    Task<bool> SaveChangesAsync();
}

/// <summary>A page that refreshes its data when the user comes back to it (filters and search are kept).</summary>
public interface IReturnablePage
{
    void OnReturnedTo();
}

public enum LeaveChoice
{
    Save,
    DontSave,
    Cancel,
}

/// <summary>The question shown before leaving a page with unsaved changes.</summary>
public sealed class LeavePrompt(string title)
{
    private readonly TaskCompletionSource<LeaveChoice> _answer = new();

    public string Message { get; } = $"Save the changes to {title}?";
    public Task<LeaveChoice> Answer => _answer.Task;
    public void Choose(LeaveChoice choice) => _answer.TrySetResult(choice);
}
