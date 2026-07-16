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

### Key files to read first

- [`Launcher/Launcher.cs`](Launcher/Launcher.cs) — the entry point and DI composition root: `Launcher.LaunchAsync` boots Avalonia, configures the service provider, and runs the startup flow (project picker → splash → main window).
- [`Rpc/RpcClient.cs`](Rpc/RpcClient.cs) — the generic JSON-RPC transport (connection, retry, request/notification primitives); peer-agnostic.
- [`EngineApi/EngineRpc.cs`](EngineApi/EngineRpc.cs) — the engine connection: the engine-specific facade over `RpcClient` (handshake + typed notifications). The domain API surface lives on the `Entity`/`Component`/`AssetCatalog` constructs and the static `Asset` API that call its primitives.
- [`EngineApi/Session.cs`](EngineApi/Session.cs) — engine process lifetime (launch/attach/teardown).
- [`Utils/Attributes/DockableAttribute.cs`](Utils/Attributes/DockableAttribute.cs) — how panels are declared (a `[Dockable]` `XxxView` binds to `XxxViewModel` in the same namespace, or an explicit `ViewModel = typeof(...)`; slot/parent/float-bounds metadata rides on the attribute). Its view-model is built on open by the `ViewModelFactory` ([`Utils/Composition/ViewModelFactory.cs`](Utils/Composition/ViewModelFactory.cs)), which resolves the panel's constructor services from the container — view-models are never registered as services.
- [`Utils/Composition/ViewModelFactory.cs`](Utils/Composition/ViewModelFactory.cs) — the one type given the service provider: `viewModels.Create<T>(runtimeArgs…)` builds any view-model, injecting its services and matching runtime (non-service) arguments by type. Use it instead of `new SomeViewModel(serviceA, serviceB, …)`; a parent that only forwards services to its children should take the factory rather than threading them through. View-models are never services — a panel keeps any across-reopen state in a service (Settings ← `SettingsManager`, the Log Console ← the `Logger` backlog), not in a kept-alive instance.
- [`PropertyGrid/ReflectionPropertyNodeFactory.cs`](PropertyGrid/ReflectionPropertyNodeFactory.cs) — how a CLR object graph becomes slot-composed property rows (the factory pattern every grid source follows).
- [`Documentation/Architecture.md`](Documentation/Architecture.md) — the system narrative tying all of the above together.

## Build / Test / Run

```bash
# Restore + build (warnings are errors)
dotnet build Toybox.Studio.slnx

# Run; a Debug build enables the Avalonia dev tools (F12)
dotnet run --project Launcher/Launcher.csproj
```

Build artifacts go under `build/`. There is no unit-test project yet; verification is by building clean and running the app (see below).

The editor compiles and launches the engine itself. A Debug Studio drives a Debug engine, a Release Studio a Release engine. Studio needs a located engine checkout — if the `Locator` can't find one, set the **Engine path** in **Settings (⚙)**.

## Verification

- **Always build clean first.** `TreatWarningsAsErrors` is on; a warning fails the build. Fix all warnings.
- **Run the app and watch the logs.** Launch Studio, let it reach "Ready.", exercise the change, then shut down cleanly. Inspect `~/.toybox/Logs/TbxStudio.log` (the same stream shown in the in-app console) — resolve any logged warnings or errors and re-test until the log is clean.
- **Verify UI/rendering changes visually.** For viewport, theming, or layout changes, run the app and confirm the result on screen; a clean build alone is not proof for visual work.
- **Mind teardown.** Engine-session and plugin-owned resources have strict teardown ordering; if you touch `Session`, `EngineRpc`, or the viewport interop, verify a clean launch *and* shutdown with no orphaned engine process and no errors in the log.
