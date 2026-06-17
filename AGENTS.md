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

- `Utils/` — pure, dependency-free helpers: `Result.cs` (the failure type used everywhere), `Dispatch.cs`/`DispatchContext.cs` (UI-thread marshalling), `ColorMath.cs`, `Contrast.cs`, `ListReconcile.cs`.
- `Models/` — plain data: `EditorSettings.cs` (persisted to `~/.toybox/EditorSettings.json`), `Models/Ecs/`.
- `Services/` — editor behavior, by sub-namespace:
  - `EngineApi/` — `EngineRpc.cs` (owns every RPC call), `Session.cs` (launch-owned vs attach, ping loop, teardown), `Locator.cs` (finds the engine checkout), `EngineWatcher.cs`, `InstanceDetector.cs`, `ViewportStream.cs` (GPU surface streaming).
  - `World/` — `WorldManager.cs` (the world service: snapshot/refresh + world-dirty + save), `WorldSelection.cs`. The `World` snapshot model lives in `Models/Ecs/`.
  - `Project/` — `ProjectManager.cs`, `CMakeCompiler.cs` (in-tree engine build), `AssetCatalog.cs`.
  - `Logging/` — `Logger.cs` (unified editor + engine log), `LogFile.cs` (rotated `~/.toybox/Logs/TbxStudio.log`).
  - `Theming/` — `ThemeManager.cs` (facade) + `ThemeRepository.cs` + `ThemeApplier.cs` + `DockSelectorTinter.cs`, `Theme.cs`, `ColorGradient.cs`.
  - `Dialogs/` — file/asset pickers, popups, OS file reveal.
- `Shell/` — the app frame: `App` composition lives in `App.axaml.cs` (`ConfigureServices` is the DI composition root); `Shell.axaml`, `ShellViewModel.cs`, `SplashWindow`; `Workspace/` is the docking system; `Documents/` is the data-ownership plumbing; `Styles/` holds the split XAML style sheets aggregated by `AppStyles.axaml`.
- `Widgets/` — self-contained panels and reusable controls: `Viewport/` (`CompositionInteropViewport.cs` is the D3D11/WGL interop surface), `WorldTree/`, `EntityInspector/`, `PropertyGrid/` (type-driven; `Core/PropertyViewRegistry.cs` registers per-type widgets), `Console/`, `LogConsole/`, `Settings/`, `Theming/`, `GameView/`, `GameToolbar/`, `Status/`, `Ghost/`, `Searching/`, `Colors/`, `Behaviors/`.
- `Assets/Templates/DefaultProject/` — a real on-disk project the editor copies and builds; not an embedded UI resource.

### Key files to read first

- [`App.axaml.cs`](App.axaml.cs) — startup sequence and the DI composition root (`ConfigureServices`).
- [`Services/EngineApi/EngineRpc.cs`](Services/EngineApi/EngineRpc.cs) — the entire editor↔engine API surface.
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
