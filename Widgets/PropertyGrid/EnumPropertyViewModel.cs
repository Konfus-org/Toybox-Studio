using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Enum property rendered as a dropdown of string choices (from the node's <c>$choices</c>).
/// </summary>
public sealed partial class EnumPropertyViewModel : PropertyViewModelBase
{
    private JToken? _token;

    public EnumPropertyViewModel(PropertyNode node) : base(node)
    {
        _token = node.Value;
        Choices = node.Choices ?? [];
        _value = node.Value?.Value<string>() ?? "";
    }

    public IReadOnlyList<string> Choices { get; }

    [ObservableProperty]
    private string _value;

    partial void OnValueChanged(string value)
    {
        if (_token is null)
            return;

        // Replace the live token and keep hold of the replacement so repeated selections keep
        // mutating the backing document (a detached token would silently stop persisting).
        var replacement = new JValue(value);
        _token.Replace(replacement);
        _token = replacement;
        RaiseCommit();
    }
}
