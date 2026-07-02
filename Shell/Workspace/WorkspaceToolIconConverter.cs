using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Toybox.Studio.Shell.Workspace;

/// <summary>
/// A dockable (an <c>IDockable</c>) → its header icon's Lucide name or tbx colour constant, read from the
/// <see cref="WorkspaceTool"/> the window manager stamps onto each tool. Null-safe: anything that isn't a
/// <see cref="WorkspaceTool"/> (e.g. a dockable restored from a layout saved before icons existed) yields
/// null, so the bound <c>IconView</c> simply hides its glyph rather than logging a binding error. The tab
/// strip and chrome title-bar templates bind the whole dockable through the <see cref="Name"/> / <see cref="Color"/>
/// instances.
/// </summary>
public sealed class WorkspaceToolIconConverter : IValueConverter
{
    /// <summary>Dockable → its Lucide header icon.</summary>
    public static readonly WorkspaceToolIconConverter Name = new(tool => tool.IconName);

    /// <summary>Dockable → its header icon's palette colour.</summary>
    public static readonly WorkspaceToolIconConverter Color = new(tool => tool.IconColor);

    private readonly Func<WorkspaceTool, object?> _select;

    private WorkspaceToolIconConverter(Func<WorkspaceTool, object?> select) => _select = select;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        // A non-WorkspaceTool dockable (e.g. one restored from a pre-icon layout) leaves the enum target at its
        // default (None) via UnsetValue, so the bound IconView just hides its glyph rather than erroring.
        value is WorkspaceTool tool ? _select(tool) : AvaloniaProperty.UnsetValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
