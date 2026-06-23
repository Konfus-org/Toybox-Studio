using Toybox.Studio.Utils;
using System;
using System.IO;

namespace Toybox.Studio.Services.Scripting;

/// <summary>
/// Finds the <c>clangd</c> language server the script editor drives. It's the same LLVM toolchain the engine's
/// Clang build option uses, so we look in the standard LLVM install location and on <c>PATH</c>. Returns the
/// full path, or a failure explaining that clangd (LLVM) isn't installed — in which case the editor still runs
/// with syntax highlighting, just without semantic IntelliSense.
/// </summary>
public static class ClangdLocator
{
    public static Result<string> Locate()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LLVM", "bin", "clangd.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LLVM", "bin", "clangd.exe"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return Result<string>.Ok(candidate);
        }

        if (OnPath() is { } onPath)
            return Result<string>.Ok(onPath);

        return Result<string>.Fail("clangd not found (install LLVM, or add clangd to PATH) — running without IntelliSense.");
    }

    private static string? OnPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (path is null)
            return null;

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "clangd.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry (illegal characters) — skip it rather than fail the whole search.
            }
        }

        return null;
    }
}
