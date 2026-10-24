using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Quotation.Desktop.ViewModels;

namespace Quotation.Desktop;

/// <summary>
/// Resolves FooViewModel → FooView by naming convention. A page's view is kept as long as its view-model lives,
/// so going Back shows the page exactly as it was left (scroll position, expanded sections, focus).
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    private readonly ConditionalWeakTable<object, Control> _views = new();

    public Control? Build(object? data)
    {
        if (data is null) return null;
        if (_views.TryGetValue(data, out var cached) && cached.Parent is null) return cached;
        var name = data.GetType().FullName!.Replace(".ViewModels.", ".Views.").Replace("ViewModel", "View");
        var type = Type.GetType(name);
        if (type is null) return new TextBlock { Text = "View not found: " + name };
        var view = (Control)Activator.CreateInstance(type)!;
        _views.AddOrUpdate(data, view);
        return view;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
