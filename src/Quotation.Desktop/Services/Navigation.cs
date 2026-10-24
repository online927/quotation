namespace Quotation.Desktop.Services;

/// <summary>Lets pages open other pages without knowing the shell.</summary>
public interface INavigator
{
    void Navigate(string key);
    void OpenQuotation(Guid id);
    void NewQuotation();
    /// <summary>Returns to the previous page (like a browser's back button).</summary>
    void GoBack();
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
