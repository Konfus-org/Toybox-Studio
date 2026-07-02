using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using Toybox.Assets;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project.Assets;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Project;

/// <summary>
/// Builds typed asset handles and OWNS asset creation — the single construction + authoring surface, injected
/// wherever an asset is acted on. Data-bearing kinds are built directly as <c>Asset&lt;TData&gt;</c> (no per-type
/// subclass); the remaining domain kinds that still scaffold their own files (World/Script/Shader) are their
/// subclass. The create menu owns the per-kind prompts and calls <see cref="CreateJsonAsync"/> here. A DI singleton.
/// </summary>
public sealed class AssetFactory(AssetServices services)
{
    // File extension → handle builder, discovered ONCE by reflection over the [AssetInfo] attribute (no hard-coded
    // extension lists): an AssetData type maps to an Asset<TData>, an Asset domain subclass (World) to itself.
    private static readonly IReadOnlyDictionary<string, Func<AssetServices, AssetMeta, Asset>> ByExtension =
        BuildExtensionMap();

    /// <summary>The shared editor services these handles route through — exposed so a caller holding the factory
    /// can also reach catalog/project/clipboard state (e.g. for the static paste helpers) without injecting both.</summary>
    public AssetServices Services => services;

    /// <summary>Builds a handle of a known typed payload directly — the agreed <c>factory.For&lt;Texture&gt;(info)</c>
    /// surface.</summary>
    public Asset<TData> For<TData>(AssetMeta info) where TData : AssetData, new() => new(services, info);

    /// <summary>Wraps a catalog entry as the right typed handle: a script by flag, a data-bearing kind as
    /// <c>Asset&lt;TData&gt;</c>, a domain kind (World) as its subclass, else an <c>Asset&lt;UnknownData&gt;</c>. No
    /// body is loaded — call <see cref="Asset.LoadAsync"/> for one.</summary>
    public Asset For(AssetMeta info)
    {
        if (info.IsScript)
            return new Asset<Script>(services, info);
        if (!string.IsNullOrEmpty(info.Type) && ByExtension.TryGetValue(info.Type, out var build))
            return build(services, info);

        return new Asset<UnknownData>(services, info);
    }

    /// <summary>Wraps a handle (resolving its full catalog entry when known) as a typed asset handle.</summary>
    public Asset For(AssetHandle handle) =>
        For(services.Catalog.Resolve(handle.Id) ?? new AssetMeta(handle.Id, handle.Name, handle.Type, handle.Path));

    /// <summary>Wraps a catalog entry by its stable id as a typed asset handle.</summary>
    public Asset For(ulong id) => For(services.Catalog.Handle(id));

    /// <summary>Whether the clipboard holds a pasteable asset handle (the counterpart to
    /// <see cref="Asset.CopyAsync"/>) — a cheap kind-tag check for gating a Paste action's visibility.</summary>
    public Task<bool> CanPasteAsync() => services.Clipboard.Has<AssetHandle>();

    /// <summary>Pastes the clipboard's copied asset. With no <paramref name="target"/> (or a target of a different
    /// type), it duplicates the copied asset into a fresh new asset; onto a <paramref name="target"/> of the same
    /// type, it pastes over that asset (replacing its data, keeping its identity) after a confirmation prompt. A
    /// no-op when the clipboard holds no asset.</summary>
    public async Task PasteAsync(Asset? target = null)
    {
        if (await services.Clipboard.Paste<AssetHandle>().ContinueOnAnyContext()
            is not { Path.Length: > 0 } copied)
            return;

        var source = For(copied);
        if (target is not null
            && string.Equals(source.Type, target.Type, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(source.Path, target.Path, StringComparison.OrdinalIgnoreCase))
        {
            await target.PasteOverAsync(source).ContinueOnAnyContext();
            return;
        }

        await source.DuplicateAsync().ContinueOnAnyContext();
    }

    /// <summary>Authors a new serialized (JSON-bodied) asset — the create the factory OWNS for the data-bearing
    /// kinds. The create menu prompts for any seed (a material's render type, an instance's base) and calls this;
    /// the factory writes the file via <c>asset.create</c>, refreshes the catalog and selects the result.</summary>
    public async Task CreateJsonAsync(
        string engineType, string subfolder, string label, string extension, JObject? seed)
    {
        if (services.Projects.CurrentProject is not { } project)
        {
            await services.Prompt.ShowErrorAsync("Can't create asset", "Open a project first.").ContinueOnAnyContext();
            return;
        }

        var (relative, _) = Asset.UniquePath(project, subfolder, "New " + label, extension);
        var request = seed is null
            ? (object)new { Type = engineType, Path = relative }
            : new { Type = engineType, Path = relative, Body = seed };
        var write = await services.Engine
            .SendCommand<JObject>(EngineMethods.AssetCreate, request, CancellationToken.None).ContinueOnAnyContext();
        if (write is not { Success: true })
        {
            await services.Prompt.ShowErrorAsync($"Couldn't create {label}", write.Error ?? "Unknown error.")
                .ContinueOnAnyContext();
            return;
        }

        await services.Catalog.RefreshAsync().ContinueOnAnyContext();
        if (services.Catalog.Find(relative) is { IsNone: false } created)
            services.Selection.Select(created);
    }

    // The extension → builder map, built once by reflecting [AssetInfo] over every concrete asset type: an
    // AssetData payload (Material/Texture/Shader/…) maps its extensions to an Asset<TData>; an Asset domain
    // subclass (World) maps to itself. No hard-coded extension lists — a new asset kind just declares [AssetInfo].
    private static Dictionary<string, Func<AssetServices, AssetMeta, Asset>> BuildExtensionMap()
    {
        var map = new Dictionary<string, Func<AssetServices, AssetMeta, Asset>>(StringComparer.OrdinalIgnoreCase);

        foreach (var data in typeof(Asset).Assembly.GetTypes())
        {
            if (data is not { IsClass: true, IsAbstract: false } || !typeof(AssetData).IsAssignableFrom(data))
                continue;
            if (data.GetCustomAttribute<AssetInfoAttribute>() is not { } file)
                continue;

            var assetType = typeof(Asset<>).MakeGenericType(data);
            foreach (var extension in file.Extensions)
                map[extension] = (s, i) => (Asset)Activator.CreateInstance(assetType, s, i, null)!;
        }

        foreach (var subclass in AssetCatalog.AssetTypes)
        {
            if (subclass.GetCustomAttribute<AssetInfoAttribute>() is not { } file)
                continue;

            foreach (var extension in file.Extensions)
                map[extension] = (s, i) => (Asset)Activator.CreateInstance(subclass, s, i, null)!;
        }

        return map;
    }

    /// <summary>Authors a new world — an engine-default <c>.world</c> body — and selects it.</summary>
    public Task CreateWorldAsync() => CreateJsonAsync("World", "Assets/Worlds", "World", ".world", seed: null);

    /// <summary>Scaffolds a new C++ script — its <c>.h</c>/<c>.cpp</c> source pair plus its identity-only
    /// <c>.h.meta</c> (engine-minted id) under <c>Source/Scripts/src/scripts/</c> — kicks off a build so codegen
    /// registers the type, refreshes the catalog, and opens the source in the code editor.</summary>
    public async Task ScaffoldScriptAsync(string className)
    {
        if (services.Projects.CurrentProject is not { } project)
        {
            await services.Prompt.ShowErrorAsync("Can't create script", "Open a project first.").ContinueOnAnyContext();
            return;
        }

        var name = SanitizeIdentifier(className);
        var directory = System.IO.Path.Combine(project.RootDirectory, "Source", "Scripts", "src", "scripts");
        Directory.CreateDirectory(directory);

        var headerPath = System.IO.Path.Combine(directory, name + ".h");
        if (File.Exists(headerPath))
        {
            await services.Prompt.ShowErrorAsync("Can't create script", $"A script named '{name}' already exists.")
                .ContinueOnAnyContext();
            return;
        }

        var id = await NewAssetIdAsync().ContinueOnAnyContext();
        if (id == 0)
        {
            await services.Prompt.ShowErrorAsync("Couldn't create script", "The engine could not mint an asset id.")
                .ContinueOnAnyContext();
            return;
        }

        var nspace = ToSnakeCase(SanitizeIdentifier(project.ModuleName));
        if (nspace.Length == 0)
            nspace = "game";

        try
        {
            File.WriteAllText(headerPath, ScriptHeader(name, nspace));
            File.WriteAllText(System.IO.Path.Combine(directory, name + ".cpp"), ScriptImplementation(name, nspace));
            File.WriteAllText(AssetPairing.MetadataPath(headerPath), ScriptMeta(id, name));
        }
        catch (Exception exception)
        {
            await services.Prompt.ShowErrorAsync("Couldn't create script", exception.Message).ContinueOnAnyContext();
            return;
        }

        services.Builder.BuildAsync(CancellationToken.None).FireAndForget();
        await services.Catalog.RefreshAsync().ContinueOnAnyContext();

        var metaRelative = System.IO.Path
            .GetRelativePath(project.RootDirectory, AssetPairing.MetadataPath(headerPath)).Replace('\\', '/');
        var info = new AssetMeta(id, name + ".h", "meta", metaRelative, IsScript: true, HasMeta: true);
        services.Opener.OpenAsync(info).FireAndForget();
    }

    /// <summary>Scaffolds a new shader source for the pipeline <paramref name="stage"/> (e.g. "vert"/"frag") plus
    /// its fresh-id <c>.meta</c> sidecar under <c>Assets/Shaders/</c>, refreshes, and opens it in the code editor.</summary>
    public async Task ScaffoldShaderAsync(string stage)
    {
        if (services.Projects.CurrentProject is not { } project)
        {
            await services.Prompt.ShowErrorAsync("Can't create shader", "Open a project first.").ContinueOnAnyContext();
            return;
        }

        var (relative, absolute) = Asset.UniquePath(project, "Assets/Shaders", "New Shader", "." + stage);
        var id = await NewAssetIdAsync().ContinueOnAnyContext();
        if (id == 0)
        {
            await services.Prompt.ShowErrorAsync("Couldn't create shader", "The engine could not mint an asset id.")
                .ContinueOnAnyContext();
            return;
        }

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, ShaderTemplate(stage));
            File.WriteAllText(AssetPairing.MetadataPath(absolute), MetaJson(id));
        }
        catch (Exception exception)
        {
            await services.Prompt.ShowErrorAsync("Couldn't create shader", exception.Message).ContinueOnAnyContext();
            return;
        }

        await services.Catalog.RefreshAsync().ContinueOnAnyContext();
        var info = new AssetMeta(id, System.IO.Path.GetFileName(relative), stage, relative);
        services.Opener.OpenAsync(info).FireAndForget();
    }

    // Mints a fresh engine id (an unsigned 64-bit Uuid) for a scaffolded file's .meta; 0 when unreachable.
    private async Task<ulong> NewAssetIdAsync()
    {
        var result = await services.Engine
            .SendCommand<JObject>(EngineMethods.AssetNewId, null, CancellationToken.None).ContinueOnAnyContext();
        return result is { Success: true, Value: { } reply } ? reply.Value<ulong?>("id") ?? 0 : 0;
    }

    // The minimal identity-only .meta sidecar JSON for a freshly authored (non-script) asset.
    private static string MetaJson(ulong id) => $"{{\n    \"id\": {id},\n    \"version\": 1\n}}\n";

    // Keeps only identifier-legal characters and ensures the result starts with a letter (a C++ class/file name).
    private static string SanitizeIdentifier(string raw)
    {
        var kept = new string(raw.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (kept.Length == 0)
            return "NewScript";
        return char.IsDigit(kept[0]) ? "_" + kept : kept;
    }

    // PascalCase/camelCase → snake_case (for a project-derived namespace), e.g. "ExampleProject" → "example_project".
    private static string ToSnakeCase(string identifier)
    {
        var builder = new StringBuilder(identifier.Length + 8);
        for (var index = 0; index < identifier.Length; index++)
        {
            var c = identifier[index];
            if (char.IsUpper(c) && index > 0 && identifier[index - 1] != '_')
                builder.Append('_');
            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private static string ScriptMeta(ulong id, string className) =>
        $"{{\n    \"id\": {id},\n    \"version\": 1,\n    \"polymorphic\": true,\n"
        + $"    \"type\": \"{className}\"\n}}\n";

    private static string ScriptHeader(string stem, string nspace) =>
        $$"""
        #pragma once
        #include "{{stem}}.generated.h"
        #include "tbx/cpp_scripting/script.h"

        namespace {{nspace}}
        {
            [[tbx::register_script]];
            [[tbx::version(1U)]];
            class {{stem}} final : public tbx::Script
            {
              public:
                {{stem}}() = default;
                ~{{stem}}() noexcept override = default;

              public:
                void on_update(const tbx::DeltaTime& dt) override;
            };
        }

        """;

    private static string ScriptImplementation(string stem, string nspace) =>
        $$"""
        #include "{{stem}}.h"

        namespace {{nspace}}
        {
            void {{stem}}::on_update(const tbx::DeltaTime& dt)
            {
                (void)dt;
            }
        }

        """;

    // A minimal, compilable starting point per shader stage.
    private static string ShaderTemplate(string stage) => stage switch
    {
        "vert" => "#version 450 core\n\nvoid main()\n{\n    gl_Position = vec4(0.0, 0.0, 0.0, 1.0);\n}\n",
        "frag" => "#version 450 core\n\nout vec4 frag_color;\n\nvoid main()\n{\n    frag_color = vec4(1.0);\n}\n",
        "comp" => "#version 450 core\n\nlayout(local_size_x = 1) in;\n\nvoid main()\n{\n}\n",
        "glsl" => "// Shared GLSL snippet — #include this from a shader stage.\n",
        _ => "#version 450 core\n\nvoid main()\n{\n}\n",
    };
}
