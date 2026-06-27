using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.Clipboard;

/// <summary>A copied entity — its serialized body (as the engine returns it), ready to spawn back into a world.</summary>
[ClipboardKind("entity")]
public sealed class ClipboardEntity
{
    public JObject Body { get; set; } = new();
}
