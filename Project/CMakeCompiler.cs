using System.Diagnostics;
using System.Text.RegularExpressions;
using Toybox.Studio.Logging;
using Toybox.Studio.Shell;

namespace Toybox.Studio.Project;

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
/// Generic cross-platform CMake driver: configures and builds any CMake project by shelling out to the
/// cmake CLI via <see cref="CommandRunner"/>, which streams tool output to the unified log. Knows nothing
/// about Toybox.
/// </summary>
public sealed class CMakeCompiler
{
    private readonly CommandRunner _runner;
    private readonly Logger _log;

    public CMakeCompiler(CommandRunner runner, Logger log)
    {
        _runner = runner;
        _log = log;
    }

    /// <summary>
    /// Configures a build tree, creating it if needed.
    /// </summary>
    public async Task<bool> ConfigureAsync(
        string sourceDirectory,
        string buildDirectory,
        IReadOnlyDictionary<string, string> defines,
        CompilerPreference compiler,
        CancellationToken ct)
    {
        var arguments = new List<string> { "-S", sourceDirectory, "-B", buildDirectory };
        arguments.AddRange(await GetGeneratorArgumentsAsync(compiler, ct).ContinueOnAnyContext());
        foreach (var (name, value) in defines)
            arguments.Add($"-D{name}={value}");

        return await RunCMakeAsync(arguments, ct).ContinueOnAnyContext();
    }

    /// <summary>
    /// Builds a configured tree; incremental when already up to date.
    /// </summary>
    public async Task<bool> BuildAsync(
        string buildDirectory,
        string configuration,
        bool parallel,
        bool verbose,
        CancellationToken ct)
    {
        var arguments = new List<string>
        {
            "--build", buildDirectory,
            "--config", configuration,
        };
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
            // file lock (worth a retry) apart from a genuine build error.
            var sawLock = false;
            void WatchForLock(LogEntry entry)
            {
                if (LooksLikeFileLock(entry.Message))
                    sawLock = true;
            }

            _log.Logged += WatchForLock;
            bool built;
            try
            {
                built = await RunCMakeAsync(arguments, ct).ContinueOnAnyContext();
            }
            finally
            {
                _log.Logged -= WatchForLock;
            }

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

    private static bool LooksLikeFileLock(string message) =>
        message.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
        || message.Contains("unable to remove file", StringComparison.OrdinalIgnoreCase)
        || message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase);

    // The CommandRunner categorizes each logged build line by the first named group that matches. Build
    // tools write most failures to stdout, so the line text — not its stream — is the severity signal:
    // the "error" group catches clang/gcc/cmake "error:", a Ninja "FAILED:" step, MSVC compiler
    // ("error C2065") and linker ("LNK2019"/"LNK1120") codes, and "fatal error"; everything else with a
    // "warning" is a warning, and the rest is info.
    private static readonly Regex BuildLogPattern = new(
        @"(?<error>error:|fatal error|FAILED:|CMake Error|error C[0-9]+|LNK[0-9]+)|(?<warning>warning)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsConfigured(string buildDirectory)
    {
        return File.Exists(Path.Combine(buildDirectory, "CMakeCache.txt"));
    }

    /// <summary>
    /// The selectable compiler options, in display order — the labels the settings dropdown offers and
    /// that <see cref="ParseCompiler"/> understands. Kept here so the two can't drift apart.
    /// </summary>
    public static readonly IReadOnlyList<string> CompilerChoices = ["Auto", "MSVC", "Clang"];

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
    /// True when an existing build tree was already configured with the toolchain the given preference
    /// resolves to. <see cref="CompilerPreference.Auto"/> adapts to whatever the tree uses and so always
    /// matches; an explicit MSVC/Clang choice that differs signals a clean reconfigure is needed (the
    /// generator itself changes, which CMake cannot do in place).
    /// </summary>
    public static bool MatchesCompiler(string buildDirectory, CompilerPreference compiler)
    {
        if (compiler == CompilerPreference.Auto)
            return true;

        var cachePath = Path.Combine(buildDirectory, "CMakeCache.txt");
        if (!File.Exists(cachePath))
            return false;

        var generator = "";
        var cxx = "";
        foreach (var line in File.ReadLines(cachePath))
        {
            if (line.StartsWith("CMAKE_GENERATOR:", StringComparison.Ordinal))
                generator = ValueAfterEquals(line);
            else if (line.StartsWith("CMAKE_CXX_COMPILER:", StringComparison.Ordinal))
                cxx = ValueAfterEquals(line);
        }

        var usesMsvc = generator.StartsWith("Visual Studio", StringComparison.OrdinalIgnoreCase)
            || cxx.EndsWith("cl.exe", StringComparison.OrdinalIgnoreCase)
            || cxx.Equals("cl", StringComparison.OrdinalIgnoreCase);
        var usesClang = cxx.Contains("clang", StringComparison.OrdinalIgnoreCase);

        return compiler == CompilerPreference.Msvc ? usesMsvc : usesClang;
    }

    private static string ValueAfterEquals(string line)
    {
        var index = line.IndexOf('=');
        return index < 0 ? "" : line[(index + 1)..].Trim();
    }

    /// <summary>
    /// Selects the native toolchain for a configure. MSVC relies on CMake's default Visual Studio
    /// generator, which locates the toolchain itself (no developer command prompt required); Clang
    /// uses a Ninja Multi-Config + clang setup. Auto picks MSVC on Windows (when installed) and Clang
    /// elsewhere, falling back to the platform default when neither is available.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetGeneratorArgumentsAsync(
        CompilerPreference compiler,
        CancellationToken ct)
    {
        switch (compiler)
        {
            case CompilerPreference.Msvc:
                // CMake's default Windows generator is the installed Visual Studio (a multi-config
                // generator, like the Ninja Multi-Config used for clang), so nothing to add.
                return [];

            case CompilerPreference.Clang:
                return ClangNinjaArguments;

            default:
                if (OperatingSystem.IsWindows() && IsVisualStudioToolchainAvailable())
                    return [];
                if (await IsToolAvailableAsync("ninja", ct).ContinueOnAnyContext()
                    && await IsToolAvailableAsync("clang++", ct).ContinueOnAnyContext())
                    return ClangNinjaArguments;
                return [];
        }
    }

    private static readonly IReadOnlyList<string> ClangNinjaArguments =
    [
        "-G",
        "Ninja Multi-Config",
        "-DCMAKE_C_COMPILER=clang",
        "-DCMAKE_CXX_COMPILER=clang++",
    ];

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

    private async Task<bool> RunCMakeAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var result = await _runner.RunAsync(
            "cmake",
            arguments,
            logOutput: true,
            logCategoryRegex: BuildLogPattern,
            ct: ct).ContinueOnAnyContext();

        // A launch or timeout failure leaves no tool output to explain itself (value -1, "did not run");
        // a non-zero build exit is already explained by the logged errors, so surface only the former.
        if (result.Value < 0 && result.Error is { } error)
            _log.Error(error);

        return result;
    }

    private static readonly TimeSpan ToolDetectionTimeout = TimeSpan.FromSeconds(5);

    /// <summary>True when <paramref name="tool"/> runs (<c>--version</c> exits 0), i.e. it is on PATH.</summary>
    private async Task<bool> IsToolAvailableAsync(string tool, CancellationToken ct) =>
        await _runner.RunAsync(tool, ["--version"], timeout: ToolDetectionTimeout, ct: ct)
            .ContinueOnAnyContext();
}
