using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Widgets.Viewport;

public sealed partial class ViewportViewModel : ObservableObject
{
    public ViewportViewModel(ViewportStream viewport, Session session)
    {
        viewport.FrameArrived += frame => Dispatch.To(DispatchContext.UI, () => Render(frame));
        session.StateChanged += state => Dispatch.To(DispatchContext.UI, () =>
        {
            if (state != ConnectionState.Connected)
                HasFrames = false;
        });
        session.BusyChanged += busy => Dispatch.To(DispatchContext.UI, () => IsBusy = busy);
    }

    [ObservableProperty]
    public partial WriteableBitmap? Frame { get; private set; }

    /// <summary>
    /// Bumped after each frame's pixels are written. The view binds it through the FrameInvalidation
    /// behavior to repaint the image — the bitmap is mutated in place, so its reference never changes.
    /// </summary>
    [ObservableProperty]
    public partial int FrameTick { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyGhost))]
    public partial bool HasFrames { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyGhost))]
    public partial bool IsBusy { get; private set; }

    /// <summary>The empty-state ghost shows only when there's nothing to draw and we're not busy
    /// (the loading ghost owns the busy state), so the two never overlap.</summary>
    public bool ShowEmptyGhost => !HasFrames && !IsBusy;

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
        FrameTick++;
    }
}
