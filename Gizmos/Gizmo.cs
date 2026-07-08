using Newtonsoft.Json.Linq;
using Toybox.Studio.AppHosting;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Gizmos;

/// <summary>
/// The editor's gizmo overlay, as call sites use it: get-or-create a retained <see cref="GizmoLayer"/>
/// by name and assign its drawing (or use <see cref="Draw"/> for the build-and-commit one-liner). One
/// instance, created by the composition root; it owns every layer, binds each to the <see cref="SyncHub"/>
/// on creation, and re-pushes them all whenever the engine connection comes (back) up — the engine's
/// layer store is process state, so a relaunched engine starts empty.
/// </summary>
public sealed class Gizmo : EventSubscriber, IEventHandler<ConnectionChanged>
{
    private readonly SyncHub _sync;
    private readonly Logger _log;
    private readonly object _gate = new();
    private readonly Dictionary<string, GizmoLayer> _layers = [];

    public Gizmo(SyncHub sync, Logger log, EventDispatcher events) : base(events)
    {
        _sync = sync;
        _log = log;
    }

    /// <summary>The named layer, created (empty, visible, every editor view) on first use.</summary>
    public GizmoLayer Layer(string name)
    {
        lock (_gate)
        {
            if (_layers.TryGetValue(name, out var existing))
                return existing;

            var layer = new GizmoLayer(name);
            layer.Bind(_sync);
            _layers[name] = layer;
            return layer;
        }
    }

    /// <summary>Builds a drawing and commits it as the named layer's content, in one call.</summary>
    public void Draw(string name, Action<GizmoRenderer> draw)
    {
        var drawing = new GizmoRenderer();
        draw(drawing);
        Layer(name).Ops = drawing.Build();
    }

    /// <summary>Drops the named layer on both sides; a no-op for an unknown name.</summary>
    public void Remove(string name)
    {
        GizmoLayer? layer;
        lock (_gate)
            _layers.Remove(name, out layer);
        if (layer is null)
            return;

        layer.Unbind();
        RemoveFromEngineAsync(name).FireAndForget();
    }

    public void Handle(in ConnectionChanged evt)
    {
        if (evt.State != ConnectionState.Connected)
            return;

        GizmoLayer[] layers;
        lock (_gate)
            layers = [.. _layers.Values];
        foreach (var layer in layers)
            RepushAsync(layer).FireAndForget();
    }

    private async Task RemoveFromEngineAsync(string name)
    {
        var payload = new JObject { ["address"] = GizmoLayer.AddressFor(name) };
        var result = await _sync.Engine.SendCommandAsync(EngineCommands.GizmoRemove, payload).ContinueOnAnyContext();
        if (!result)
            _log.Warning($"Removing gizmo layer '{name}' from the engine failed: {result.Error}");
    }

    // The layer's full synced body, resent value by value — the same {address, key, value} payloads its
    // property pushes send, so the engine handler sees one shape regardless of how a value arrives.
    private async Task RepushAsync(GizmoLayer layer)
    {
        foreach (var (key, value) in layer.Serialize())
        {
            if (value is null)
                continue;

            var payload = new JObject
            {
                ["address"] = GizmoLayer.AddressFor(layer.Name),
                ["key"] = key,
                ["value"] = value,
            };
            var result = await _sync.Engine
                .SendCommandAsync(EngineCommands.GizmoSet, payload)
                .ContinueOnAnyContext();
            if (!result)
            {
                _log.Warning($"Re-pushing gizmo layer '{layer.Name}' failed: {result.Error}");
                return;
            }
        }
    }
}
