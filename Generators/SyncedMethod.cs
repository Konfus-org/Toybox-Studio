using System.Collections.Generic;
using System.Linq;

namespace Toybox.Studio.Generators;

/// <summary>Everything the emitter needs for one engine-synced partial method (and its optional
/// relay command).</summary>
internal sealed class SyncedMethod(
    string name,
    string accessibility,
    string command,
    bool relay,
    List<SyncedMethod.Parameter> parameters,
    List<SyncedExtra> extras)
{
    public string Name { get; } = name;

    public string Accessibility { get; } = accessibility;

    public string Command { get; } = command;

    /// <summary>Also emit a bindable async command wrapping the method (view-model classes only).</summary>
    public bool Relay { get; } = relay;

    public List<Parameter> Parameters { get; } = parameters;

    public List<SyncedExtra> Extras { get; } = extras;

    public string? CancellationTokenName =>
        Parameters.FirstOrDefault(parameter => parameter.IsCancellationToken)?.Name;

    public IEnumerable<Parameter> PayloadParameters =>
        Parameters.Where(parameter => !parameter.IsCancellationToken);

    /// <summary>PlayAsync → PlayCommand.</summary>
    public string CommandPropertyName =>
        (Name.EndsWith("Async", System.StringComparison.Ordinal) ? Name.Substring(0, Name.Length - 5) : Name)
        + "Command";

    /// <summary>One declared method parameter: its payload key, or the call's CancellationToken.</summary>
    internal sealed class Parameter(string name, string typeDisplay, bool isCancellationToken, string writeCall)
    {
        public string Name { get; } = name;

        public string TypeDisplay { get; } = typeDisplay;

        public bool IsCancellationToken { get; } = isCancellationToken;

        /// <summary>The codec call over the parameter name producing its wire value.</summary>
        public string WriteCall { get; } = writeCall;

        public string Key => char.ToLowerInvariant(Name[0]) + Name.Substring(1);
    }
}
