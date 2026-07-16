using System.Reflection;
using Toybox.Studio.EngineApi.Types;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>
/// The extension→asset-type registry, reflected once from the asset payload classes' declared extensions
/// (<see cref="AssetExtensionsAttribute"/> for imported kinds, <see cref="CreatableAttribute"/> for
/// authored ones). Lets a consumer classify an <see cref="AssetEntry.Type"/> token by the asset class it
/// resolves to, with the single source of truth being the asset classes themselves rather than extension
/// lists copied into each caller.
/// </summary>
public static class AssetKinds
{
    private static readonly IReadOnlyDictionary<string, Type> ByExtension = Build();

    /// <summary>The asset payload type an extension token (with or without a leading dot) resolves to, or
    /// null when no asset class claims it.</summary>
    public static Type? ForExtension(string extension) =>
        ByExtension.GetValueOrDefault(extension.TrimStart('.'));

    /// <summary>Whether the extension token resolves to the <typeparamref name="TAsset"/> asset type.</summary>
    public static bool Is<TAsset>(string extension) where TAsset : Asset =>
        ForExtension(extension) == typeof(TAsset);

    private static IReadOnlyDictionary<string, Type> Build()
    {
        var map = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in typeof(Asset).Assembly.GetTypes())
        {
            if (!type.IsSubclassOf(typeof(Asset)))
                continue;

            foreach (var extension in ExtensionsOf(type))
                map[extension] = type;
        }

        return map;
    }

    private static IEnumerable<string> ExtensionsOf(Type type)
    {
        if (type.GetCustomAttribute<AssetExtensionsAttribute>() is { } recognized)
            foreach (var extension in recognized.Extensions)
                yield return extension;

        // An authored body asset's creation extension is also one it's recognized from.
        if (type.GetCustomAttribute<CreatableAttribute>() is { } creatable)
            yield return creatable.Extension;
    }
}
