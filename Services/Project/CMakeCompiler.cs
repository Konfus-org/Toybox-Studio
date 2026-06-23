using Toybox.Studio.Utils;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Shell;

namespace Toybox.Studio.Services.Project;

/// <summary>
/// Which C++ toolchain CMake should configure a build tree with.
/// </summary>
public enum CompilerPreference
{
    /// <summary>MSVC on Windows, Clang on other platforms.</summary>
    Auto,

    /// <summary>The Microsoft Visual C++ toolchain (Windows only).</summary>
    Msvc,

    /// <summary>The LLVM Clang toolchain.</summary>
    Clang,
}

/// <summary>
/// Generic cross-platform CMake driver: configures and builds any CMake project through its
/// <c>CMakePresets.json</c> by shelling out to the cmake CLI via <see cref="CommandRunner"/>, which
/// streams tool output to the unified log. The generator, toolchain, and base cache variables live in
/// the project's presets rather than here; this class only chooses which preset to use. Knows nothing
/// about Toybox beyond the preset naming convention (a <c>msvc</c>/<c>clang</c> configure preset and
/// matching <c>&lt;compiler&gt;-&lt;config&gt;</c> build presets).
/// </summary>
public sealed class CMakeCompiler
{
    private readonly CommandRunner _runner;
    private readonly Logger _log;

    // The CommandRunner categorizes each logged build line by the first named group that matches. Build
    // tools write most failures to stdout, so the line text — not its stream — is the severity signal:
    // the "error" group catches clang/gcc/cmake "error:", a Ninja "FAILED:" step, MSVC compiler
    // ("error C2065") and linker ("LNK2019"/"LNK1120") codes, and "fatal error"; everything else with a
    // "warning" is a warning, and the rest is info.
    private static readonly Regex BuildLogPattern = new(
        @"(?<error>error:|fatal error|FAILED:|CMake Error|error C[0-9]+|LNK[0-9]+)|(?<warning>warning)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The selectable compiler options, in display order — the labels the settings dropdown offers and
    /// that <see cref="ParseCompiler"/> understands. Kept here so the two can't drift apart.
    /// </summary>
    public static readonly IReadOnlyList<string> CompilerChoices = ["Auto", "MSVC", "Clang"];

    /// <summary>The configure-preset names defined in every Toybox project's <c>CMakePresets.json</c>.</summary>
    public const string MsvcPreset = "msvc";
    public const string ClangPreset = "clang";

    private static readonly TimeSpan ToolDetectionTimeout = TimeSpan.FromSeconds(5);

    public CMakeCompiler(CommandRunner runner, Logger log)
    {
        _runner = runner;
        _log = log;
    }

    /// <summary>
    /// Configures a build tree from the given configure preset, creating it if needed. The preset fixes
    /// the binary directory and toolchain; <paramref name="defines"/> supplies the values presets can't
    /// know ahead of time (such as the located engine directory). cmake resolves presets relative to its
    /// working directory, so this runs from <paramref name="projectDirectory"/>.
    /// </summary>
    public async Task<bool> ConfigureAsync(
        string projectDirectory,
        string configurePreset,
        IReadOnlyDictionary<string, string> defines,
        CancellationToken ct)
    {
        var arguments = new List<string> { "--preset", configurePreset };
        foreach (var (name, value) in defines)
            arguments.Add($"-D{name}={value}");

        return await RunCMakeAsync(arguments, projectDirectory, ct).ContinueOnAnyContext();
    }

    /// <summary>
    /// Builds a configured tree from the given build preset; incremental when already up to date. The
    /// build preset carries the configuration and binary directory, so this runs from
    /// <paramref name="projectDirectory"/> (where cmake finds the presets file).
    /// </summary>
    public async Task<bool> BuildAsync(
        string projectDirectory,
        string buildDirectory,
        string buildPreset,
        bool parallel,
        bool verbose,
        CancellationToken ct)
    {
        var arguments = new List<string> { "--build", "--preset", buildPreset };
        if (parallel)
            arguments.Add("--parallel");
        if (verbose)
            arguments.Add("--verbose");

        // The engine is built in-tree, so its freshly produced binaries can still be briefly locked
        // when we relink — by an engine process that is shutting down (handles linger past exit) or
        // by an on-access virus scan. Those locks clear within moments, so retry before giving up.
        var retryDelays = new[]
        {
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
        };
        for (var attempt = 0; ; attempt++)
        {
            // The runner logs the tool output itself, so watch the unified log stream to tell a transient
            // file lock (worth a retry) apart from a genuine build error, and to catch a damaged Ninja
            // dependency log so we can repair it (see RepairNinjaDepsAsync).
            var sawLock = false;
            var sawDepsCorruption = false;
            void WatchBuildOutput(LogEntry entry)
            {
                if (LooksLikeFileLock(entry.Message))
                    sawLock = true;
                if (LooksLikeDepsCorruption(entry.Message))
                    sawDepsCorruption = true;
            }

            _log.Logged += WatchBuildOutput;
            bool built;
            try
            {
                built = await RunCMakeAsync(arguments, projectDirectory, ct).ContinueOnAnyContext();
            }
            finally
            {
                _log.Logged -= WatchBuildOutput;
            }

            // Ninja warned that its dependency log was truncated (an earlier build's process tree was
            // killed mid-write — a cancelled launch or a force-stopped engine). It recovers the log in
            // memory but never rewrites the file, so the damage sticks: every later build re-reads the
            // stale prefix and recompiles everything. Recompact now, while the records are fresh from
            // this build, so the next build is incremental again.
            if (sawDepsCorruption)
                await RepairNinjaDepsAsync(buildDirectory, ct).ContinueOnAnyContext();

            if (built)
                return true;

            if (!sawLock || attempt >= retryDelays.Length)
            {
                if (sawLock)
                    _log.Error(
                        "Build output is still locked. Close any running instance of the project "
                            + "(or exclude the build folder from your virus scanner) and try again.");
                return false;
            }

            _log.Warning(
                $"Build output was locked (a running engine or virus scan); retrying in "
                    + $"{retryDelays[attempt].TotalSeconds:0.#}s...");
            await Task.Delay(retryDelays[attempt], ct).ContinueOnAnyContext();
        }
    }

    public static bool IsConfigured(string buildDirectory)
    {
        return File.Exists(Path.Combine(buildDirectory, "CMakeCache.txt"));
    }

    /// <summary>
    /// Maps a stored compiler setting ("Auto"/"MSVC"/"Clang", case-insensitive) to a preference,
    /// defaulting to <see cref="CompilerPreference.Auto"/> for anything unrecognized.
    /// </summary>
    public static CompilerPreference ParseCompiler(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "msvc" => CompilerPreference.Msvc,
        "clang" => CompilerPreference.Clang,
        _ => CompilerPreference.Auto,
    };

    /// <summary>
    /// The build preset for a configure preset and configuration, by the
    /// <c>&lt;compiler&gt;-&lt;config&gt;</c> convention the presets file uses (e.g. <c>clang-debug</c>).
    /// </summary>
    public static string BuildPreset(string configurePreset, string configuration) =>
        $"{configurePreset}-{configuration.ToLowerInvariant()}";

    /// <summary>
    /// The configure preset an existing tree was generated with ("msvc"/"clang"), inferred from its
    /// CMakeCache, or null if the directory isn't configured or the toolchain is unrecognized. Lets the
    /// editor reuse a tree's own preset rather than re-deriving one from the environment, which could
    /// differ from how the tree was actually built.
    /// </summary>
    public static string? ConfiguredPresetOf(string buildDirectory)
    {
        var cachePath = Path.Combine(buildDirectory, "CMakeCache.txt");
        if (!File.Exists(cachePath))
            return null;

        var generator = "";
        var cxx = "";
        foreach (var line in File.ReadLines(cachePath))
        {
            if (line.StartsWith("CMAKE_GENERATOR:", StringComparison.Ordinal))
                generator = ValueAfterEquals(line);
            else if (line.StartsWith("CMAKE_CXX_COMPILER:", StringComparison.Ordinal))
                cxx = ValueAfterEquals(line);
        }

        if (generator.StartsWith("Visual Studio", StringComparison.OrdinalIgnoreCase)
            || cxx.EndsWith("cl.exe", StringComparison.OrdinalIgnoreCase)
            || cxx.Equals("cl", StringComparison.OrdinalIgnoreCase))
            return MsvcPreset;
        if (cxx.Contains("clang", StringComparison.OrdinalIgnoreCase))
            return ClangPreset;
        return null;
    }

    /// <summary>
    /// True when an existing build tree was already configured with the toolchain the given preference
    /// resolves to. <see cref="CompilerPreference.Auto"/> adapts to whatever the tree uses and so always
    /// matches; an explicit MSVC/Clang choice that differs signals a clean reconfigure is needed (the
    /// generator itself changes, which CMake cannot do in place).
    /// </summary>
    public static bool MatchesCompiler(string buildDirectory, CompilerPreference compiler)
    {
        if (compiler == CompilerPreference.Auto)
            return true;

        var preset = ConfiguredPresetOf(buildDirectory);
        if (preset is null)
            return false;

        return compiler == CompilerPreference.Msvc ? preset == MsvcPreset : preset == ClangPreset;
    }

    /// <summary>
    /// Picks the configure preset for a fresh build tree. MSVC's preset uses CMake's default Visual Studio
    /// generator, which locates the toolchain itself (no developer command prompt required); Clang's uses
    /// Ninja Multi-Config + clang. Auto picks MSVC on Windows (when installed) and Clang elsewhere,
    /// falling back to MSVC only as a last resort on Windows.
    /// </summary>
    public async Task<string> ResolveConfigurePresetAsync(
        CompilerPreference compiler,
        CancellationToken ct)
    {
        switch (compiler)
        {
            case CompilerPreference.Msvc:
                return MsvcPreset;

            case CompilerPreference.Clang:
                return ClangPreset;

            default:
                if (OperatingSystem.IsWindows() && IsVisualStudioToolchainAvailable())
                    return MsvcPreset;
                if (await IsToolAvailableAsync("ninja", ct).ContinueOnAnyContext()
                    && await IsToolAvailableAsync("clang++", ct).ContinueOnAnyContext())
                    return ClangPreset;
                return MsvcPreset;
        }
    }

    private static bool LooksLikeFileLock(string message) =>
        message.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
        || message.Contains("unable to remove file", StringComparison.OrdinalIgnoreCase)
        || message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase);

    // Ninja prints this while loading a .ninja_deps/.ninja_log that ends mid-record — the signature of a
    // build process that was killed mid-write (a cancelled launch, a force-stopped engine, a timeout).
    private static bool LooksLikeDepsCorruption(string message) =>
        message.Contains("premature end of file", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Rewrites Ninja's build/dependency logs from the records it currently holds, clearing damage left by
    /// a build whose process tree was killed mid-write. Without this a single interrupted build poisons the
    /// tree indefinitely: Ninja recovers the truncated log in memory on every load but never rewrites it,
    /// so each subsequent build re-reads the stale prefix and recompiles everything. A no-op for non-Ninja
    /// generators (the MSVC preset's Visual Studio generator has no <c>build.ninja</c>) or if ninja isn't
    /// on PATH.
    /// </summary>
    private async Task RepairNinjaDepsAsync(string buildDirectory, CancellationToken ct)
    {
        if (!File.Exists(Path.Combine(buildDirectory, "build.ninja")))
            return;

        _log.Info("Repairing the incremental build database left inconsistent by an interrupted build...");
        await _runner.RunAsync(
            "ninja",
            ["-t", "recompact"],
            workingDirectory: buildDirectory,
            ct: ct).ContinueOnAnyContext();
    }

    private static string ValueAfterEquals(string line)
    {
        var index = line.IndexOf('=');
        return index < 0 ? "" : line[(index + 1)..].Trim();
    }

    /// <summary>
    /// True when a Visual Studio C++ toolchain is installed, detected via vswhere (which ships with every
    /// modern Visual Studio installer) so detection works without a developer command prompt. This is the
    /// one command run directly rather than through <see cref="CommandRunner"/>: it needs vswhere's stdout
    /// (an empty result means "no VC toolchain"), whereas the runner reports only an exit code — and
    /// vswhere always exits 0.
    /// </summary>
    private static bool IsVisualStudioToolchainAvailable()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (string.IsNullOrEmpty(programFilesX86))
            return false;

        var vswhere = Path.Combine(
            programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere))
            return false;

        try
        {
            using var process = Process.Start(
                new ProcessStartInfo(vswhere)
                {
                    ArgumentList =
                    {
                        "-latest",
                        "-products", "*",
                        "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
                        "-property", "installationPath",
                    },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
            if (process is null)
                return false;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<bool> RunCMakeAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken ct)
    {
        var result = await _runner.RunAsync(
            "cmake",
            arguments,
            workingDirectory: workingDirectory,
            logOutput: true,
            logCategoryRegex: BuildLogPattern,
            ct: ct).ContinueOnAnyContext();

        // A launch or timeout failure leaves no tool output to explain itself (value -1, "did not run");
        // a non-zero build exit is already explained by the logged errors, so surface only the former.
        if (result.Value < 0 && result.Error is { } error)
            _log.Error(error);

        return result;
    }

    /// <summary>True when <paramref name="tool"/> runs (<c>--version</c> exits 0), i.e. it is on PATH.</summary>
    private async Task<bool> IsToolAvailableAsync(string tool, CancellationToken ct) =>
        await _runner.RunAsync(tool, ["--version"], timeout: ToolDetectionTimeout, ct: ct)
            .ContinueOnAnyContext();
}
