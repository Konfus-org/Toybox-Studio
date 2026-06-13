using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Dock.Serializer;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Toybox.Studio.Workspace;

/// <summary>
/// Dock's serializer settings with one change: the live-content properties Avalonia adds to every dock
/// model (<c>DataContext</c> and <c>Resources</c>) are dropped from the contract. Dock models are
/// <c>StyledElement</c>s, so the <see cref="MainWindow"/>'s <c>ShellViewModel</c> data-context inherits
/// down onto the RootDock and gets persisted; on load the serializer then tries to reconstruct the
/// view-model through its parameterized constructor and throws (the layout becomes "unreadable" and the
/// editor silently falls back to the default). Removing the properties from the contract means they are
/// neither written on save nor read back on load, so a layout round-trips as pure structure — matching the
/// store's stated intent of persisting "structure and ids but never the live content."
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
}
