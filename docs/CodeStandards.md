# Toybox Studio CodeStandard

Engineering standards for the C#/Avalonia editor. These complement the engine's C++ standards; where the engine and Studio share a philosophy (small scope, no stale code, simplest solution first) the same spirit applies here.

## Core Engineering Policies

- **Scope**: Keep changes isolated and highly reusable. Widgets are self-contained — a panel owns its View, ViewModel, and any panel-specific helpers, and reaches the rest of the app only through injected services.
- **Duplication**: Avoid redundant patterns; factor a single-use helper rather than copy-pasting, but don't build an abstraction for one caller.
- **Simplicity**: Prioritize the simplest, most direct solution. Avoid over-engineering, speculative generality, and god classes. Add complexity only when a concrete problem requires it.
- **Housekeeping**: Permanently delete stale code instead of leaving commented-out placeholders.
- **No god classes**: Prefer many small, focused types over one large coordinator. When a class grows two responsibilities, split it (see `ThemeManager` → facade + repository + applier + tinter).

## C# Language Standards

- **Target**: .NET 10, latest C# language version. Nullable reference types are enabled and `TreatWarningsAsErrors` is on — a warning fails the build. Treat every nullability and analyzer warning as a real defect.
- **File-scoped namespaces**: Use `namespace Toybox.Studio.X;` (no braces). Namespaces follow the folder path exactly.
- **`sealed` by default**: Seal classes that are not explicitly designed for inheritance.
- **Primary constructors and `record`s**: Prefer them for data carriers and simple dependency injection. Use `record`/`readonly record struct` for value-like and DTO types (e.g. `Result`, RPC reply types).
- **Expected failures return `Result`**: Use `Result`/`Result<T>` from `Utils/` for operations that can fail in a foreseeable way (RPC calls, project/file I/O). Reserve exceptions for genuinely exceptional, unrecoverable conditions. `Result` converts implicitly to `bool`, so `if (result)` tests success.
- **`async`/`await` end to end**: Don't block on async with `.Result`/`.Wait()` on the UI thread. Marshal back to the UI thread explicitly via the `Utils/Dispatch` helpers; use `ContinueOnSameContext()`/`ContinueOnAnyContext()` to make the intended thread obvious at the call site.
- **Implicit usings** are enabled; don't add redundant `using`s for the implicit set.

## Naming

- **No `Base` suffix.** Name a shared parent for what it *is*, not its role in the hierarchy — `DropdownPropertyViewModel`, not `PropertyViewModelBase`.
- **MVVM convention**: `XxxView` (UserControl) ↔ `XxxViewModel`, in the same namespace. The docking system relies on this convention to resolve a panel's view model.
- **Services drop the `Service` suffix** and the redundant `Engine` prefix (`Logger`, `ProjectManager`, `WorldSelection`, not `LoggingService`/`EngineSessionService`).

## File & Type Layout

- **One public class per `.cs` file**; one widget (View) per `.axaml` file. Small private helper types tightly owned by the file may share it, but prefer their own file.
- **Document the "why".** Use `///` XML-doc summaries on public classes, records, and non-obvious public members. Comments explain assumptions, constraints, and non-obvious decisions — not what self-explanatory code already says.
- **Member layout within a type.** Order members by category, in this fixed sequence:

  1. Constants & Fields
  2. Constructors (static constructor / finalizer alongside)
  3. Events
  4. Properties (and indexers)
  5. Methods (including operators)
  6. Nested Types

  When a type has members in **two or more** of these categories, mark each present category with a banner comment so the sections are scannable:

  ```csharp
  // ==========================================
  // 1. Constants & Fields
  // ==========================================
  ```

  Keep each member's attributes, XML-doc, and leading comments attached when it moves, and preserve the existing relative order *within* a category (never reorder fields among themselves — initializer order can be load-bearing). Skip the banners for trivial single-section types — enums, interfaces, delegates, marker attributes, simple DTO `record`s, and framework code-behind (`*.axaml.cs`): the ordering still applies, but banners on a five-line type are noise. See `Widgets/Settings/EditorSettingsViewModel.cs` for a worked example.

- **Order methods by accessibility**, most accessible first: `public` → `internal` → `protected` (and `protected internal` / `private protected`) → `private`. The public surface of a type reads top-down before its implementation detail. Within a single accessibility tier preserve the existing order (keep related/overload groups together; don't sort alphabetically). This ordering applies *within* the Methods section only — it does not reshuffle the category sequence above. (Fields stay in their load-bearing order; the public-first rule is for methods.)

## MVVM & Avalonia

- **No code-behind routing.** `.axaml.cs` files contain only what the framework requires (`InitializeComponent`) and genuinely view-local visual concerns. No view-model construction, no event-handler wiring that belongs in a command, no business logic. Move that into:
  - **Commands** on the view model (`CommunityToolkit.Mvvm` `[RelayCommand]`).
  - **Attached behaviors** (`Widgets/Behaviors/`) for reusable input/interaction glue.
  - **Control subclasses** for control-specific behavior (e.g. `ConsoleListBox`).
  - **Services** for cross-cutting concerns.
- **Compiled bindings.** `AvaloniaUseCompiledBindingsByDefault` is on; every binding must be statically resolvable — set `x:DataType` and bind against real members.
- **MVVM toolkit.** Use `CommunityToolkit.Mvvm` (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`) for view models rather than hand-rolled `INotifyPropertyChanged`.
- **Dependency injection.** Services are registered as singletons in `App.ConfigureServices` (the composition root). Don't `new` a service that the container owns — inject it. Dockable view models auto-register via the `[Dockable]` attribute; don't register them by hand.
- **Styles are split and aggregated.** Shared styles live in focused sheets under `Shell/Styles/` and are pulled together by `AppStyles.axaml`. Theme-derived brushes are computed from published `Theme*Color` resources in XAML; keep resource-precedence-sensitive overrides where they already live.

## Documentation

- Keep `README.md`, `AGENTS.md`, and `docs/` accurate when you make architectural changes.
- Reusable controls live under `Widgets/` in a themed sub-folder (e.g. `Widgets/Colors`, `Widgets/Searching`) — there is no top-level `Controls/` folder.

## Formatting

- Use the repository's `.editorconfig` / default `dotnet format` conventions.
- Keep `using` directives sorted and free of unused entries (warnings-as-errors will catch the latter).
