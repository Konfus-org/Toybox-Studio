using Toybox.Studio.Coding;
using Toybox.Studio.Dialogs;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Logging;
using Toybox.Studio.Projects;
using Toybox.Studio.Utils;

namespace Toybox.Studio.AssetViewer;

/// <summary>
/// Routes File ▸ Open ▸ Asset by asset type: a world opens as the active editing world (shown in the World
/// Viewer), a previewable asset (model / texture / material) opens in the Asset Viewer, a C++ script or a
/// GLSL/OpenGL shader opens in the in-Studio Coder editor, and everything else (plain data, unknown) opens in
/// the OS default app.
/// </summary>
public sealed class AssetOpener(
    AssetViewerLauncher viewer, CoderLauncher coder, Engine engine, Project project, Popups popups, Logger log)
{
    /// <summary>Opens the asset by kind: a world becomes the active editing world, a previewable asset goes
    /// to the Asset Viewer (reusing the open one when reuse is on), everything else the OS default app.</summary>
    public void Open(AssetEntry asset)
    {
        if (AssetClassifier.IsWorld(asset))
        {
            OpenWorldAsync(asset).FireAndForget();
            return;
        }

        if (AssetClassifier.IsPreviewable(asset))
        {
            viewer.Open(asset);
            return;
        }

        if (TryOpenInCoder(asset))
            return;

        OpenExternally(asset);
    }

    /// <summary>Opens a previewable asset in a FRESH Asset Viewer (the browser's "Open in New Window"); a
    /// world still opens as the active editing world (there is only one), everything else the OS app.</summary>
    public void OpenInNewWindow(AssetEntry asset)
    {
        if (AssetClassifier.IsWorld(asset))
        {
            OpenWorldAsync(asset).FireAndForget();
            return;
        }

        if (AssetClassifier.IsPreviewable(asset))
        {
            viewer.OpenInNewWindow(asset);
            return;
        }

        if (TryOpenInCoder(asset))
            return;

        OpenExternally(asset);
    }

    // Opens a C++ script (or its header) or a GLSL/OpenGL shader in the in-Studio Coder editor (which brings a
    // C++ file's header + source pair as tabs); returns false for anything Coder shouldn't own (plain JSON/.meta
    // data, unknown types) so the caller falls back to the OS default app.
    private bool TryOpenInCoder(AssetEntry asset)
    {
        var language = ScriptLanguages.ForPath(asset.Path);
        if (!asset.IsScript && language != ScriptLanguages.Cpp && language != ScriptLanguages.Glsl)
            return false;

        var relative = asset.Path.Replace('/', Path.DirectorySeparatorChar);
        coder.OpenScript(Path.Combine(project.Path, relative));
        return true;
    }

    // Activates the world engine-side; the World Viewer renders the active world, so it re-renders the
    // opened world on its own — no viewport reload needed here.
    private async Task OpenWorldAsync(AssetEntry asset)
    {
        var opened = await engine
            .SendCommandAsync(EngineCommands.WorldOpen, new { AssetId = asset.Id })
            .ContinueOnAnyContext();
        if (!opened)
            ReportError($"Couldn't open world '{asset.Name}' ({asset.Path}): {opened.Error}");
    }

    // Resolves the engine-normalized project-relative path to an absolute OS path and hands it to the
    // OS default app.
    private void OpenExternally(AssetEntry asset)
    {
        var relative = asset.Path.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.Combine(project.Path, relative);
        var opened = FileOpen.OpenWithDefaultApp(full);
        if (!opened)
            ReportError($"Couldn't open '{asset.Name}' ({asset.Path}): {opened.Error}");
    }

    // Surfaces a failure to the user (a popup) as well as the log — opening an asset should never fail
    // silently. Marshalled to the UI thread since the world route runs off the RPC lane.
    private void ReportError(string message)
    {
        log.Error(message);
        Dispatch.To(DispatchContext.UI, () => popups.ErrorAsync("Open Asset", message).FireAndForget());
    }
}
