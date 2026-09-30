using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Quotation.Desktop.ViewModels;

namespace Quotation.Desktop;

/// <summary>Resolves FooViewModel → FooView by naming convention.</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null) return null;
        var name = data.GetType().FullName!.Replace(".ViewModels.", ".Views.").Replace("ViewModel", "View");
        var type = Type.GetType(name);
        return type is null ? new TextBlock { Text = "View not found: " + name } : (Control)Activator.CreateInstance(type)!;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
