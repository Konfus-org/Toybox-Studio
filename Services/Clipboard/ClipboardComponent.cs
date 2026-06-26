using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.Clipboard;

/// <summary>A copied single component: its type name and its typed value, ready to paste onto another entity.</summary>
[ClipboardKind("component")]
public sealed class ClipboardComponent
{
    public string Component { get; set; } = "";

    public JObject Value { get; set; } = new();
}
