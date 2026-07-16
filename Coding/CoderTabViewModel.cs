using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Toybox.Studio.Coding;

/// <summary>
/// One open document tab in the <see cref="CoderViewModel"/>'s tab strip. Wraps the shared
/// <see cref="ScriptDocument"/> buffer so the tab title shows the file name and a dirty dot that tracks the
/// document's unsaved state. Closing the tab routes back to the editor via the supplied callback.
/// </summary>
public sealed partial class CoderTabViewModel : ObservableObject
{
    private readonly Action<CoderTabViewModel> _close;

    public CoderTabViewModel(ScriptDocument document, Action<CoderTabViewModel> close)
    {
        Document = document;
        _close = close;
        document.PropertyChanged += OnDocumentChanged;
    }

    public ScriptDocument Document { get; }

    public string Path => Document.Path;

    public string Title => Document.FileName;

    public bool IsDirty => Document.IsDirty;

    /// <summary>True for the tab whose document is currently shown in the editor.</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [RelayCommand]
    private void Close() => _close(this);

    /// <summary>Drops the document subscription when the tab is closed, so the buffer doesn't pin the tab.</summary>
    public void Detach() => Document.PropertyChanged -= OnDocumentChanged;

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScriptDocument.IsDirty))
            OnPropertyChanged(nameof(IsDirty));
    }
}
