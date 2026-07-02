namespace Toybox.Studio.EngineApi;

/// <summary>
/// When/how a <see cref="EngineSyncAttribute"/> field syncs with the engine. Governs <i>live-sync timing only</i> —
/// persistence to disk (for assets) is a separate commit (<c>Save</c>).
/// </summary>
public enum EngineSyncMode
{
    /// <summary>Push on change, throttled to an enforced max rate (leading + trailing) so a continuous scrub/drag
    /// stays live without flooding the channel. The default; right for toggles, dropdowns, live edits.</summary>
    OnChanged,

    /// <summary>Coalesce rapid changes, pushing once the field has been quiet for the window (trailing-edge
    /// debounce). Right for sliders, drag handles, and typing, where every keystroke would otherwise be an RPC.</summary>
    Timer,

    /// <summary>Stage changes and push only when the owner explicitly commits (<c>CommitAsync</c>/<c>Save</c>).
    /// Right for heavy fields and for buffered editing.</summary>
    Manual,

    /// <summary>Inbound only: the generated property still raises <c>PropertyChanged</c> on set, but never pushes
    /// to the engine. The field is engine-owned read state, (re)filled by hydration and <c>RefreshAsync</c>.</summary>
    Hydrate,

    /// <summary>Inbound only and immutable: the generator emits a get-only property (no setter) whose backing field
    /// is filled once at hydration and never pushed. Right for identity that the editor must never edit — an asset
    /// handle, an id, a version.</summary>
    ReadOnly,

    /// <summary>Inbound only and periodically re-pulled (every <c>windowMs</c>) from the engine — engine-owned live
    /// state the editor mirrors (e.g. a streamed entity tree). Codegen treats it like <see cref="Hydrate"/> for
    /// push; the periodic re-pull timer lands with its first consumer.</summary>
    Stream,
}
