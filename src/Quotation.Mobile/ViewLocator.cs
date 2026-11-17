using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Quotation.Mobile.ViewModels;

namespace Quotation.Mobile;

/// <summary>Shows each phone page with its view: HomeViewModel → Views.HomeView.</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null) return null;
        var name = data.GetType().FullName!.Replace(".ViewModels.", ".Views.").Replace("ViewModel", "View");
        var type = typeof(ViewLocator).Assembly.GetType(name);
        return type is null ? new TextBlock { Text = "Not found: " + name } : (Control)Activator.CreateInstance(type)!;
    }

    public bool Match(object? data) => data is PageViewModel or LoginViewModel;
}
