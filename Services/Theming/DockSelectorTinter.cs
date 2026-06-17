using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Services.Theming;

/// <summary>
/// Recolours Dock.Avalonia's drop-target selector squares to the active theme. Those squares are baked white
/// PNGs (a window glyph with a warm/yellow dock-edge highlight) shipped in Dock.Avalonia.Themes.Fluent; we
/// multiply-tint copies into resource keys, and AppStyles points each DockTarget/GlobalDockTarget selector
/// Image at the matching key.
/// </summary>
public static class DockSelectorTinter
{
    private static readonly (string Key, string Asset)[] Assets =
    [
        ("DockSelectorTopImage", "DockAnchorableTop"),
        ("DockSelectorBottomImage", "DockAnchorableBottom"),
        ("DockSelectorLeftImage", "DockAnchorableLeft"),
        ("DockSelectorRightImage", "DockAnchorableRight"),
        ("DockSelectorInsideImage", "DockDocumentInside"),
    ];

    /// <summary>
    /// Tints each selector PNG into its resource key: the warm/yellow dock-edge highlight becomes
    /// <paramref name="accent"/>; the neutral window glyph is tinted between <paramref name="ink"/> (Text) and
    /// <paramref name="paper"/> (Surface) by its lightness, so it reads dark-on-light or light-on-dark
    /// automatically.
    /// </summary>
    public static void Tint(IResourceDictionary resources, Color accent, Color ink, Color paper)
    {
        var accentHsl = accent.ToHsl();
        foreach (var (key, asset) in Assets)
        {
            var tinted = RecolorAsset($"avares://Dock.Avalonia.Themes.Fluent/Assets/{asset}.png", accentHsl, ink, paper);
            if (tinted is not null)
                resources[key] = tinted;
        }
    }

    /// <summary>
    /// Targeted recolour of a Dock selector PNG: warm/yellow pixels (the dock-edge highlight) take the accent
    /// hue/saturation at their own lightness, and the neutral window glyph is blended between <paramref name="ink"/>
    /// (Text) and <paramref name="paper"/> (Surface) by its lightness — so it reads on any background. Returns
    /// null (leaving Dock's original asset) if anything goes wrong.
    /// </summary>
    private static WriteableBitmap? RecolorAsset(string uri, HslColor accentHsl, Color ink, Color paper)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(uri));
            using var src = new Bitmap(stream);
            var size = src.PixelSize;
            var stride = size.Width * 4;
            var bytes = stride * size.Height;

            var buffer = new byte[bytes];
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                src.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes, stride);
            }
            finally
            {
                handle.Free();
            }

            // Bgra8888, alpha left untouched.
            for (var i = 0; i < bytes; i += 4)
            {
                var hsl = Color.FromRgb(buffer[i + 2], buffer[i + 1], buffer[i]).ToHsl();
                var outc = hsl is { S: > 0.20, H: >= 20 and <= 70 }
                    ? new HslColor(1.0, accentHsl.H, accentHsl.S, hsl.L).ToRgb() // warm dock-edge highlight → accent
                    : ColorMath.Blend(ink, paper, (float)hsl.L);                // neutral glyph → Text↔Surface
                buffer[i] = outc.B;
                buffer[i + 1] = outc.G;
                buffer[i + 2] = outc.R;
            }

            var bitmap = new WriteableBitmap(size, src.Dpi, PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (var fb = bitmap.Lock())
            {
                for (var y = 0; y < size.Height; y++)
                    Marshal.Copy(buffer, y * stride, fb.Address + y * fb.RowBytes, stride);
            }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
