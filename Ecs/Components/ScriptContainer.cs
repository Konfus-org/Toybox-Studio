using Toybox.Studio.Project;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Ecs.Components;

/// <summary>Typed handle for the <c>script_container</c> component — string-free typed access by class name. It
/// models no individual fields; bound scripts are edited through the generic inspector grid.</summary>
[IconAttribute(Icon.ScrollText, PaletteColor.Green)]
public sealed class ScriptContainer : Component
{
}
