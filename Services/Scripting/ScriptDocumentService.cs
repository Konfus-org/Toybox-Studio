using Toybox.Studio.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Toybox.Studio.Services.Scripting;

/// <summary>
/// Owns the set of open <see cref="ScriptDocument"/> buffers keyed by absolute path, so every editor surface
/// asking for the same file gets the same instance (one shared buffer). Reads source from disk on first open
/// and writes it back on save, then marks the buffer clean. Saving is the single seam the compile + hot-reload
/// pipeline hooks into.
/// </summary>
public sealed class ScriptDocumentService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ScriptDocument> _open = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the shared buffer for <paramref name="path"/>, loading it from disk on first request. A second
    /// caller for the same file gets the already-open instance regardless of unsaved edits.
    /// </summary>
    public Result<ScriptDocument> GetOrOpen(string path)
    {
        var full = Path.GetFullPath(path);
        lock (_gate)
        {
            if (_open.TryGetValue(full, out var existing))
                return Result<ScriptDocument>.Ok(existing);
        }

        string text;
        try
        {
            text = File.ReadAllText(full);
        }
        catch (Exception e)
        {
            return Result<ScriptDocument>.Fail($"Couldn't open '{full}': {e.Message}");
        }

        lock (_gate)
        {
            // Another thread may have opened it while we read; keep a single instance per path.
            if (_open.TryGetValue(full, out var existing))
                return Result<ScriptDocument>.Ok(existing);

            var document = new ScriptDocument(full, text);
            _open[full] = document;
            return Result<ScriptDocument>.Ok(document);
        }
    }

    /// <summary>Writes the document's buffer to disk and marks it clean.</summary>
    public async Task<Result> SaveAsync(ScriptDocument document, CancellationToken ct = default)
    {
        try
        {
            await File.WriteAllTextAsync(document.Path, document.Text, ct).ContinueOnAnyContext();
        }
        catch (Exception e)
        {
            return Result.Fail($"Couldn't save '{document.Path}': {e.Message}");
        }

        document.MarkSaved();
        return Result.Ok();
    }
}
