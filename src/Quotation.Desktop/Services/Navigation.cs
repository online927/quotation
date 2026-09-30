namespace Quotation.Desktop.Services;

/// <summary>Lets pages open other pages without knowing the shell.</summary>
public interface INavigator
{
    void Navigate(string key);
    void OpenQuotation(Guid id);
    void NewQuotation();
}
