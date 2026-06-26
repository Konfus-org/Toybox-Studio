using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.Clipboard;

/// <summary>A copied single property value (the bare JSON token), ready to paste into another compatible field.</summary>
[ClipboardKind("propertyValue")]
public sealed class ClipboardPropertyValue
{
    public JToken Value { get; set; } = JValue.CreateNull();
}
