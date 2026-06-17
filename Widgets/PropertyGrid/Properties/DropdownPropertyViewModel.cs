using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Shared parent for leaf widgets that pick one string from a fixed list of choices (the generic enum
/// dropdown and the theme picker). Owns the choice list and the commit-on-change wiring; subclasses
/// exist only to give each its own <c>[View]</c> routing and room to diverge (e.g. swatches) later.
/// </summary>
public abstract partial class DropdownPropertyViewModel : PropertyViewModel
{
    private readonly JsonValueSlot _slot;

    protected DropdownPropertyViewModel(PropertyNode node) : base(node)
    {
        _slot = new JsonValueSlot(node.Value);
        Choices = node.Choices ?? [];
        _value = _slot.Read<string>() ?? "";
    }

    public IReadOnlyList<string> Choices { get; }

    [ObservableProperty]
    private string _value;

    partial void OnValueChanged(string value)
    {
        if (_slot.Set(new JValue(value)))
            RaiseCommit();
    }

    public override JToken? CurrentValue => new JValue(Value ?? "");

    public override void ApplyValue(JToken token) => Value = token.Value<string>() ?? "";
}
