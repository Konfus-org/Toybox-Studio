namespace Toybox.Studio.Utils;

/// <summary>
/// A single-entry gate that hands out disposable scopes to guard against re-entrant or concurrent runs of the
/// same work. <see cref="TryEnter"/> returns a scope when the gate is free and <c>null</c> when one is already
/// held, so a caller can bail instead of running the guarded work twice; disposing the scope (e.g. at the end
/// of a <c>using</c> block) releases the gate. An optional <c>toggle</c> is set <c>true</c> on a successful
/// enter and <c>false</c> when the scope is disposed, so a "busy" flag tracks the scope for free — no
/// <c>try/finally</c> to reset it. Thread-safe, and dispose is idempotent. The intended shape:
/// <code>
/// using var scope = _gate.TryEnter(busy => Busy = busy);
/// if (scope is null)
///     return; // already running — don't run it again
/// // ... guarded work; Busy is true until the using block exits ...
/// </code>
/// </summary>
public sealed class ReentrancyGuard
{
    private int _held;

    /// <summary>Whether a scope is currently active (the gate is held).</summary>
    public bool IsHeld => Volatile.Read(ref _held) == 1;

    /// <summary>
    /// Enters the gate when it is free, returning a scope to dispose once the guarded work is done; returns
    /// <c>null</c> when the gate is already held (the caller should not proceed). Disposing a null scope is a
    /// no-op, so the result can be assigned straight to a <c>using</c> variable. When given, <paramref name="toggle"/>
    /// is invoked with <c>true</c> on a successful enter and <c>false</c> on dispose — so a backing "busy" flag
    /// follows the scope without a separate <c>try/finally</c>. It is not called when the gate is already held.
    /// </summary>
    public IDisposable? TryEnter(Action<bool>? toggle = null)
    {
        if (Interlocked.Exchange(ref _held, 1) != 0)
            return null;

        toggle?.Invoke(true);
        return new Scope(this, toggle);
    }

    private void Release() => Interlocked.Exchange(ref _held, 0);

    // The handle a caller disposes to release the gate. Drops its guard reference on first dispose so a
    // double-dispose can't toggle the flag back or release a scope a later TryEnter handed to another caller.
    private sealed class Scope(ReentrancyGuard guard, Action<bool>? toggle) : IDisposable
    {
        private ReentrancyGuard? _guard = guard;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _guard, null) is not { } owner)
                return;

            toggle?.Invoke(false);
            owner.Release();
        }
    }
}
