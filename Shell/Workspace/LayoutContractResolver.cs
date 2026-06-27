using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Dock.Serializer;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Toybox.Studio.Shell.Workspace;

/// <summary>
/// Dock's serializer settings with two changes. First, the live-content properties Avalonia adds to every dock
/// model (<c>DataContext</c> and <c>Resources</c>) are dropped from the contract. Dock models are
/// <c>StyledElement</c>s, so the <see cref="MainWindow"/>'s <c>ShellViewModel</c> data-context inherits
/// down onto the RootDock and gets persisted; on load the serializer then tries to reconstruct the
/// view-model through its parameterized constructor and throws (the layout becomes "unreadable" and the
/// editor silently falls back to the default). Removing the properties from the contract means they are
/// neither written on save nor read back on load, so a layout round-trips as pure structure — matching the
/// store's stated intent of persisting "structure and ids but never the live content."
///
/// Second, every property's getter is guarded: some Avalonia/Dock model properties (e.g. a <c>ToolDock</c>'s
/// <c>CanUpdateItemsSourceOnUnregister</c>) throw when read outside a registered control context, which would
/// otherwise abort the whole save. A throwing getter yields <c>null</c> instead and — with the store's
/// <c>NullValueHandling.Ignore</c> — is simply omitted, so one ornery runtime flag can't break persistence.
/// </summary>
public sealed class LayoutContractResolver : ListContractResolver
{
    private static readonly HashSet<string> LiveContentProperties =
        new(StringComparer.Ordinal) { "DataContext", "Resources" };

    public LayoutContractResolver() : base(typeof(ObservableCollection<>))
    {
    }

    protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
    {
        return base.CreateProperties(type, memberSerialization)
            .Where(property => !LiveContentProperties.Contains(property.UnderlyingName ?? property.PropertyName ?? string.Empty))
            .ToList();
    }

    protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
    {
        var property = base.CreateProperty(member, memberSerialization);
        if (property.Readable && property.ValueProvider is { } inner)
            property.ValueProvider = new SafeValueProvider(inner);
        return property;
    }

    // Wraps a property's value provider so a getter that throws (a model flag invalid outside a live control)
    // returns null rather than aborting serialization. Writes pass straight through.
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
