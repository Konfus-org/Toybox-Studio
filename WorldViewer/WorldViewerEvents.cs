namespace Toybox.Studio.WorldViewer;

// The event structs the world viewport's tools dispatch. Handlers implement IEventHandler<T> and
// register with the shared EventDispatcher; nobody references the publisher.

/// <summary>The transform tool changed — its mode or its snapping toggle. The viewport toolbars
/// re-read <see cref="WorldViewerTool.Mode"/> / <see cref="WorldViewerTool.SnappingEnabled"/>.</summary>
public readonly record struct WorldViewerToolChanged;

/// <summary>The render layers changed — a collider wireframe toggle, the post-processing toggle, or
/// the active render stage. The viewport toolbars re-read the <see cref="RenderLayers"/> state.</summary>
public readonly record struct RenderLayersChanged;
