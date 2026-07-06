namespace Toybox.Studio.Ecs;

/// <summary>An integer vector triple, mirroring the engine's <c>IVec3</c> — used for logical ids such
/// as world chunk coordinates rather than continuous positions.</summary>
public readonly record struct IVec3(int X, int Y, int Z);
