using System;
using System.IO;
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

    /// <summary>Applies an edit reported by an editor surface; recomputes dirtiness, does not re-push.</summary>
    public void SetFromEditor(string text)
    {
        if (text == Text)
            return;

        Text = text;
        IsDirty = !string.Equals(Text, _savedText, StringComparison.Ordinal);
    }

    /// <summary>Marks the current buffer as the on-disk truth (after a successful save).</summary>
    public void MarkSaved()
    {
        _savedText = Text;
        IsDirty = false;
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
