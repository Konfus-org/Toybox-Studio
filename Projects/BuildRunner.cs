using Toybox.Studio.CMake;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.Settings;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Projects;

/// <summary>
/// Runs one native CMake build — ensure the tree is configured (recovering a failed configure, and
/// reconfiguring from clean when the compiler setting changed under it), then compile — for both build
/// services: <see cref="ProjectBuilder"/> and <see cref="EngineBuilder"/> differ only in what tree they
/// point it at. The runner owns the editor's single build gate: at most one native build runs at a time
/// across BOTH services (a second caller is turned away rather than racing a concurrent CMake
/// invocation), and the busy state dispatches as <see cref="BuildStateChanged"/> — one global signal,
/// flipped only when the one running build starts and ends. The build settings (compiler choice,
/// parallel, verbose) are read live from the editor settings on every run, so a Settings edit applies
/// to the next compile without a restart.
/// </summary>
public sealed class BuildRunner
{
    private readonly CMakeCompiler _compiler;
    private readonly SettingsManager _settings;
    private readonly Logger _log;
    private readonly EventDispatcher _events;

    // The one gate every native build runs through; the busy state it toggles is the global
    // BuildStateChanged, so it must not be duplicated per build service.
    private readonly ReentrancyGuard _buildGate = new();
    private bool _isBuilding;

    public BuildRunner(CMakeCompiler compiler, SettingsManager settings, Logger log, EventDispatcher events)
    {
        _compiler = compiler;
        _settings = settings;
        _log = log;
        _events = events;
    }

    // Announces the compile phase as BuildStateChanged, which Engine.State folds into the Compiling phase.
    private bool Building
    {
        set
        {
            if (_isBuilding == value)
                return;

            _isBuilding = value;
            _events.Dispatch(new BuildStateChanged(value));
        }
    }

    /// <summary>
    /// Configures (once) and builds a CMake tree in the given mode. <paramref name="buildDirectoryOf"/>
    /// maps a configure preset to its build tree — a project's is one fixed <c>build/</c> folder, the
    /// engine's presets each own <c>build/&lt;preset&gt;</c>. <paramref name="defines"/> supplies the
    /// cache values presets can't know ahead of time; they apply when the tree is (re)configured.
    /// </summary>
    public async Task<Result> RunAsync(
        string sourceDirectory,
        Func<string, string> buildDirectoryOf,
        IReadOnlyDictionary<string, string> defines,
        BuildMode mode,
        CancellationToken ct)
    {
        // The gate turns the building state on now and off when this scope disposes; a concurrent caller
        // gets a null scope and bails.
        using var buildScope = _buildGate.TryEnter(building => Building = building);
        if (buildScope is null)
            return Result.Fail("A compile is already running.");

        var options = _settings.Editor.Build;

        // An explicit compiler setting names the preset outright; on Auto, reuse whichever tree is
        // already configured, and only resolve one for the machine's toolchain when none is.
        var preferred = options.Compiler == CompilerChoice.Auto
            ? null
            : await _compiler
                .ResolveConfigurePresetAsync(PreferenceOf(options.Compiler), ct).ContinueOnAnyContext();
        var configurePreset = preferred
            ?? FindConfiguredPreset(buildDirectoryOf)
            ?? await _compiler
                .ResolveConfigurePresetAsync(CompilerPreference.Auto, ct).ContinueOnAnyContext();

        var buildDirectory = buildDirectoryOf(configurePreset);
        var configured = CMakeCompiler.ConfiguredPresetOf(buildDirectory);
        if (configured is not null && configured != configurePreset)
        {
            // The compiler setting changed under an existing tree; its cache pins the old toolchain,
            // so honoring the setting means reconfiguring from clean.
            _log.Info($"The compiler setting is now '{configurePreset}' but the build tree was "
                + $"configured with '{configured}'; reconfiguring.");
            CMakeCompiler.Clean(buildDirectory);
            configured = null;
        }

        if (configured is null)
        {
            _log.Info("Configuring the CMake build (the first time can take a while)...");
            if (!await _compiler
                    .ConfigureAsync(sourceDirectory, configurePreset, defines, ct).ContinueOnAnyContext())
            {
                // A failed configure still writes a cache, which would make this tree read as configured
                // and skip this step on every later build; clear it so the next attempt starts clean.
                CMakeCompiler.Clean(buildDirectory);
                return Result.Fail("CMake configure failed.");
            }
        }

        var buildPreset = CMakeCompiler.BuildPreset(configurePreset, mode.ToString());
        var built = await _compiler
            .BuildAsync(sourceDirectory, buildDirectory, buildPreset, options.Parallel, options.Verbose, ct)
            .ContinueOnAnyContext();
        return built ? Result.Ok() : Result.Fail("CMake build failed.");
    }

    /// <summary>The configure preset of whichever candidate tree is already configured, or null.</summary>
    private static string? FindConfiguredPreset(Func<string, string> buildDirectoryOf) =>
        new[] { CMakeCompiler.MsvcPreset, CMakeCompiler.ClangPreset }
            .Select(preset => CMakeCompiler.ConfiguredPresetOf(buildDirectoryOf(preset)))
            .FirstOrDefault(configured => configured is not null);

    // The settings enum mirrors the CMake layer's member-for-member (so the settings layer stays
    // independent of it); this is the promised mapping.
    private static CompilerPreference PreferenceOf(CompilerChoice choice) =>
        choice switch
        {
            CompilerChoice.Msvc => CompilerPreference.Msvc,
            CompilerChoice.Clang => CompilerPreference.Clang,
            _ => CompilerPreference.Auto,
        };
}
