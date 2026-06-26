# Toybox Studio Agent Guide

Operational guide for AI agents working in the Toybox Studio repository. Studio is the C#/Avalonia (MVVM, .NET 10) editor that drives the C++ Toybox engine over RPC. AI-generated code is held to the **same standards as human-written code** and is reviewed with great care — see [`docs/Contributing.md`](docs/Contributing.md).

## Standards

**Strictly follow [`docs/CodeStandards.md`](docs/CodeStandards.md).** It is the single source of truth for C#/Avalonia engineering policies, MVVM rules, file/class layout, and formatting. Do not restate those rules here — keep `docs/` up to date when making sweeping architectural changes.

Most-violated reminders (full rules live in CodeStandards):

- **MVVM only — no code-behind routing.** No view-model bootstrap or event wiring in `.axaml.cs`. Use commands, attached behaviors (`Widgets/Behaviors/`), control subclasses, and services instead.
- **One class per `.cs`, one widget per `.axaml`.** Don't mash multiple types into a file.
- **Don't suffix shared parent classes with `Base`.** Name a type for what it is (e.g. `DropdownPropertyViewModel`, not `PropertyViewModelBase`).
- **Return `Result`/`Result<T>` for expected failures** (RPC calls, project/file operations) instead of throwing; the build treats warnings as errors and nullable is enabled.
- **Prefer existing editor utilities and the simplest direct solution** over new abstractions. Widgets are self-contained; avoid god classes.
- **Compiled bindings are on by default** — bindings must be statically resolvable (`x:DataType`).

## Repository Map

Tiers; namespaces follow folders. Lower tiers never depend on higher ones.

- `Utils/` — pure, dependency-free helpers: `Result.cs` (the failure type used everywhere), `Dispatch.cs`/`DispatchContext.cs` (UI-thread marshalling), `Contrast.cs` (WCAG contrast maths), `IListenable.cs` (the change-notification contract). Extension classes live in `Utils/Extensions/`: `ColorExtensions.cs` (`Color.Blend`/`WithAlpha`), `ListExtensions.cs` (`ObservableCollection.Reconcile`), `ListenableExtensions.cs` (`IListenable.Listen` — react-now-and-on-change subscription, implemented by `SettingsManager` and `AssetCatalog`).
- `Services/` — editor behavior, by sub-namespace. Plain data types live beside the construct that mainly produces or owns them (there is no separate `Models/` tier).
  - `Rpc/` — the **generic transport**, peer-agnostic: `RpcClient.cs` (owns the newline-delimited JSON-over-loopback-TCP `JsonRpc` connection, connect-with-retry, the `InvokeAsync`/`NotifyAsync` request/notification primitives, and the `Disconnected` event — knows nothing about the engine's methods), `RpcHandlers.cs` (the handle a caller uses at connect time to register inbound handlers before listening starts, wrapping `JsonRpc` so callers don't depend on StreamJsonRpc directly), `RpcCall.cs` (a data-driven call — method + params + notify-vs-await — run by `EngineRpc.RunAsync`; the building block of a toolbar `ToolCommand`).
  - `EngineApi/` — `EngineRpc.cs` (the **engine connection, and the single channel every engine call is piped through**: a thin engine-specific facade over an `RpcClient` — does the `editor.hello` handshake, surfaces the engine's inbound notifications as typed events, fronts the engine-lifecycle/viewport calls, and exposes the internal `InvokeAsync`/`NotifyAsync` forwarders the domain constructs build on. It enforces safety so callers don't have to: every request is **guarded** (errors → failure `Result`) and **bounded** by a default timeout, so a hung engine never parks an editor op — callers pass `CancellationToken.None` safely and only need a token to cancel early. VMs never hold the raw client), `Session.cs` (launch-owned vs attach, ping loop, watchdog, teardown — delegates the native build to `ProjectBuilder`), `Locator.cs` (finds the engine checkout), `EngineWatcher.cs`, `InstanceDetector.cs`, `ViewportStream.cs` (GPU surface streaming), `JsonParser.cs` (describe-reply → snapshots/`PropertyNode`), `PropertyNode.cs` (the parsed property-tree node the grid binds to — lives at this tier, not under `Widgets/`, so `Services/World/` snapshots can reference it).
  - `World/` — `WorldManager.cs` (the **"World" construct**: snapshot/refresh + world-dirty + save + `CreateEntityAsync` + an `Entity(id)` factory), `Entity.cs`/`Component.cs` (lightweight identity-only handles fronting the per-entity / per-component+property RPCs), `WorldSelection.cs`. The immutable data counterparts (`WorldDescription`/`EntityDescription`/`ComponentDescription`) live beside them here.
  - `Project/` — `ProjectManager.cs`, `CMakeCompiler.cs` (in-tree engine build), `ProjectBuilder.cs` (owns the native build/ship orchestration — `BuildAsync`/`ShipAsync` — separate from the `Session` that runs the result; serializes builds and raises `BuildingChanged`), `AssetCatalog.cs`, `Asset.cs` (the asset record pickers and the grid bind to).
  - `Settings/` — `SettingsManager.cs` (the single owner of `EditorSettings`: loads it, exposes `.Settings`, owns `SaveAsync` + change notification via `IListenable`); `EditorSettings.cs` (the persisted editor-wide config data, `~/.toybox/EditorSettings.json`); `EngineSettings.cs` (the construct fronting the engine's settings RPCs: `app.describeSettings` schema + the generic lean asset save).
  - `Logging/` — `Logger.cs` (unified editor + engine log), `LogFile.cs` (rotated `~/.toybox/Logs/TbxStudio.log`).
  - `Theming/` — `ThemeManager.cs` (facade) + `ThemeRepository.cs` + `ThemeApplier.cs` + `DockSelectorTinter.cs`, `Theme.cs`, `ColorGradient.cs`.
  - `Dialogs/` — file/asset pickers, popups, OS file reveal.
- `Shell/` — the app frame: `App` composition lives in `App.axaml.cs` (`ConfigureServices` is the DI composition root); `Shell.axaml`, `ShellViewModel.cs`, `SplashWindow`; `Workspace/` is the docking system; `Documents/` is the data-ownership plumbing; `Styles/` holds the split XAML style sheets aggregated by `AppStyles.axaml`.
- `Widgets/` — self-contained panels and reusable controls: `Viewport/` (`CompositionInteropViewport.cs` is the D3D11/WGL interop surface), `WorldTree/`, `EntityInspector/`, `PropertyGrid/` (type-driven; `Core/PropertyViewRegistry.cs` registers per-type widgets), `Console/`, `LogConsole/`, `Settings/`, `Theming/`, `GameView/`, `Toolbar/` (the movable, data-driven toolbar used by both the viewport (transform tools) and the game view (the Play/Stop/Pause transport) — owns `ToolbarLayout`/`ToolbarItem`/`ToolbarEdge`/`GameModeCondition`/`ToolCommand`/`ToolCommandStep` and the `ToolCommandRunner` that runs a tool's command via `EngineRpc.RunAsync`, routing `view.setGizmo`→`GizmoTool` and `editor.play`/`stop`/`togglePause`→`Session`; tools carry a `GameModeCondition` (Any/Off/On) so the transport shows Play while stopped and Stop/Pause while playing), `Status/`, `Ghost/`, `Searching/`, `Colors/`, `Behaviors/` (input/interaction glue; the micro-animation behaviors and their shared `MotionTokens.cs` util live in `Behaviors/Animations/`).
- `Assets/Templates/DefaultProject/` — a real on-disk project the editor copies and builds; not an embedded UI resource.

### Key files to read first

- [`App.axaml.cs`](App.axaml.cs) — startup sequence and the DI composition root (`ConfigureServices`).
- [`Services/Rpc/RpcClient.cs`](Services/Rpc/RpcClient.cs) — the generic JSON-RPC transport (connection, retry, request/notification primitives); peer-agnostic.
- [`Services/EngineApi/EngineRpc.cs`](Services/EngineApi/EngineRpc.cs) — the engine connection: the engine-specific facade over `RpcClient` (handshake + typed notifications). The domain API surface lives on the `WorldManager`/`Entity`/`Component`/`AssetCatalog`/`EngineSettings` constructs that call its primitives.
- [`Services/EngineApi/Session.cs`](Services/EngineApi/Session.cs) — engine process lifetime (launch/attach/teardown).
- [`Shell/Workspace/DockableAttribute.cs`](Shell/Workspace/DockableAttribute.cs) — how panels are declared and auto-registered.
- [`Widgets/PropertyGrid/Core/PropertyViewModelFactory.cs`](Widgets/PropertyGrid/Core/PropertyViewModelFactory.cs) — how typed JSON becomes per-type property widgets.
- [`docs/Architecture.md`](docs/Architecture.md) — the system narrative tying all of the above together.

## Build / Test / Run

```bash
# Restore + build (warnings are errors)
dotnet build Toybox.Studio.slnx

# Run; a Debug build enables the Avalonia dev tools (F12)
dotnet run --project Toybox.Studio.csproj
```

Build artifacts go under `build/`. There is no unit-test project yet; verification is by building clean and running the app (see below).

The editor compiles and launches the engine itself. A Debug Studio drives a Debug engine, a Release Studio a Release engine. Studio needs a located engine checkout — if the `Locator` can't find one, set the **Engine path** in **Settings (⚙)**.

## Verification

- **Always build clean first.** `TreatWarningsAsErrors` is on; a warning fails the build. Fix all warnings.
- **Run the app and watch the logs.** Launch Studio, let it reach "Ready.", exercise the change, then shut down cleanly. Inspect `~/.toybox/Logs/TbxStudio.log` (the same stream shown in the in-app console) — resolve any logged warnings or errors and re-test until the log is clean.
- **Verify UI/rendering changes visually.** For viewport, theming, or layout changes, run the app and confirm the result on screen; a clean build alone is not proof for visual work.
- **Mind teardown.** Engine-session and plugin-owned resources have strict teardown ordering; if you touch `Session`, `EngineRpc`, or the viewport interop, verify a clean launch *and* shutdown with no orphaned engine process and no errors in the log.
