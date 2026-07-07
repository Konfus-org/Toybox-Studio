namespace Toybox.Studio.Utils.Attributes;

/// <summary>
/// Marks a View (a UserControl) as a dockable panel. The workspace's dockable catalog reflection-scans
/// the assemblies for these and turns each into a registered panel, so a dockable is declared in
/// exactly one place — on its own View — with no id to author: it is identified by its view-model type
/// (the <c>XxxView → XxxViewModel</c> same-namespace convention, or an explicit
/// <see cref="ViewModel"/>). Lives in Utils so feature projects can declare their panels without
/// referencing the shell.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DockableAttribute : Attribute
{
    /// <summary>Tab/window title. Falls back to the view-model type's name when unset.</summary>
    public string Title { get; init; } = "";

    /// <summary>The Lucide icon shown on the panel's tab/header and in the Windows menu, by the
    /// icon-pack kind's member name (as <see cref="IconAttribute"/>); empty for none.</summary>
    public string Icon { get; init; } = "";

    /// <summary>
    /// The view-model type backing this dockable. Defaults to the <c>XxxView → XxxViewModel</c>
    /// same-namespace convention; set this when the convention doesn't hold (e.g. a shared view-model).
    /// </summary>
    public Type? ViewModel { get; init; }

    /// <summary>Width of the floating window when the dockable is opened standalone.</summary>
    public double Width { get; init; } = 800;

    /// <summary>Height of the floating window when the dockable is opened standalone.</summary>
    public double Height { get; init; } = 600;
}
