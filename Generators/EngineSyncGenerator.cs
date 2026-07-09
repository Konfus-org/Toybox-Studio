using Microsoft.CodeAnalysis;

namespace Toybox.Studio.Generators;

/// <summary>
/// The engine-sync code generator: for every class opted in with <c>[EngineSync]</c> it implements the
/// marked partial properties (backing field, notify, push per <c>SyncMode</c>, inbound apply), partial
/// events (subscription-driven accessors plus the inbound raise), and partial methods (engine commands
/// — with reply decoding for <c>Task&lt;Result&lt;T&gt;&gt;</c> queries — optionally wrapped in relay
/// commands), and injects the <c>EngineObject</c> base so user code never inherits it by hand. See
/// <c>EngineApi/EngineSyncAttribute.cs</c> for the API this generator implements.
/// </summary>
[Generator]
public sealed class EngineSyncGenerator : IIncrementalGenerator
{
    internal const string AttributeName = "Toybox.Studio.EngineApi.EngineSyncAttribute";
    internal const string EngineObjectName = "Toybox.Studio.EngineApi.EngineObject";
    internal const string EngineName = "Toybox.Studio.EngineApi.Engine";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classes = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => SyncClassParser.LooksSynced(node),
                static (syntaxContext, ct) => SyncClassParser.Parse(syntaxContext, ct))
            .Where(static model => model is not null);

        context.RegisterSourceOutput(classes, static (production, model) =>
        {
            foreach (var diagnostic in model!.Diagnostics)
                production.ReportDiagnostic(diagnostic.ToDiagnostic());

            if (model.Emit)
                production.AddSource(model.HintName, SyncClassEmitter.Emit(model));
        });
    }
}
