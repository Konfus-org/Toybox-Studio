using System;

namespace Toybox.Studio.Services.Clipboard;

/// <summary>
/// Gives a clipboard-able type a stable wire "kind" tag, independent of its C# name. The generic
/// <see cref="Clipboard"/> stamps this tag onto the payload so a later <c>Peek</c>/<c>Pop</c> can tell what is
/// held and refuse to deserialize an unrelated payload into the wrong type. Optional — a type with no attribute
/// falls back to its <see cref="Type.Name"/>; declare one when you want the tag to survive a rename or to read
/// nicely in the raw clipboard JSON (e.g. <c>"entity"</c> rather than <c>"ClipboardEntity"</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class ClipboardKindAttribute(string kind) : Attribute
{
    /// <summary>The stable wire tag written into the clipboard envelope.</summary>
    public string Kind { get; } = kind;
}
