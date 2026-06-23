using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Shell.Panels;

/// <summary>
/// Base for a panel that OWNS its data and is the source of truth for display. By DEFAULT a panel BUFFERS its
/// edits: changes are held in a working copy and only committed to the source (engine/disk) on
/// <see cref="SaveAsync"/>, and reverted on <see cref="Cancel"/>. Unsaved state shows as a trailing '*' on the
/// title (mirrored onto the dock tab by the window manager) and prompts before the tab closes.
///
/// The world/viewport is the one exception: it overrides <see cref="IsLive"/> to commit edits immediately
/// (no working copy, no Save/Cancel footer, no save-before-close prompt) — world edits must be live.
/// </summary>
public abstract partial class DataPanel : ObservableObject
{
    protected DataPanel()
    {
        // Bridge the computed Title to a plain event so the window manager can keep the dock tab in sync
        // without binding into the view-model.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Title))
                TitleChanged?.Invoke();
        };
    }

    /// <summary>Raised whenever <see cref="Title"/> changes — the hook the window manager subscribes to.</summary>
    public event Action? TitleChanged;

    /// <summary>The undecorated title (e.g. "Settings"). Constant for the current panels.</summary>
    public abstract string BaseTitle { get; }

    /// <summary>The dock-tab title: the base title with a trailing '*' while dirty.</summary>
    public string Title => IsDirty ? $"{BaseTitle} *" : BaseTitle;

    /// <summary>Whether the panel holds unsaved changes against its source of truth.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial bool IsDirty { get; protected set; }

    /// <summary>
    /// Whether this panel commits edits immediately rather than buffering them. The world is the only live
    /// panel; live panels get no Save/Cancel footer and no save-before-close prompt.
    /// </summary>
    public virtual bool IsLive => false;

    /// <summary>Buffered, unsaved changes worth prompting about before the tab closes (live panels: false).</summary>
    public bool HasUnsavedChanges => !IsLive && IsDirty;

    /// <summary>Commits to the source: buffered panels flush their working copy and re-baseline; live panels
    /// persist whatever is live (e.g. the world). The Save/Cancel footer binds this.</summary>
    [RelayCommand]
    public virtual async Task SaveAsync()
    {
        if (!IsLive)
            await CommitAsync().ContinueOnSameContext();
    }

    /// <summary>Discards buffered edits back to the last-saved baseline. No-op for live panels.</summary>
    [RelayCommand]
    public void Cancel()
    {
        if (!IsLive)
            RevertChanges();
    }

    /// <summary>Flushes the working copy to the source. Overridden by buffered panels.</summary>
    protected virtual Task CommitAsync() => Task.CompletedTask;

    /// <summary>Reverts the working copy to the baseline. Overridden by buffered panels.</summary>
    protected virtual void RevertChanges()
    {
    }
}
