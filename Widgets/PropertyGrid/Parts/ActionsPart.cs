using System.Windows.Input;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// The add/remove part of a row. A resizable list's own row carries an <see cref="AddCommand"/> (append a
/// fresh element); each element row carries a <see cref="RemoveCommand"/> (delete itself). Either may be null,
/// and the View shows only the affordances that are present, so the same part serves both roles.
/// </summary>
public sealed class ActionsPart : PropertyPart
{
    public ActionsPart(ICommand? add = null, ICommand? remove = null)
    {
        AddCommand = add;
        RemoveCommand = remove;
    }

    public ICommand? AddCommand { get; }

    public ICommand? RemoveCommand { get; }

    public bool CanAdd => AddCommand is not null;

    public bool CanRemove => RemoveCommand is not null;
}
