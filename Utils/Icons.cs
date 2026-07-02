using System;

namespace Toybox.Studio.Utils;

/// <summary>
/// The string ↔ <see cref="Icon"/> boundary. Code authors icons as the strong <see cref="Icon"/>
/// enum directly; these helpers are only for the wire/persisted edges where an icon arrives as text — the
/// engine's <c>[[tbx::icon]]</c> name in a describe reply, or a saved settings glyph. An unknown or empty name
/// resolves to <see cref="Icon.None"/> (the control then simply hides the glyph) rather than throwing.
/// </summary>
public static class Icons
{
    /// <summary>The icon for a Lucide name (case-insensitive), or <see cref="Icon.None"/> when the name
    /// is empty or unrecognised.</summary>
    public static Icon Parse(string? name) =>
        !string.IsNullOrWhiteSpace(name) && Enum.TryParse<Icon>(name, ignoreCase: true, out var kind)
            ? kind
            : Icon.None;

    /// <summary>The Lucide name for an icon (e.g. <c>"Move3d"</c>), or null for <see cref="Icon.None"/>.</summary>
    public static string? ToWire(Icon icon) => icon == Icon.None ? null : icon.ToString();
}
