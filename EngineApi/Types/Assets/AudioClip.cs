using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>
/// An audio asset (<c>.wav</c>/<c>.mp3</c>/<c>.ogg</c>/<c>.flac</c>), mirrored from the engine's
/// <c>AudioClip</c>. Identity-only: the sample data, rate, and channel count are decoded by the audio
/// loader from the source clip (a version-only asset — none of it serializes), so there is nothing
/// beyond the <see cref="Asset"/> identity to mirror.
/// </summary>
[AssetExtensions("wav", "mp3", "ogg", "flac")]
public sealed class AudioClip : Asset
{
    public AudioClip(ulong id = 0) : base(id)
    {
    }
}
