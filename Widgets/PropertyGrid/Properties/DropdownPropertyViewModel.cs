using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Shared parent for leaf widgets that pick one string from a fixed list of choices (the generic enum
/// dropdown and the theme picker). Owns the choice list and the commit-on-change wiring; subclasses
/// exist only to give each its own <c>[View]</c> routing and room to diverge (e.g. swatches) later.
/// </summary>
public abstract partial class DropdownPropertyViewModel : PropertyViewModel
{
    private readonly JsonValueSlot _slot;

    [ObservableProperty]
    private string _value;

    protected DropdownPropertyViewModel(PropertyNode node) : base(node)
    {
        _slot = new JsonValueSlot(node.Value);
        Choices = node.Choices ?? [];
        _value = ReadChoice(node.Value);
    }

    public IReadOnlyList<string> Choices { get; }

    public override JToken? CurrentValue => new JValue(Value ?? "");

    public override void ApplyValue(JToken token) => Value = ResolveChoice(token);

    // Track a live enum change in place instead of letting the base DeepEquals check force a grid rebuild.
    protected override bool SyncCore(PropertyNode node)
    {
        Value = ReadChoice(node.Value);
        return true;
    }

    partial void OnValueChanged(string value)
    {
        if (_slot.Set(new JValue(value)))
            RaiseCommit();
    }

    // The engine may serialize an enum as either its choice name or its numeric index (e.g. a choice-less or
    // not-[[serializable]] enum surfaces as a bare integer). A numeric value is mapped to the matching choice
    // name so the dropdown shows a real selection — otherwise "5" never matches a choice, the control renders
    // blank, and the first user pick clobbers the real value. Writes always go back as the choice name.
    private string ReadChoice(JToken? token) => token is null ? "" : ResolveChoice(token);

    private string ResolveChoice(JToken token)
    {
        if (token.Type == JTokenType.Integer)
        {
            var index = token.Value<int>();
            if (index >= 0 && index < Choices.Count)
                return Choices[index];
        }

        return token.Value<string>() ?? "";
    }
}
