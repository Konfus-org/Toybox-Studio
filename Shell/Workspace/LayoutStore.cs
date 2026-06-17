using Toybox.Studio.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dock.Model.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.Project;

namespace Toybox.Studio.Shell.Workspace;

/// <summary>
/// Persists dock layouts as JSON under <c>~/.toybox/Layouts</c>. The whole RootDock is serialized, so
/// floating windows ride along with the docked tree. <c>last.json</c> is the auto-saved working layout
/// restored on the next launch; named layouts (saved/listed here but not yet surfaced in the UI) are the
/// hook for user-created layouts later. A missing or corrupt file yields null so the caller can fall back
/// to the built-in default — mirroring how <see cref="EditorSettings"/> recovers from a bad settings file.
/// </summary>
public sealed class LayoutStore
{
    private static readonly string LayoutsDirectory = Path.Combine(EditorSettings.BaseDirectory, "Layouts");
    private static readonly string LastLayoutPath = Path.Combine(LayoutsDirectory, "last.json");

    // Mirrors Dock's own DockSerializer settings (TypeNameHandling.Objects + reference preservation +
    // KeyValuePairConverter), but swaps in a resolver that strips DataContext/Resources so a persisted
    // layout can't drag a live view-model along — see LayoutContractResolver.
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        Formatting = Formatting.Indented,
        TypeNameHandling = TypeNameHandling.Objects,
        PreserveReferencesHandling = PreserveReferencesHandling.Objects,
        ReferenceLoopHandling = ReferenceLoopHandling.Serialize,
        ContractResolver = new LayoutContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters = { new KeyValuePairConverter() },
    };

    private readonly Logger _log;

    public LayoutStore(Logger log)
    {
        _log = log;
    }

    public IRootDock? LoadLast() => Read(LastLayoutPath);

    public void SaveLast(IRootDock layout) => Write(LastLayoutPath, layout);

    public IRootDock? Load(string name) => Read(PathFor(name));

    public void Save(string name, IRootDock layout) => Write(PathFor(name), layout);

    /// <summary>Names of the user's saved layouts (the <c>last.json</c> working layout is excluded).</summary>
    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(LayoutsDirectory))
            return [];

        return Directory.EnumerateFiles(LayoutsDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && !string.Equals(name, "last", StringComparison.OrdinalIgnoreCase))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IRootDock? Read(string path)
    {
        // A missing layout is the normal first-run case, so stay silent; a present-but-unreadable one
        // (corrupt or stale schema) is worth a breadcrumb before falling back to the default.
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonConvert.DeserializeObject<IRootDock>(File.ReadAllText(path), SerializerSettings);
        }
        catch (Exception exception)
        {
            _log.Warning($"Ignoring unreadable dock layout '{Path.GetFileName(path)}': {exception.Message}");
            return null;
        }
    }

    private void Write(string path, IRootDock layout)
    {
        try
        {
            Directory.CreateDirectory(LayoutsDirectory);
            File.WriteAllText(path, JsonConvert.SerializeObject(layout, SerializerSettings));
        }
        catch (Exception exception)
        {
            // Failing to persist a layout must never take the editor down on exit, but log why it was lost.
            _log.Warning($"Failed to persist dock layout '{Path.GetFileName(path)}': {exception.Message}");
        }
    }

    private static string PathFor(string name)
    {
        var safe = string.Concat(name.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));
        if (string.IsNullOrWhiteSpace(safe))
            safe = "layout";
        return Path.Combine(LayoutsDirectory, safe + ".json");
    }
}
