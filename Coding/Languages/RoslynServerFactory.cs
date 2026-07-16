using System.Diagnostics;
using System.Text;
using Toybox.Studio.Coding;
using Toybox.Studio.Monaco;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Coding.Languages;

/// <summary>
/// Launches the Roslyn language server (<c>Microsoft.CodeAnalysis.LanguageServer</c>) for C#, giving the editor
/// real go-to-definition / hover / completion over the Studio's own sources. The server loads nothing from the
/// initialize <c>rootUri</c>, so after the handshake the page sends a <c>solution/open</c> (or, failing a
/// solution file, <c>project/open</c>) notification — supplied here as the server's post-initialise step — and
/// waits for <c>workspace/projectInitializationComplete</c>. When the server DLL or the Studio solution can't be
/// found, C# stays highlight-only.
/// </summary>
public sealed class RoslynServerFactory : ILanguageServerFactory
{
    public IReadOnlyList<string> LanguageIds { get; } = [ScriptLanguages.CSharp.Id];

    public Result<ILanguageServer?> Start(MonacoSession session, LanguageServerContext context)
    {
        var serverDll = RoslynServerLocator.Locate();
        if (serverDll is null)
            return Result<ILanguageServer?>.Ok(null); // Server not vendored/installed: highlight-only.

        var workspaceLoad = ResolveWorkspace(context.StudioRoot);
        if (workspaceLoad is null)
            return Result<ILanguageServer?>.Ok(null); // No solution/projects to analyse: highlight-only.

        var logDirectory = Path.Combine(Path.GetTempPath(), "ToyboxStudio", "RoslynLsp");
        try
        {
            Directory.CreateDirectory(logDirectory);
        }
        catch (Exception)
        {
            // A missing log dir isn't fatal; the server tolerates it.
        }

        var info = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = context.StudioRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        info.ArgumentList.Add(serverDll);
        info.ArgumentList.Add("--stdio");
        info.ArgumentList.Add("--logLevel");
        info.ArgumentList.Add("Warning");
        info.ArgumentList.Add("--extensionLogDirectory");
        info.ArgumentList.Add(logDirectory);
        info.ArgumentList.Add($"--clientProcessId={Environment.ProcessId}");

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception e)
        {
            return Result<ILanguageServer?>.Fail($"Couldn't start the Roslyn C# server: {e.Message}");
        }

        var server = new StdioLspServer("roslyn", LanguageIds, session, process, context.Log, "roslyn");
        session.EnableLsp(
            "roslyn",
            new Uri(context.StudioRoot).AbsoluteUri,
            LanguageIds,
            initializationOptions: new { },
            postInitialize: [workspaceLoad]);
        return Result<ILanguageServer?>.Ok(server);
    }

    // The post-initialise notification that makes Roslyn load a workspace: a solution when the .slnx is present,
    // else the loose projects discovered under the studio root. Null when there's nothing to load.
    private static object? ResolveWorkspace(string studioRoot)
    {
        if (string.IsNullOrEmpty(studioRoot) || !Directory.Exists(studioRoot))
            return null;

        var solution = Path.Combine(studioRoot, "Toybox.Studio.slnx");
        if (File.Exists(solution))
            return new { method = "solution/open", @params = new { solution = FileUri(solution) } };

        string[] projects;
        try
        {
            projects = Directory.GetFiles(studioRoot, "*.csproj", SearchOption.AllDirectories)
                .Where(path => !IsUnderBuildOutput(path))
                .ToArray();
        }
        catch (Exception)
        {
            return null;
        }

        if (projects.Length == 0)
            return null;

        var uris = projects.Select(FileUri).ToArray();
        return new { method = "project/open", @params = new { projects = uris } };
    }

    private static bool IsUnderBuildOutput(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/build/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static string FileUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;
}
