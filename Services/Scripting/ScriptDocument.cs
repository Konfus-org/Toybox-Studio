using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Toybox.Studio.Services.Scripting;

/// <summary>
/// One open script source file as a single shared buffer. Both editor surfaces — the inline inspector strip
/// and the popped-out window — bind to the same instance (vended by <see cref="ScriptDocumentService"/> keyed
/// by path), so the unsaved text, dirty state, and title stay consistent no matter which surface the user
/// typed in. Dirtiness is derived by comparing the live <see cref="Text"/> against the last text written to
/// disk, mirroring how the rest of the editor tracks document ownership.
/// </summary>
public sealed partial class ScriptDocument : ObservableObject
{
    // Serialises writes to this file across editor surfaces. Two quick Ctrl+S (or a save from each surface)
    // would otherwise issue concurrent File.WriteAllTextAsync to the same path — a sharing violation and a
    // nondeterministic MarkSaved. Saves run one at a time and coalesce: the second waits, then writes the
    // latest buffer (the writer reads Text at write time), so MarkSaved ordering is deterministic.
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    private string _savedText;

    public ScriptDocument(string path, string text)
    {
        Path = path;
        Language = LanguageFor(path);
        Text = text;
        _savedText = text;
    }

    /// <summary>Absolute path of the source file on disk.</summary>
    public string Path { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Monaco language id (always <c>cpp</c> for engine scripts and their headers; json for data).</summary>
    public string Language { get; }

    /// <summary>The live (possibly unsaved) buffer text.</summary>
    [ObservableProperty]
    public partial string Text { get; private set; }

    /// <summary>True when the buffer differs from what's on disk.</summary>
    [ObservableProperty]
    public partial bool IsDirty { get; private set; }

    /// <summary>
    /// Raised when the buffer is replaced wholesale from outside an editor surface (initial load, disk reload,
    /// post-save normalisation), so any attached surface re-pushes the authoritative text into its Monaco model.
    /// </summary>
    public event Action? Reloaded;

    /// <summary>
    /// Raised when one live editor surface edits the shared buffer, carrying the surface that originated the
    /// change. Other live surfaces showing this document re-sync their Monaco model; the originator skips its
    /// own notification so it doesn't echo the edit back onto itself (resetting the cursor). The argument is an
    /// opaque origin token — surfaces only ever compare it by reference against <c>this</c> — so a subscriber
    /// can be (un)subscribed purely on attach/detach without ever pinning a dead view-model.
    /// </summary>
    public event Action<object?>? ExternalEdit;

    /// <summary>
    /// Applies an edit reported by an editor surface; recomputes dirtiness and notifies the OTHER live surfaces
    /// to re-sync (does not re-push to <paramref name="origin"/>). Pass the originating surface as
    /// <paramref name="origin"/> so it doesn't echo the change back onto itself.
    /// </summary>
    public void SetFromEditor(string text, object? origin = null)
    {
        if (text == Text)
            return;

        Text = text;
        IsDirty = !string.Equals(Text, _savedText, StringComparison.Ordinal);
        ExternalEdit?.Invoke(origin);
    }

    /// <summary>Marks the current buffer as the on-disk truth (after a successful save).</summary>
    public void MarkSaved()
    {
        _savedText = Text;
        IsDirty = false;
    }

    /// <summary>
    /// Runs <paramref name="save"/> under this document's per-file save gate, so concurrent saves to the same
    /// path (two quick Ctrl+S, or a save from each surface) serialise instead of racing the file. Overlapping
    /// callers queue behind the in-flight write and then run against the latest buffer.
    /// </summary>
    public async Task<T> RunSaveAsync<T>(Func<Task<T>> save, CancellationToken ct = default)
    {
        await _saveGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await save().ConfigureAwait(false);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    /// <summary>Replaces the buffer with fresh content from disk and notifies surfaces to re-push it.</summary>
    public void ReplaceFromDisk(string text)
    {
        _savedText = text;
        Text = text;
        IsDirty = false;
        Reloaded?.Invoke();
    }

    private static string LanguageFor(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => "json",
        _ => "cpp",
    };
}
