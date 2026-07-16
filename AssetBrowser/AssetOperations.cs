using Newtonsoft.Json.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System;
using Toybox.Studio.Clipboards;
using Toybox.Studio.Dialogs;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Projects;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Composition;

namespace Toybox.Studio.AssetBrowser;

/// <summary>
/// The asset browser's CRUD operations, acting on a catalog <see cref="AssetEntry"/>. An asset is just data on
/// disk (its payload file plus a <c>.meta</c> sidecar carrying its engine-owned id), so rename/duplicate/delete
/// are file moves/copies/deletes under the project root, coordinated with the engine: a delete tells the engine
/// to forget the id (its registry lists from memory, not disk, so a refresh would otherwise still show the gone
/// asset), and a duplicate stamps the copy's <c>.meta</c> with a freshly-minted id so it's a distinct asset, not
/// an alias. Copy/paste ride the shared <see cref="Clipboard"/> (a paste duplicates the copied asset). Every
/// operation ends by refreshing the <see cref="AssetCatalog"/>. Built-in engine/bridge assets have no project
/// file and are not editable here.
/// </summary>
public sealed class AssetOperations
{
    private readonly Project _project;
    private readonly AssetCatalog _catalog;
    private readonly Engine _engine;
    private readonly Clipboard _clipboard;
    private readonly Popups _popups;
    private readonly ViewModelFactory _viewModels;

    public AssetOperations(
        Project project, AssetCatalog catalog, Engine engine, Clipboard clipboard, Popups popups,
        ViewModelFactory viewModels)
    {
        _project = project;
        _catalog = catalog;
        _engine = engine;
        _clipboard = clipboard;
        _popups = popups;
        _viewModels = viewModels;
    }

    /// <summary>Prompts for a new name and renames the asset's files, keeping its id so references stay stable.</summary>
    public async Task RenameAsync(AssetEntry entry)
    {
        var newName = await _popups
            .ShowAsync(_viewModels.Create<TextPromptPopupViewModel>(
                "Rename Asset", "New name", entry.Name, false, "Rename"))
            .ContinueOnSameContext();
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, entry.Name, StringComparison.Ordinal))
            return;

        var source = Absolute(entry);
        var directory = Path.GetDirectoryName(source)!;
        var extension = Path.GetExtension(source);
        var target = Path.Combine(directory, newName + extension);
        if (File.Exists(target))
        {
            await _popups.ErrorAsync("Rename Asset", $"'{newName}{extension}' already exists.")
                .ContinueOnSameContext();
            return;
        }

        try
        {
            File.Move(source, target);
            if (File.Exists(Meta(source)))
                File.Move(Meta(source), Meta(target));
        }
        catch (Exception exception)
        {
            await _popups.ErrorAsync("Couldn't rename asset", exception.Message).ContinueOnSameContext();
            return;
        }

        await _catalog.RefreshAsync().ContinueOnAnyContext();
    }

    /// <summary>Duplicates the asset beside itself under a fresh-id <c>…_copy</c> name.</summary>
    public async Task DuplicateAsync(AssetEntry entry)
    {
        var source = Absolute(entry);
        if (!File.Exists(source))
            return;

        var directory = Path.GetDirectoryName(source)!;
        var stem = Path.GetFileNameWithoutExtension(source);
        var extension = Path.GetExtension(source);
        var target = UniqueCopyPath(directory, stem, extension);

        var newId = await NewIdAsync().ContinueOnAnyContext();
        if (newId == 0)
        {
            await _popups.ErrorAsync("Couldn't duplicate asset", "The engine could not mint an asset id.")
                .ContinueOnSameContext();
            return;
        }

        try
        {
            File.Copy(source, target);
            var sourceMeta = Meta(source);
            if (File.Exists(sourceMeta))
                File.WriteAllText(Meta(target), FreshMeta(sourceMeta, newId));
        }
        catch (Exception exception)
        {
            await _popups.ErrorAsync("Couldn't duplicate asset", exception.Message).ContinueOnSameContext();
            return;
        }

        await _catalog.RefreshAsync().ContinueOnAnyContext();
    }

    /// <summary>Deletes the asset's files after confirmation, tells the engine to forget its id, then refreshes.</summary>
    public async Task DeleteAsync(AssetEntry entry)
    {
        var confirmed = await _popups
            .ConfirmAsync("Delete Asset", $"Delete '{entry.Name}'? This removes the file from disk.")
            .ContinueOnSameContext();
        if (confirmed != Confirmation.Yes)
            return;

        var source = Absolute(entry);
        try
        {
            if (File.Exists(source))
                File.Delete(source);
            if (File.Exists(Meta(source)))
                File.Delete(Meta(source));
        }
        catch (Exception exception)
        {
            await _popups.ErrorAsync("Couldn't delete asset", exception.Message).ContinueOnSameContext();
            return;
        }

        // The engine lists from its in-memory registry, so forget the id before the refresh below.
        await _engine
            .SendCommandAsync<JObject>(
                EngineCommands.AssetForget, new { Id = entry.Id, entry.Path }, CancellationToken.None)
            .ContinueOnAnyContext();
        await _catalog.RefreshAsync().ContinueOnAnyContext();
    }

    /// <summary>Copies the asset's handle to the clipboard.</summary>
    public Task CopyAsync(AssetEntry entry) => _clipboard.Copy(entry.Handle);

    /// <summary>Whether the clipboard holds an asset handle a <see cref="PasteAsync"/> could duplicate — the
    /// cheap kind-tag check that gates the Paste verb's visibility.</summary>
    public Task<bool> CanPasteAsync() => _clipboard.Has<Handle>();

    /// <summary>Pastes the copied asset: resolves the clipboard handle to its catalog row and duplicates it
    /// (a paste is a duplicate of whatever was copied). A no-op when the clipboard holds no asset, or the
    /// copied asset is gone.</summary>
    public async Task PasteAsync()
    {
        var handle = await _clipboard.Paste<Handle>().ContinueOnAnyContext();
        if (handle is not { Id: not 0UL } valid)
            return;

        if (_catalog.Find(valid.Id) is not { } entry)
        {
            await _popups.ErrorAsync("Paste Asset", "The copied asset is no longer available.")
                .ContinueOnSameContext();
            return;
        }

        await DuplicateAsync(entry).ContinueOnAnyContext();
    }

    /// <summary>Opens the OS file browser with the asset's file selected.</summary>
    public async Task RevealAsync(AssetEntry entry)
    {
        var result = FileReveal.Reveal(Absolute(entry));
        if (!result)
            await _popups.ErrorAsync("Couldn't reveal asset", result.Error ?? "The file could not be revealed.")
                .ContinueOnSameContext();
    }

    // The asset's payload file, absolute, under the project root (its engine path is project-relative,
    // forward-slashed).
    private string Absolute(AssetEntry entry) =>
        Path.Combine(_project.Path, entry.Path.Replace('/', Path.DirectorySeparatorChar));

    // An asset's identity sidecar sits beside its payload as "<file>.meta".
    private static string Meta(string file) => file + ".meta";

    private static string UniqueCopyPath(string directory, string stem, string extension)
    {
        var target = Path.Combine(directory, $"{stem}_copy{extension}");
        var n = 2;
        while (File.Exists(target))
            target = Path.Combine(directory, $"{stem}_copy{n++}{extension}");
        return target;
    }

    private async Task<ulong> NewIdAsync()
    {
        var reply = await _engine
            .SendCommandAsync<JObject>(EngineCommands.AssetNewId, null, CancellationToken.None)
            .ContinueOnAnyContext();
        return !reply || reply.Value is not { } body ? 0 : body.Value<ulong?>("id") ?? 0;
    }

    // A copy of an existing .meta carrying a fresh id (so a duplicate is its own asset), preserving any other
    // identity fields; falls back to a minimal sidecar if the source can't be read.
    private static string FreshMeta(string sourceMetaPath, ulong id)
    {
        try
        {
            var meta = JObject.Parse(File.ReadAllText(sourceMetaPath));
            meta["id"] = id;
            return meta + "\n";
        }
        catch (Exception)
        {
            return $"{{\n    \"id\": {id},\n    \"version\": 1\n}}\n";
        }
    }
}
