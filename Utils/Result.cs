namespace Toybox.Studio.Utils;

/// <summary>
/// The outcome of an operation that can fail in an expected way: either success carrying a value of type
/// <typeparamref name="T"/>, or failure carrying a human-readable message. Used across the editor (engine RPC
/// calls, project operations, …) so callers branch on the result instead of catching exceptions for expected
/// failures. A result converts implicitly to <see cref="bool"/>, so <c>if (result)</c> tests success directly;
/// on failure <see cref="Value"/> is default and <see cref="Error"/> explains why.
/// </summary>
public readonly record struct Result<T>(bool Success, T? Value, string? Error)
{
    public static Result<T> Ok(T value) => new(true, value, null);

    public static Result<T> Fail(string error) => new(false, default, error);

    /// <summary>Lets <c>if (result)</c> and other boolean contexts test success directly.</summary>
    public static implicit operator bool(Result<T> result) => result.Success;
}

/// <summary>
/// The outcome of an operation that returns no value: success or failure with a human-readable message.
/// Conceptually a <see cref="Result{T}"/> of <see cref="bool"/> whose value is its own success — <see cref="Value"/>
/// mirrors <see cref="Success"/> — so the non-generic form needs no type argument while still composing with the
/// generic one (the two convert implicitly both ways). Converts implicitly to <see cref="bool"/>, so
/// <c>if (result)</c> tests success directly.
/// </summary>
public readonly record struct Result(bool Success, string? Error)
{
    /// <summary>The success flag as the result's value, so this reads as a <see cref="Result{T}"/> of bool.</summary>
    public bool Value => Success;

    public static Result Ok() => new(true, null);

    public static Result Fail(string error) => new(false, error);

    /// <summary>Lets <c>if (result)</c> and other boolean contexts test success directly.</summary>
    public static implicit operator bool(Result result) => result.Success;

    public static implicit operator Result(Result<bool> result) => new(result.Success, result.Error);

    public static implicit operator Result<bool>(Result result) =>
        new(result.Success, result.Success, result.Error);
}
