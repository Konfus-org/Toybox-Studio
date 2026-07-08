using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// Resolves a popup view-model to its body view by the <c>XxxViewModel → XxxView</c> same-namespace,
/// same-assembly convention (the inverse of the dockable catalog's view → view-model resolution).
/// <see cref="PopupWindow"/> carries one in its DataTemplates so its body ContentControl materializes
/// the right view for whatever popup it hosts.
/// </summary>
public sealed class PopupViewLocator : IDataTemplate
{
    public bool Match(object? data) => data is PopupViewModel;

    public Control Build(object? data)
    {
        var type = data!.GetType();
        var name = type.Name;

        var viewName = name.EndsWith("ViewModel", StringComparison.Ordinal)
            ? name[..^"ViewModel".Length] + "View"
            : name + "View";

        var fullName = type.Namespace is { } ns ? $"{ns}.{viewName}" : viewName;
        var viewType = type.Assembly.GetType(fullName)
                       ?? throw new InvalidOperationException(
                           $"Popup '{type.Name}': could not find its view '{fullName}'. Popup views follow "
                           + "the XxxViewModel → XxxView same-namespace convention.");
        return (Control)Activator.CreateInstance(viewType)!;
    }
}
