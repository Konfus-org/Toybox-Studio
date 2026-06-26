# Toybox Studio Architecture

This document explains how the editor is put together. For coding rules see [`CodeStandards.md`](CodeStandards.md); for the high-level pitch see the [README](../README.md).

## The big idea: Studio drives the engine, it does not contain it

Studio is a .NET/Avalonia application. The Toybox engine is a separate C++ process. The editor:

1. **Builds** the engine in-tree against the open project (CMake/Ninja, via `CMakeCompiler`).
2. **Launches** it as a child process (or **attaches** to an already-running instance).
3. **Connects** over a JSON-RPC channel and issues commands / receives notifications.
4. **Displays** the engine's rendered frames by sharing GPU textures — no pixel copy.

This keeps the editor's UI responsive and decoupled from the engine's frame loop, and means the viewport shows the real engine running your real world.

## Composition & startup

`Program.cs` configures Avalonia and starts the classic desktop lifetime. `App.axaml.cs` is the composition root:

- `App.ConfigureServices` builds a `Microsoft.Extensions.Hosting` generic host and registers every service as a **singleton**. This is the one place services are wired.
- `DockableCatalog.RegisterDockables` reflection-scans the assembly for `[Dockable]` views and auto-registers their view models — so panels are never registered by hand here.
- `StartupAsync` brings the app up step by step behind a splash window, narrating each step through the shared `Logger` (so every startup line lands in both the splash log and `TbxStudio.log`): apply theme → build the workspace (hidden) → locate the engine → start watching for running engines → optionally launch into a world → reveal the main window.

## The engine connection (`Services/Rpc/` + `Services/EngineApi/`)

- **`RpcClient`** (in `Services/Rpc/`) is the **generic transport**: it owns the StreamJsonRpc connection — newline-delimited JSON over a loopback TCP socket — with connect-with-retry, the `InvokeAsync`/`NotifyAsync` request/notification primitives, and a `Disconnected` event (every call returns a `Result` with a helpful message on failure, including "not connected", rather than throwing). It is peer-agnostic — it knows nothing about the engine's methods; a caller registers its inbound handlers at connect time via `RpcHandlers`.
- **`EngineRpc`** (in `Services/EngineApi/`) is the **engine connection, and the single channel every engine call is piped through**: a thin engine-specific facade over an `RpcClient`. It performs the `editor.hello` handshake, surfaces the engine's inbound notifications as typed events, fronts the connection lifecycle / viewport calls that only services make, and exposes the internal `InvokeAsync`/`NotifyAsync` forwarders the domain constructs build on. Safety is enforced here so callers don't have to think about it: every request is **guarded** (errors become a failure `Result`, never an exception) and **bounded** (linked to a default deadline, so a hung or crashed engine can never park an editor operation forever — a caller stays safe passing `CancellationToken.None`, and only needs a token to cancel early or a `timeout` overload to change the bound). Liveness detection is the `Session` watchdog's job; this layer guarantees each individual await completes. Nobody else touches the socket, and VMs never hold the raw `RpcClient`.
- **Domain constructs** front the engine's actual operations so view-models never call the wire directly. The world's entity graph is reached through `WorldManager` (the "World": describe/save/dirty + `CreateEntityAsync` + `Entity(id)`), which vends lightweight identity-only `Entity` handles (rename/enable/global/move/destroy/describe), which in turn vend `Component` handles (set component, get/set/reset property). Assets go through `AssetCatalog` (`asset.describe`, `asset.save`, `editor.listAssets`) and the engine's settings schema through `EngineSettings` (`app.describeSettings`). Each construct calls `EngineRpc`'s primitives; a VM holds a construct, never `EngineRpc`. (Per-property "modified" state is read from the `is_default` flag the describe payload already carries — no separate round-trip.)
- **`Session`** drives the connection lifecycle. It either **owns** an engine process it launched or **attaches** to an existing one (`SessionKind`). It runs a ping loop, relaunches the engine when the open project changes, and tears everything down cleanly. The engine is launched with a "die-together" tie to the editor PID so it never orphans. It delegates the native build to `ProjectBuilder` and surfaces that build's phase through its own busy/compiling state.
- **`Locator`** finds the engine source checkout at startup; **`InstanceDetector`** notices an already-running engine to attach to; **`EngineWatcher`** observes connection/compile/launch state to drive busy/loading UI.
- **`ProjectBuilder`** (under `Services/Project/`) owns the project's native build operations, separate from the `Session` that runs the result: it configures + builds the project via `CMakeCompiler` (`BuildAsync`) and ships a standalone export (`ShipAsync`), serializing builds and raising `BuildingChanged`. Because the engine is built with the project, the editor's build configuration selects the engine binary: Debug↔Debug, Release↔Release.

Notifications flow the other way too: engine log lines stream into the unified `Logger`, and `view.surface` / `view.presented` notifications drive the viewport.

## GPU frame sharing (`Widgets/Viewport/`)

Editor viewport frames are **zero-copy**. The engine renders each view into a shared Direct3D 11 texture; the editor imports that texture via the `WGL_NV_DX_interop2` extension and composites it directly through Avalonia's composition interop (`CompositionInteropViewport`). There is no PBO readback, shared-memory blit, or `WriteableBitmap` round-trip. This path is **Windows-only** and there is no CPU fallback.

Each viewport panel is a non-singleton dockable: every viewport window drives its own engine camera. Input is forwarded to the engine through the viewport input sink (mouse, keyboard via the engine's `InputKey` codes, and a mouse-lock mode for in-game mouselook).

## The docking workspace (`Shell/Workspace/`)

The workspace is data-driven and built on vanilla **Dock.Avalonia** + FluentTheme.

- A panel declares itself with a single **`[Dockable]`** attribute on its View. The attribute carries identity, title, icon, default slot/proportion, ordering, and whether it's a singleton.
- `DockableCatalog` scans for these at startup and turns each into a `DockableDescriptor`, auto-registering the view model into DI, the **Windows menu**, and the **dock layout**. Adding a new panel is: create the widget, add the attribute. Nothing else.
- `LayoutStore` saves and restores the working layout (docked + floating windows) across launches; the layout is persisted on exit.

## The type-driven property grid (`Widgets/PropertyGrid/`)

The inspector and settings editor share one generic, type-driven property grid rather than hand-built forms.

- The engine describes component/data shapes as **typed JSON** — every value is `{ "type": ..., "value": ... }` (the keys are literally `type`/`value`). There is no legacy bare-value reading; data is typed-only.
- `PropertyViewModelFactory` maps each typed value to a **per-type widget** (number, bool, string, enum, color, vector, rotation/Euler, asset/handle/entity pickers, material overrides, …). `PropertyViewRegistry` is where per-type widgets and their backing services are registered (configured once at startup).
- Inline attributes from the engine's `describe` (nested shapes, enum choices, readonly/hidden/category/icon) disambiguate things like quaternion-vs-vec4 and drive widget choice and presentation.
- Edits round-trip back to the engine through the `Component` handle (`SetProperty`/`ResetProperty`/`SetComponent`), and the inspector builds its rows from `EntityDescription`/`ComponentDescription` (the parsed, immutable `world.describe` data the VMs reconcile against, living in `Services/World/`).

## Data ownership & documents (`Shell/Documents/`)

Editors own their data through the `DataOwner` / `IDocumentOwner` pattern:

- A dirty document shows a `*` in its title and a Save/Cancel footer.
- **Settings** is buffered — it edits a copy and commits on save, not per keystroke.
- The **inspector** is live-push, with refresh driven by `InspectorRefreshCoordinator` (it owns the play/selection subscriptions and the play-mode pull) rather than a self-poll.
- The **viewport's** `*` mirrors `WorldDocument`'s world-dirty state.
- File ▸ Save / Save All map to the world save RPC (Ctrl+S / Ctrl+Shift+S).

## Theming (`Services/Theming/` + `Widgets/Theming/`)

- Themes are `Theme.json` palettes stored under `~/.toybox/Themes`. Palette colors are `ColorGradient`s (flat-or-gradient, with JSON back-compat) editable through a live theme creator.
- `ThemeManager` is a thin facade over `ThemeRepository` (load/save), `ThemeApplier` (push colors into Avalonia resources), and `DockSelectorTinter` (dock-specific tinting). Theme-derived neutral brushes are computed in XAML from published `Theme*Color` resources.
- The default base theme is "Claymorphism" (a playful clay look), with refreshed light/dark variants.

## Logging (`Services/Logging/`)

`Logger` is the single editor log. It absorbs both editor messages and engine log lines (forwarded over RPC) into one stream, written to a single rotated `~/.toybox/Logs/TbxStudio.log` and surfaced live in the in-app console. The splash window shows the same stream during startup.

## Conventions that hold across the codebase

- **No code-behind routing** — behavior lives in commands, attached behaviors, control subclasses, and services (see [`CodeStandards.md`](CodeStandards.md)).
- **Namespaces follow folders**; tiers (`Utils` → `Services` → `Shell`/`Widgets`) don't depend upward. Plain data types live beside the service that owns them, not in a separate `Models` tier.
- **Widgets are self-contained** and reach the rest of the app only through injected services.
