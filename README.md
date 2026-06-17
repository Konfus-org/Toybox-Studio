## About

Toybox Studio is the official WYSIWYG editor for the [Toybox engine](https://github.com/Konfus-org/Toybox). It is a cross-platform desktop application built with [Avalonia](https://avaloniaui.net/) on .NET, following the MVVM pattern.
The editor does not embed the engine — it drives a separate engine process over RPC and shares the engine's rendered frames directly on the GPU, so what you see in the viewport is the real engine running your world.

## What's here so far?

Studio currently includes a data-driven dockable workspace (save/restore layouts), a live viewport backed by zero-copy GPU texture sharing, a world/entity tree, a type-driven property grid and entity inspector, 
a unified log console wired to both the editor and the engine, project open/build/launch with an in-tree engine compile, play-mode controls, a buffered settings editor, 
and a full theming system (gradient palettes, a live theme creator, and a "Claymorphism" default).

## Repository Structure

Code is organized into tiers; namespaces follow folders.

- `Utils/` — pure, dependency-free helpers (`Result`, dispatching, color math, list reconciliation).
- `Models/` — plain data types (`EditorSettings`, the ECS view models' backing data).
- `Services/` — the editor's behavior, split into focused sub-namespaces: `EngineApi/` (RPC, session, viewport streaming), `World/`, `Project/`, `Logging/`, `Theming/`, `Dialogs/`.
- `Shell/` — the application frame: the main window, splash, workspace docking system, document/data-ownership plumbing, and the split XAML style sheets.
- `Widgets/` — self-contained editor panels and reusable controls (viewport, world tree, entity inspector, property grid, console, settings, theme creator, behaviors).
- `Assets/` — icons and the on-disk `DefaultProject` template the editor copies and builds.
- `App.axaml(.cs)`, `Program.cs` — Avalonia entry point and dependency-injection composition root.

## Architecture

Studio talks to the engine; it does not contain it. The editor builds the engine in-tree with the open project, launches it as a child process, and connects over a JSON-RPC channel. See [`docs/Architecture.md`](docs/Architecture.md) for the full picture (DI composition, the RPC layers, GPU frame sharing, the docking system, the type-driven property grid, data ownership, and theming).

- **MVVM, no code-behind routing.** Views bind to view models with compiled bindings; behavior lives in commands, attached behaviors, control subclasses, and services — never in view bootstrap code.
- **Dependency injection.** A `Microsoft.Extensions.Hosting` generic host owns every service as a singleton; the composition root is `App.ConfigureServices`.
- **RPC over the wire.** `EngineRpc` owns the StreamJsonRpc connection (newline-delimited JSON over loopback TCP) to the engine's `StudioBridge` plugin and returns a `Result` on failure rather than throwing.
- **Data-driven panels.** A panel is declared in exactly one place — a `[Dockable]` attribute on its View — and auto-registers into DI, the Windows menu, and the dock.

## Getting Started

Clone the repository:

```bash
git clone https://github.com/Konfus-org/Toybox-Studio.git
cd Toybox-Studio
```

Studio drives a Toybox engine checkout. On first launch it tries to locate one automatically; if it can't, open **Settings (⚙)** and point the **Engine path** at your engine source folder.
A Debug build of Studio drives a Debug engine and a Release build drives a Release engine.

## How to build

### Prerequisites

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download) or newer.
2. A Toybox engine checkout (see [the engine repo](https://github.com/Konfus-org/Toybox)) and its C++ build prerequisites — the editor compiles the engine in-tree via CMake/Ninja when launching a project.
3. Windows: the live viewport uses Direct3D 11 shared textures and the `WGL_NV_DX_interop2` extension. GPU frame sharing is Windows-only. The RPC plugin engine side is also currently Windows-only, but the editor and engine as a whole (other than the live viewport) are cross-platform and we plan to support Linux/Mac engines in the future.

### Building and running

#### Command line

```bash
# Restore + build
dotnet build Toybox.Studio.slnx

# Run (a Debug build pulls in the Avalonia dev tools)
dotnet run --project Toybox.Studio.csproj
```

#### Visual Studio

Open `Toybox.Studio.slnx` and build/run from there.

#### Build artifacts and editor data

Build artifacts are kept under `build/` (`build/bin` for output, `build/obj` for intermediates) per `Directory.Build.props`.
Editor settings, themes, and the rotated `TbxStudio.log` live under `~/.toybox/`.

## Contributing and AI Usage

See [`docs/Contributing.md`](docs/Contributing.md) for the contribution flow and [`docs/CodeStandards.md`](docs/CodeStandards.md) for the engineering standards.
In regards to AI usage:
See [`AGENTS.md`](AGENTS.md) for the standards AI agents follow in this project. We encourage the responsible use of AI tools, but AI-generated code is reviewed with the same care and scrutiny as human-written code and is never blindly accepted. If you use AI to contribute, you should fully understand what the code is doing and be ready to explain, defend, and change it. All AI-generated code must be reviewed and approved by a human before being merged and must follow the same standards as human-written code.
