# Toybox Studio Agent Guide

Operational guide for AI agents working in the Toybox Studio repository. Studio is the C#/Avalonia (MVVM, .NET 10) editor that drives the C++ Toybox engine over RPC. AI-generated code is held to the **same standards as human-written code** and is reviewed with great care — see [`Documentation/Contributing.md`](Documentation/Contributing.md).

## Standards

**Strictly follow [`Documentation/CodeStandards.md`](Documentation/CodeStandards.md).** It is the single source of truth for C#/Avalonia engineering policies, MVVM rules, file/class layout, and formatting. Do not restate those rules here — keep `Documentation/` up to date when making sweeping architectural changes.

Most-violated reminders (full rules live in CodeStandards):

- **MVVM only — no code-behind routing.** No view-model bootstrap or event wiring in `.axaml.cs`. Use commands, attached behaviors (`Behaviors/`), control subclasses, and services instead.
- **One class per `.cs`, one widget per `.axaml`.** Don't mash multiple types into a file.
- **Don't suffix shared parent classes with `Base`.** Name a type for what it is (e.g. `DropdownPropertyViewModel`, not `PropertyViewModelBase`).
- **Return `Result`/`Result<T>` for expected failures** (RPC calls, project/file operations) instead of throwing; the build treats warnings as errors and nullable is enabled.
- **Prefer existing editor utilities and the simplest direct solution** over new abstractions. Panels are self-contained; avoid god classes.
- **Compiled bindings are on by default** — bindings must be statically resolvable (`x:DataType`).

## Repository Map

**Flat feature folders at the repo root** — there is no longer a `Services/` or `Widgets/` tier folder. Namespaces follow folders exactly (`Toybox.Studio.<Feature>`, e.g. `Toybox.Studio.EngineApi`). A feature folder co-locates its model, view-model, and view (the `Entity` / `EntityViewModel` / `EntityView` shape); plain data types live beside the construct that owns them (there is no separate `Models/` tier). The old tier *dependency direction* — pure `Utils/` → engine/domain services → `Shell/` → UI panels — remains a design convention (lower never depends on higher), just no longer a folder boundary.

**Naming**: view-models end in `ViewModel`, views end in `View` (dialogs/windows keep the `*Dialog`/`*Window` sub-convention), models are bare (`Entity`, not `EntityModel`). A type must not share the name of its own folder/namespace leaf (that shadows the type from sibling namespaces) — hence `Worlds/`, `Clipboards/`, `ColorPickers/`, and `Game/` are named to avoid the clash with the `World`/`Clipboard`/`ColorPicker`/`GameView` types.

Foundation:

- `Utils/` — pure, dependency-free helpers: `Result.cs` (the failure type used everywhere), `Dispatch.cs`/`DispatchContext.cs` (UI-thread marshalling), `Contrast.cs` (WCAG contrast maths), `Colors.cs` (the palette constants, paired with the `PaletteColor` enum), `IListenable.cs` (the change-notification contract). Extension classes live in `Utils/Extensions/`.

Engine, domain & editor services (formerly `Services/`):

- `Rpc/` — the **generic transport**, peer-agnostic: `RpcClient.cs` (newline-delimited JSON-over-loopback-TCP `JsonRpc`, connect-with-retry, `InvokeAsync`/`NotifyAsync` primitives, `Disconnected` event — knows nothing about the engine's methods), `RpcHandlers.cs`, `RpcCall.cs` (a data-driven call run by `EngineRpc.RunAsync`; the building block of a toolbar `ToolCommand`).
- `EngineApi/` — `EngineRpc.cs` (the **engine connection**: a thin engine-specific facade over an `RpcClient` — the `editor.hello` handshake, typed inbound-notification events, engine-lifecycle/viewport calls, and the `InvokeAsync`/`NotifyAsync` forwarders the domain builds on; every request is **guarded** → failure `Result` and **bounded** by a default timeout, so callers pass `CancellationToken.None` safely; VMs never hold the raw client), `Session.cs` (launch-owned vs attach, ping loop, watchdog, teardown — delegates the native build to `ProjectBuilder`), `EngineLocator.cs`, `EngineWatcher.cs`, `InstanceDetector.cs`, `ViewportStream.cs`, plus the `EngineSyncedObject` base + `[EngineSync]` attribute the source generator drives (`WireKeys.cs`/`WireTypes.cs` are the shared wire vocabulary; the generator lives in the sibling `../Studio.Generators` project and keys on the `Toybox.Studio.EngineApi.EngineSyncAttribute` metadata name).
- `Ecs/` — the entity/component domain **and** its view-models together: `Entity.cs`/`Component.cs` (engine-synced handles), the concrete component types under `Ecs/Components/` (`Transform`, `Renderer`, `Camera`, lights, colliders/triggers, …), `ComponentCatalog.cs`, the collection converters, and the `EntityViewModel`/`ComponentViewModel`/`ScriptContainerViewModel` that the world tree and inspector bind to.
- `Worlds/` — the world/scene + editor-session runtime: `World.cs` (the world asset, `: AssetData`), `WorldSelection.cs`, `GameState.cs`/`PlaySync.cs` (play transport), and the gizmo plumbing (`GizmoTool`/`GizmoSync`/`GizmoToolbarBridge`/`SelectionSync`).
- `Project/` — `ProjectManager.cs`, `CMakeCompiler.cs`, `ProjectBuilder.cs` (native build/ship orchestration, separate from the `Session` that runs the result), `AssetCatalog.cs` (the handle database + reflection-discovered `AssetTypes`), `AssetFactory.cs`, `Asset.cs` (the abstract asset handle that IS the asset API — static `Asset.For`/`Asset.Of` construction, instance `Load/Save/Open/Rename/Delete/Duplicate/Copy`, `CreateAsync` + `Creatable`, `CanPaste`/`Paste`), `AssetServices.cs` (the DI singleton bundling the services a handle needs), and `Project/Assets/` (the strongly-typed `Asset`/`AssetData` subclasses — `Material`/`MaterialInstance`/`Texture`/`Model`/`Script`/`Shader`/`UnknownData` + the `ProjectSettings` settings asset).
- `Settings/` — merges the settings service and its UI: `SettingsManager.cs` (owns both the editor's `EditorSettings` and the project's `ProjectSettings`, with `SaveAsync`/`ReloadProjectAsync` + `IListenable` notification), `EditorSettings.cs`, and the `SettingsView`/`SettingsViewModel`/`EditorSettingsViewModel`/`ProjectSettingsViewModel` panel.
- `Theming/` — merges the theming service and its UI: `ThemeManager.cs` (facade) + `ThemeRepository.cs` + the appliers + `Theme.cs`/`ColorGradient.cs`, plus the `ThemeCreatorWindow`/`ThemeCreatorViewModel`.
- `Dialogs/` — merges the dialog services (file/asset pickers, popups, OS reveal) with the MVVM dialog widgets (`ConfirmDialog`, `MessageDialog`, `AssetPickerDialog`, …).
- `Scripting/` (`ScriptService`/`ScriptCatalog`/`ScriptDocument`), `Clipboards/` (the `Clipboard` service — a JSON envelope over the OS clipboard that copies plain values and engine-synced object bodies with no per-kind wrapper types), `Favorites/`, `Logging/` (`Logger.cs` unified editor+engine log, `LogFile.cs`).

App frame & UI (formerly `Shell/` + `Widgets/`):

- `Shell/` — `App.axaml.cs` (startup + `ConfigureServices` DI composition root), `Shell.axaml`/`ShellViewModel.cs`, `SplashWindow`; `Workspace/` is the docking system; `Styles/` holds the split XAML sheets aggregated by `AppStyles.axaml`.
- Self-contained panels & reusable controls, each its own top-level folder: `Viewport/` (`CompositionInteropViewport.cs` is the D3D11/WGL interop surface), `WorldTree/` (`WorldTreeView`/`WorldTreeViewModel`), `EntityInspector/` (`EntityInspectorView`/`EntityInspectorViewModel`), `PropertyGrid/` (type-driven; `Core/PropertyViewRegistry.cs` registers per-type widgets), `Console/`, `LogConsole/`, `Game/` (`GameView`/`GameViewModel` — the Play/Stop/Pause game panel), `Toolbar/` (the movable, data-driven toolbar shared by the viewport transform tools and the game transport; owns `ToolCommand`/`ToolCommandRunner`, routing `view.setGizmo`→`GizmoTool` and `editor.play`/`stop`/`togglePause`→`Session`), `AssetBrowser/`, `AssetViewer/`, `ContextMenu/`, `Status/`, `Ghost/`, `Searching/`, `ColorPickers/` (the color-editing controls), `Overlays/`, `ScriptEditor/` (the Monaco-backed editor; its vendored bundle is `ScriptEditor/Monaco/`, copied to output), `Behaviors/` (input/interaction glue; the micro-animation behaviors + `MotionTokens.cs` live in `Behaviors/Animations/`).
- `Resources/Templates/DefaultProject/` — a real on-disk project the editor copies and builds; not an embedded UI resource (`Resources/` also holds `Icons/` and the `AssetViewer/` preview assets, per the `AvaloniaResource`/`None` globs in the csproj).

### Key files to read first

- [`App.axaml.cs`](App.axaml.cs) — startup sequence and the DI composition root (`ConfigureServices`).
- [`Rpc/RpcClient.cs`](Rpc/RpcClient.cs) — the generic JSON-RPC transport (connection, retry, request/notification primitives); peer-agnostic.
- [`EngineApi/EngineRpc.cs`](EngineApi/EngineRpc.cs) — the engine connection: the engine-specific facade over `RpcClient` (handshake + typed notifications). The domain API surface lives on the `Entity`/`Component`/`AssetCatalog` constructs and the static `Asset` API that call its primitives.
- [`EngineApi/Session.cs`](EngineApi/Session.cs) — engine process lifetime (launch/attach/teardown).
- [`Shell/Workspace/DockableAttribute.cs`](Shell/Workspace/DockableAttribute.cs) — how panels are declared and auto-registered (a `[Dockable]` `XxxView` binds to `XxxViewModel` in the same namespace, or an explicit `ViewModel = typeof(...)`).
- [`PropertyGrid/Core/PropertyViewModelFactory.cs`](PropertyGrid/Core/PropertyViewModelFactory.cs) — how typed JSON becomes per-type property widgets.
- [`Documentation/Architecture.md`](Documentation/Architecture.md) — the system narrative tying all of the above together.

## Build / Test / Run

```bash
# Restore + build (warnings are errors)
dotnet build Toybox.Studio.slnx

# Run; a Debug build enables the Avalonia dev tools (F12)
dotnet run --project Toybox.App.csproj
```

Build artifacts go under `build/`. There is no unit-test project yet; verification is by building clean and running the app (see below).

The editor compiles and launches the engine itself. A Debug Studio drives a Debug engine, a Release Studio a Release engine. Studio needs a located engine checkout — if the `Locator` can't find one, set the **Engine path** in **Settings (⚙)**.

## Verification

- **Always build clean first.** `TreatWarningsAsErrors` is on; a warning fails the build. Fix all warnings.
- **Run the app and watch the logs.** Launch Studio, let it reach "Ready.", exercise the change, then shut down cleanly. Inspect `~/.toybox/Logs/TbxStudio.log` (the same stream shown in the in-app console) — resolve any logged warnings or errors and re-test until the log is clean.
- **Verify UI/rendering changes visually.** For viewport, theming, or layout changes, run the app and confirm the result on screen; a clean build alone is not proof for visual work.
- **Mind teardown.** Engine-session and plugin-owned resources have strict teardown ordering; if you touch `Session`, `EngineRpc`, or the viewport interop, verify a clean launch *and* shutdown with no orphaned engine process and no errors in the log.
