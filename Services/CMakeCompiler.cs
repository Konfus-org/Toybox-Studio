using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Toybox.Studio.Services;

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
/// Generic cross-platform CMake driver: configures and builds any CMake project by shelling out
/// to the cmake CLI, streaming tool output to a log callback. Knows nothing about Toybox.
/// </summary>
public sealed class CMakeCompiler
{
    /// <summary>
    /// True when the cmake CLI is reachable on PATH.
    /// </summary>
    public static bool IsCMakeAvailable()
    {
        return TryGetToolVersion("cmake") is not null;
    }

    /// <summary>
    /// Configures a build tree, creating it if needed.
    /// </summary>
    public async Task<bool> ConfigureAsync(
        string sourceDirectory,
        string buildDirectory,
        IReadOnlyDictionary<string, string> defines,
        CompilerPreference compiler,
        Action<string, string> log,
        CancellationToken ct)
    {
        var arguments = new List<string> { "-S", sourceDirectory, "-B", buildDirectory };
        arguments.AddRange(GetGeneratorArguments(compiler));
        foreach (var (name, value) in defines)
            arguments.Add($"-D{name}={value}");

        return await RunCMakeAsync(arguments, log, ct).ContinueOnAnyContext();
    }

    /// <summary>
    /// Builds a configured tree; incremental when already up to date.
    /// </summary>
    public async Task<bool> BuildAsync(
        string buildDirectory,
        string configuration,
        bool parallel,
        bool verbose,
        Action<string, string> log,
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
            var sawLock = false;
            void TrackLock(string level, string message)
            {
                if (LooksLikeFileLock(message))
                    sawLock = true;
                log(level, message);
            }

            if (await RunCMakeAsync(arguments, TrackLock, ct).ContinueOnAnyContext())
                return true;

            if (!sawLock || attempt >= retryDelays.Length)
            {
                if (sawLock)
                    log(
                        "error",
                        "Build output is still locked. Close any running instance of the project "
                            + "(or exclude the build folder from your virus scanner) and try again.");
                return false;
            }

            log(
                "warning",
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

    // Build tools write most failures to stdout, so a line's stream alone is a poor severity signal.
    // Recognize the common error shapes — clang/gcc/cmake "error:", a Ninja "FAILED:" step, MSVC
    // compiler ("error C2065") and linker ("LNK2019"/"LNK1120") codes, and "fatal error".
    private static readonly Regex ErrorLinePattern = new(
        @"error:|fatal error|FAILED:|CMake Error|error C[0-9]+|LNK[0-9]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Upgrades a tool line to "error" when it carries a recognizable build-error signature, so real
    /// failures stand out instead of hiding among the info/warning stream lines; otherwise the line
    /// keeps its stream's default level.
    /// </summary>
    private static string ClassifyLevel(string defaultLevel, string line) =>
        ErrorLinePattern.IsMatch(line) ? "error" : defaultLevel;

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
    private static IEnumerable<string> GetGeneratorArguments(CompilerPreference compiler)
    {
        switch (compiler)
        {
            case CompilerPreference.Msvc:
                // CMake's default Windows generator is the installed Visual Studio (a multi-config
                // generator, like the Ninja Multi-Config used for clang), so nothing to add.
                yield break;

            case CompilerPreference.Clang:
                foreach (var argument in ClangNinjaArguments())
                    yield return argument;
                yield break;

            default:
                if (OperatingSystem.IsWindows() && IsVisualStudioToolchainAvailable())
                    yield break;
                if (TryGetToolVersion("ninja") is not null && TryGetToolVersion("clang++") is not null)
                    foreach (var argument in ClangNinjaArguments())
                        yield return argument;
                yield break;
        }
    }

    private static IEnumerable<string> ClangNinjaArguments()
    {
        yield return "-G";
        yield return "Ninja Multi-Config";
        yield return "-DCMAKE_C_COMPILER=clang";
        yield return "-DCMAKE_CXX_COMPILER=clang++";
    }

    /// <summary>
    /// True when a Visual Studio C++ toolchain is installed, detected via vswhere (which ships with
    /// every modern Visual Studio installer) so detection works without a developer command prompt.
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

    private static async Task<bool> RunCMakeAsync(
        IReadOnlyList<string> arguments,
        Action<string, string> log,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo("cmake")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                log(ClassifyLevel("info", e.Data), e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                log(ClassifyLevel("warning", e.Data), e.Data);
        };

        try
        {
            if (!process.Start())
            {
                log("error", "Failed to start cmake; is it installed and on PATH?");
                return false;
            }
        }
        catch (Exception exception)
        {
            log("error", $"Failed to start cmake: {exception.Message}");
            return false;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(ct).ContinueOnAnyContext();
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // Already exited.
            }

            throw;
        }

        return process.ExitCode == 0;
    }

    private static string? TryGetToolVersion(string tool)
    {
        try
        {
            using var process = Process.Start(
                new ProcessStartInfo(tool, "--version")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
            if (process is null)
                return null;

            var firstLine = process.StandardOutput.ReadLine();
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? firstLine : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
