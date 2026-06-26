using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.Clipboard;

/// <summary>A copied entity: enough to recreate it (its components are the typed component JSON, keyed by type).</summary>
[ClipboardKind("entity")]
public sealed class ClipboardEntity
{
    public string Name { get; set; } = "";

    public bool IsGlobal { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>The entity's components, type name → its typed <c>{ type, value }</c> JSON.</summary>
    public Dictionary<string, JObject> Components { get; set; } = [];
}
