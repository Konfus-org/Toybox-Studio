using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Newtonsoft.Json;
using Toybox.Studio.Services.Scripting;

namespace Toybox.Studio.Widgets.ScriptEditor;

/// <summary>
/// Hosts the Monaco editor page in a native WebView and wires it to a <see cref="MonacoSession"/>: it points
/// the view at the session's page URL, forwards the page's messages into <see cref="MonacoSession.Receive"/>,
/// and supplies the outbound channel (script injection of <c>window.__tbx.receive</c>). All protocol and
/// document state lives on the session and the shared <see cref="ScriptDocument"/>; this control is just the
/// surface, following the same VM-owns-the-object pattern as the composition viewport.
///
/// When <see cref="Offscreen"/> is set the WebView2 backend is asked to render off-screen so the editor
/// composites with surrounding Avalonia content instead of floating above it (native airspace) — used by the
/// inline inspector strip, which sits inside a scrolling panel. The dockable window leaves it native (faster).
/// </summary>
public sealed class MonacoEditorControl : NativeWebView
{
    /// <summary>The bridge/document endpoint this surface displays. Null shows a blank page.</summary>
    public static readonly StyledProperty<MonacoSession?> SessionProperty =
        AvaloniaProperty.Register<MonacoEditorControl, MonacoSession?>(nameof(Session));

    /// <summary>Request off-screen (composited) rendering instead of a native overlay. Set before attach.</summary>
    public static readonly StyledProperty<bool> OffscreenProperty =
        AvaloniaProperty.Register<MonacoEditorControl, bool>(nameof(Offscreen));

    private MonacoSession? _attached;

    public MonacoEditorControl()
    {
        // A dark background avoids a white flash before Monaco's dark chrome paints.
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        EnvironmentRequested += OnEnvironmentRequested;
        WebMessageReceived += OnWebMessageReceived;
    }

    public MonacoSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public bool Offscreen
    {
        get => GetValue(OffscreenProperty);
        set => SetValue(OffscreenProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SessionProperty)
            Bind(change.GetNewValue<MonacoSession?>());
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached?.DetachTransport();
        _attached = null;
    }

    private void Bind(MonacoSession? session)
    {
        if (ReferenceEquals(session, _attached))
            return;

        _attached?.DetachTransport();
        _attached = session;
        if (session is null)
            return;

        // Outbound (host -> page): inject a call to the page's receive() with the envelope as a JS string
        // literal (SerializeObject quotes/escapes it); the page JSON.parses it back into an object.
        session.AttachTransport(json => InvokeScript($"window.__tbx.receive({JsonConvert.SerializeObject(json)})"));
        Source = session.PageUri;
    }

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        // WebMessageReceived already fires on the UI thread; the session and document mutate UI state.
        if (e.Body is { } body)
            _attached?.Receive(body);
    }

    private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        if (Offscreen && e is WindowsWebView2EnvironmentRequestedEventArgs windows)
            windows.ExperimentalOffscreen = true;
    }
}
