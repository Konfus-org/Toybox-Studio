using Dock.Model.Controls;
using Dock.Serializer;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Reflection;
using Toybox.Studio.Logging;
using Toybox.Studio.Settings;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Docking;

/// <summary>
/// Persists dock layouts as JSON under the open project's <c>.toybox/Layouts</c>, purely by name — which name means what
/// (the main window's auto-saved working layout, the user's Window ▸ Save Layout names) is the
/// <see cref="LayoutManager"/>'s business. The whole RootDock is serialized, so floating windows ride
/// along with the docked tree. A missing or corrupt file yields null so the caller can fall back to the
/// built-in default — mirroring how <see cref="SettingsManager"/> recovers from a bad settings file.
/// Only the file I/O is async: dock models are UI-affine <c>StyledElement</c>s, so the JSON conversion
/// runs on the caller's (UI) thread — a save captures the layout as of the call, before its first await.
/// </summary>
public sealed class LayoutStore
{
    // Mirrors Dock's own DockSerializer settings (TypeNameHandling.Objects + reference preservation +
    // KeyValuePairConverter), but swaps in a resolver that strips DataContext/Resources so a persisted
    // layout can't drag a live view-model along — see ContractResolver below.
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        Formatting = Formatting.Indented,
        TypeNameHandling = TypeNameHandling.Objects,
        PreserveReferencesHandling = PreserveReferencesHandling.Objects,
        ReferenceLoopHandling = ReferenceLoopHandling.Serialize,
        ContractResolver = new ContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters = { new KeyValuePairConverter() },
    };

    private readonly Logger _log;
    private readonly ProjectPaths _paths;

    public LayoutStore(Logger log, ProjectPaths paths)
    {
        _log = log;
        _paths = paths;
    }

    public Task<IRootDock?> LoadAsync(string name) => ReadAsync(PathFor(name));

    public Task SaveAsync(string name, IRootDock layout) => WriteAsync(PathFor(name), layout);

    /// <summary>Names of every stored layout, the working layout included.</summary>
    public Task<IReadOnlyList<string>> ListAsync() => Task.Run(ListNames);

    private IReadOnlyList<string> ListNames()
    {
        if (!Directory.Exists(_paths.LayoutsDirectory))
            return [];

        return Directory.EnumerateFiles(_paths.LayoutsDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IRootDock?> ReadAsync(string path)
    {
        // A missing layout is the normal first-run case, so stay silent; a present-but-unreadable one
        // (corrupt or stale schema) is worth a breadcrumb before falling back to the default.
        if (!File.Exists(path))
            return null;

        try
        {
            // Resume on the caller's context: deserializing constructs the UI-affine dock models.
            var json = await File.ReadAllTextAsync(path).ContinueOnSameContext();
            return JsonConvert.DeserializeObject<IRootDock>(json, SerializerSettings);
        }
        catch (Exception exception)
        {
            _log.Warning($"Ignoring unreadable dock layout '{Path.GetFileName(path)}': {exception.Message}");
            return null;
        }
    }

    private async Task WriteAsync(string path, IRootDock layout)
    {
        try
        {
            // Serialized before the first await — see the class note; only the write leaves the caller.
            var json = JsonConvert.SerializeObject(layout, SerializerSettings);
            Directory.CreateDirectory(_paths.LayoutsDirectory);
            await File.WriteAllTextAsync(path, json).ContinueOnAnyContext();
        }
        catch (Exception exception)
        {
            // Failing to persist a layout must never take the editor down on exit, but log why it was lost.
            _log.Warning($"Failed to persist dock layout '{Path.GetFileName(path)}': {exception.Message}");
        }
    }

    private string PathFor(string name)
    {
        var safe = string.Concat(name.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));
        if (string.IsNullOrWhiteSpace(safe))
            safe = "layout";
        return Path.Combine(_paths.LayoutsDirectory, safe + ".json");
    }

    /// <summary>
    /// Dock's serializer settings with two changes. First, the live-content properties Avalonia adds to every
    /// dock model (<c>DataContext</c> and <c>Resources</c>) are dropped from the contract. Dock models are
    /// <c>StyledElement</c>s, so the main window's view-model data-context inherits down onto the RootDock and
    /// gets persisted; on load the serializer then tries to reconstruct the view-model through its parameterized
    /// constructor and throws (the layout becomes "unreadable" and the editor silently falls back to the
    /// default). Removing the properties from the contract means they are neither written on save nor read back
    /// on load, so a layout round-trips as pure structure — persisting structure and ids but never live content.
    ///
    /// Second, every property's getter is guarded: some Avalonia/Dock model properties (e.g. a <c>ToolDock</c>'s
    /// <c>CanUpdateItemsSourceOnUnregister</c>) throw when read outside a registered control context, which would
    /// otherwise abort the whole save. A throwing getter yields <c>null</c> instead and — with the store's
    /// <c>NullValueHandling.Ignore</c> — is simply omitted, so one ornery runtime flag can't break persistence.
    /// </summary>
    private sealed class ContractResolver : ListContractResolver
    {
        private static readonly HashSet<string> LiveContentProperties =
            new(StringComparer.Ordinal) { "DataContext", "Resources" };

        public ContractResolver() : base(typeof(ObservableCollection<>))
        {
        }

        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            return base.CreateProperties(type, memberSerialization)
                .Where(property =>
                    !LiveContentProperties.Contains(property.UnderlyingName ?? property.PropertyName ?? string.Empty))
                .ToList();
        }

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            if (property.Readable && property.ValueProvider is { } inner)
                property.ValueProvider = new SafeValueProvider(inner);
            return property;
        }

        // Wraps a property's value provider so a getter that throws (a model flag invalid outside a live
        // control) returns null rather than aborting serialization. Writes pass straight through.
        private sealed class SafeValueProvider(IValueProvider inner) : IValueProvider
        {
            public object? GetValue(object target)
            {
                try
                {
                    return inner.GetValue(target);
                }
                catch
                {
                    return null;
                }
            }

            public void SetValue(object target, object? value) => inner.SetValue(target, value);
        }
    }
}
