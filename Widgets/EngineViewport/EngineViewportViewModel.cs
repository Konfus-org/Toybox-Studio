using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.EngineViewport;

public sealed partial class EngineViewportViewModel : ObservableObject
{
    public EngineViewportViewModel(EngineViewportService viewport, EngineSessionService session)
    {
        viewport.FrameArrived += frame => Dispatcher.UIThread.Post(() => Render(frame));
        session.StateChanged += state => Dispatcher.UIThread.Post(() =>
        {
            if (state != EngineConnectionState.Connected)
                HasFrames = false;
        });
    }

    /// <summary>Raised after new pixels were written so the view can invalidate the image.</summary>
    public event Action? FrameRendered;

    [ObservableProperty]
    public partial WriteableBitmap? Frame { get; private set; }

    [ObservableProperty]
    public partial bool HasFrames { get; private set; }

    private void Render(ViewportFrame frame)
    {
        if (Frame is null
            || Frame.PixelSize.Width != frame.Width
            || Frame.PixelSize.Height != frame.Height)
        {
            Frame = new WriteableBitmap(
                new PixelSize(frame.Width, frame.Height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque);
        }

        using (var pixels = Frame.Lock())
        {
            if (pixels.RowBytes == frame.Stride)
            {
                Marshal.Copy(frame.Data, 0, pixels.Address, frame.Height * frame.Stride);
            }
            else
            {
                for (var row = 0; row < frame.Height; row++)
                {
                    Marshal.Copy(
                        frame.Data,
                        row * frame.Stride,
                        pixels.Address + row * pixels.RowBytes,
                        frame.Stride);
                }
            }
        }

        HasFrames = true;
        FrameRendered?.Invoke();
    }
}
