using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Shared parent for leaf widgets that pick one string from a fixed list of choices (the generic enum
/// dropdown and the theme picker). Owns the choice list and the commit-on-change wiring; subclasses
/// exist only to give each its own <c>[View]</c> routing and room to diverge (e.g. swatches) later.
/// </summary>
public abstract partial class DropdownPropertyViewModel : PropertyViewModel
{
    [ObservableProperty]
    private string _value;

    protected DropdownPropertyViewModel(PropertyDescriptor descriptor, IValueAccessor accessor)
        : base(descriptor, accessor)
    {
        Choices = descriptor.Choices ?? [];
        _value = ReadChoice(accessor);
    }

    public IReadOnlyList<string> Choices { get; }

    public override void ApplyValue(JToken token) => Value = ResolveChoice(token);

    // Track a live enum change in place instead of letting the base DeepEquals check force a grid rebuild.
    protected override bool SyncCore(IValueAccessor accessor)
    {
        Value = ReadChoice(accessor);
        return true;
    }

    partial void OnValueChanged(string value)
    {
        Accessor.Set(value ?? "");
        RaiseCommit();
    }

    // The engine may serialize an enum as either its choice name or its numeric index (e.g. a choice-less or
    // not-[[serializable]] enum surfaces as a bare integer). Read the live JSON token (via CurrentValue) so a
    // numeric value maps to the matching choice name rather than "5"; writes always go back as the choice name.
    private string ReadChoice(IValueAccessor accessor)
    {
        var value = accessor.Get();
        if (value is null)
            return "";

        return ResolveChoice(EngineSyncValue.WriteBare(value));
    }

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
