using System.Text;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Base for every property widget view-model. Leaf widgets mutate the live JSON token in place and then
/// raise <see cref="CommitChanges"/> so the host can persist the owning object (e.g. re-send a component
/// over RPC). The host leaves <see cref="CommitChanges"/> null for a read-only grid.
/// </summary>
public abstract class PropertyViewModelBase : ObservableObject
{
    protected PropertyViewModelBase(PropertyNode node)
    {
        Name = Humanize(node.Name);
        Type = node.Type;
        Category = node.Category;
        Description = node.Description;
        IsReadOnly = node.ReadOnly;
        Icon = node.Icon;
        IconColor = node.IconColor;
        ResetCommand = new RelayCommand(() => ResetToDefault?.Invoke());
    }

    public string Name { get; }

    public string Type { get; }

    /// <summary>
    /// Nesting level within the grid (0 at the top). Drives the row's indent, elbow connector, and the
    /// per-depth shading. Set by <see cref="PropertyViewModelFactory"/> as the tree is built.
    /// </summary>
    public int Depth { get; set; }

    /// <summary>
    /// Editor icon for the value's type ([[tbx::icon]]), or null. Badges composite headers.
    /// </summary>
    public string? Icon { get; }

    /// <summary>
    /// The icon's accent colour name (e.g. "BLUE"), or null.
    /// </summary>
    public string? IconColor { get; }

    /// <summary>
    /// True for rows that own a collapsible sub-tree (object/array). Leaf rows are false.
    /// </summary>
    public virtual bool HasChildren => false;

    /// <summary>
    /// Group heading ([[tbx::category]]), or null for the default (header-less) group.
    /// </summary>
    public string? Category { get; }

    /// <summary>
    /// Tooltip text ([[editor::description]]), or null.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// True for [[editor::readonly]] fields: editable leaf views disable their control, and the host
    /// also withholds the commit action (see <see cref="PropertyViewModelFactory"/>).
    /// </summary>
    public bool IsReadOnly { get; }

    /// <summary>
    /// True for composite rows (object/array) that render full-width rather than name+value.
    /// </summary>
    public virtual bool IsComposite => false;

    /// <summary>
    /// Raised after a leaf edits its backing token. Null = read-only.
    /// </summary>
    public Action? CommitChanges { get; set; }

    /// <summary>
    /// Resets this property to its engine default. Set by the host (inspector) only on rows that support
    /// it — top-level, non-read-only component properties; null everywhere else (settings, nested rows).
    /// </summary>
    public Action? ResetToDefault { get; set; }

    /// <summary>True when a reset affordance should be offered for this row.</summary>
    public bool CanReset => ResetToDefault is not null;

    /// <summary>
    /// True when the row should show the "value is set" dot. Suppressed on resettable rows, whose state
    /// slot shows the reset button instead, and on read-only rows, which show a lock.
    /// </summary>
    public bool ShowSetDot => !IsReadOnly && !CanReset;

    /// <summary>Invokes <see cref="ResetToDefault"/>; bound to the row's reset button.</summary>
    public ICommand ResetCommand { get; }

    protected void RaiseCommit() => CommitChanges?.Invoke();

    /// <summary>
    /// Turns a raw field key into a human label: underscores become spaces, camelCase/PascalCase splits
    /// into words, and each word is capitalized. Array element keys ("[0]") are left untouched.
    /// </summary>
    private static string Humanize(string name)
    {
        if (string.IsNullOrEmpty(name) || name[0] == '[')
            return name;

        var builder = new StringBuilder(name.Length + 4);
        var startWord = true;
        var previous = '\0';
        foreach (var character in name)
        {
            if (character is '_' or '-' or ' ')
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                    builder.Append(' ');
                startWord = true;
                previous = character;
                continue;
            }

            // Insert a break at a camelCase boundary (a capital following a lower-case letter or digit).
            if (builder.Length > 0 && char.IsUpper(character) && (char.IsLower(previous) || char.IsDigit(previous)))
            {
                builder.Append(' ');
                startWord = true;
            }

            builder.Append(startWord ? char.ToUpperInvariant(character) : character);
            startWord = false;
            previous = character;
        }

        return builder.ToString();
    }
}
