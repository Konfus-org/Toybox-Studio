using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// A theme selector rendered as a dropdown of theme names (supplied as the node's <c>$choices</c> from
/// the themes folder). Commits the chosen name back to the backing token; the settings grid applies it.
/// Wired via [View("themePicker")]. Kept as its own widget so it can grow (e.g. show swatches) without
/// touching the generic enum widget.
/// </summary>
public sealed partial class ThemePickerPropertyViewModel : PropertyViewModelBase
{
    private JToken? _token;

    public ThemePickerPropertyViewModel(PropertyNode node, Action? commit) : base(node)
    {
        _token = node.Value;
        Choices = node.Choices ?? [];
        _value = node.Value?.Value<string>() ?? "";
        CommitChanges = commit;
    }

    public IReadOnlyList<string> Choices { get; }

    [ObservableProperty]
    private string _value;

    partial void OnValueChanged(string value)
    {
        if (_token is null)
            return;

        var replacement = new JValue(value);
        _token.Replace(replacement);
        _token = replacement;
        RaiseCommit();
    }
}
