namespace Toybox.Studio.Projects;

/// <summary>
/// The build mode matching the studio's own configuration: a Debug Studio drives Debug native binaries,
/// a Release Studio Release ones. Callers that build "whatever matches the studio" (the engine launch,
/// the Build ▸ Engine menu) use this instead of re-deriving it from compilation symbols.
/// </summary>
public static class StudioBuild
{
    public const BuildMode Mode =
#if DEBUG
        BuildMode.Debug;
#else
        BuildMode.Release;
#endif
}
