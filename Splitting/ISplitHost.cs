namespace Toybox.Studio.Splitting;

/// <summary>
/// A dockable view-model whose panel hosts a split container. The workspace persists the panel's
/// <see cref="SplitLayout"/> inside the dock layout and hands the persisted instance here when the
/// panel materializes — and again on every attach pass of a layout restore, so implementations must be
/// idempotent. Lives in Utils so feature projects can implement it without referencing the workspace,
/// mirroring <see cref="Toolbars.IToolbarHost"/>.
/// </summary>
public interface ISplitHost
{
    /// <summary>Binds the panel to its persisted <paramref name="layout"/> (re-binding the same
    /// instance must no-op).</summary>
    void BindSplitLayout(SplitLayout layout);
}
