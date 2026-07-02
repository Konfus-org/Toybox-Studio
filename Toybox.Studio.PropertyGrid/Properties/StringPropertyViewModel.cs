using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// String property rendered as a text box.
/// </summary>
public sealed partial class StringPropertyViewModel : PropertyViewModel
{
    [ObservableProperty]
    private string _value;

    public StringPropertyViewModel(PropertyDescriptor descriptor, IValueAccessor accessor)
        : base(descriptor, accessor)
    {
        _value = accessor.Get() as string ?? "";
    }

    public override void ApplyValue(JToken token) => Value = token.Value<string>() ?? "";

    protected override bool SyncCore(IValueAccessor accessor)
    {
        Value = accessor.Get() as string ?? "";
        return true;
    }

    partial void OnValueChanged(string value)
    {
        Accessor.Set(value);
        RaiseCommit();
    }
}
