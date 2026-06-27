using Toybox.Studio.Utils;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.Rpc;

namespace Toybox.Studio.Services.EngineApi;

/// <summary>The engine's reply to the editor.hello handshake.</summary>
public sealed record Hello(int ProtocolVersion, string Engine, string App);

/// <summary>The engine's reply to view.start: the new view's id and pixel format.</summary>
public sealed record ViewInfo(string Name, string Format, uint WorldId = 0);

/// <summary>
/// One entity's projected screen position in a <c>view.projectEntities</c> reply: the entity id plus its
/// normalized image coordinates (<see cref="U"/>/<see cref="V"/>, top-left origin, in front of the camera)
/// and world-space distance from the camera (<see cref="Depth"/>, for distance fade). The editor polls for
/// these and joins them against the world snapshot to draw name labels + component icon stacks.
/// </summary>
public sealed record BillboardPosition(ulong Id, double U, double V, double Depth);

/// <summary>
/// A <c>view.surface</c> notification: the engine has created (or failed to create) a view's shared
/// GPU texture. <see cref="Handle"/> is a Windows global shared D3D11 texture handle the editor's
/// compositor imports directly; a zero handle means GPU sharing was unavailable for this view.
/// </summary>
public sealed record ViewSurface(string Name, long Handle, int Width, int Height, string Format);

/// <summary>
/// The editor's connection to the engine, and the single channel every engine call is piped through: the
/// engine-specific facade over a generic <see cref="RpcClient"/>. It performs the editor.hello handshake,
/// exposes the engine's inbound notifications as typed events, and fronts the engine-lifecycle / viewport
/// calls that only services make. The entity / component / world / asset / settings domain calls live on
/// their own editor-side constructs (<see cref="Toybox.Studio.Services.World.WorldManager"/>,
/// <see cref="Toybox.Studio.Services.World.Entity"/>, <see cref="Toybox.Studio.Services.World.Component"/>,
/// <see cref="Toybox.Studio.Services.Project.AssetCatalog"/>,
/// <see cref="Toybox.Studio.Services.Settings.EngineSettings"/>), which build on the internal
/// <see cref="InvokeAsync{T}"/>/<see cref="NotifyAsync"/> primitives here. <see cref="Session"/> drives the
/// connection (<see cref="ConnectAsync"/>/<see cref="Disconnect"/>).
///
/// Safety is enforced here so callers don't have to think about it: every request is <b>guarded</b> (the
/// transport turns any error, including "not connected", into a failure <see cref="Result"/> rather than an
/// exception) and <b>bounded</b> (each call is linked to a default deadline — <see cref="DefaultRequestTimeout"/>
/// — so a hung or crashed engine can never park an editor operation forever). A caller therefore stays safe
/// passing <see cref="CancellationToken.None"/>; it only needs an explicit token to cancel early, and the
/// <c>timeout</c> overloads to pick a tighter or looser bound. Liveness detection (is the engine frozen?) is
/// the <see cref="Session"/> watchdog's job; this layer just guarantees each individual await completes.
/// </summary>
public sealed class EngineRpc : IAsyncDisposable
{
    /// <summary>
    /// The default deadline applied to every request that doesn't pass an explicit one. Generous enough for a
    /// large <c>world.describe</c> or a cold settings/asset describe, but finite so the editor never blocks
    /// indefinitely on an engine that wedged. Notifications (fire-and-forget) carry no reply and so no timeout.
    /// </summary>
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(15);

    private readonly RpcClient _rpc = new();

    public EngineRpc() => _rpc.Disconnected += () => Disconnected?.Invoke();

    /// <summary>Raised for every engine.log notification streamed by the engine.</summary>
    public event Action<LogEntry>? LogReceived;

    /// <summary>
    /// Raised for every view.surface notification: a view's shared GPU texture is ready (or failed).
    /// Each <see cref="ViewportStream"/> filters by view name. Fired on the RPC listener thread.
    /// </summary>
    public event Action<ViewSurface>? SurfaceReceived;

    /// <summary>
    /// Raised for every view.presented notification: a view has drawn its first real frame into the
    /// shared surface (sent once per view, after the surface is created). Carries the view name.
    /// Fired on the RPC listener thread.
    /// </summary>
    public event Action<string>? ViewPresented;

    /// <summary>
    /// Raised for every input.mouseLock notification: the playing game's mouse-lock mode changed
    /// ("unlocked", "relative", or "grabbed"). Lets the game panel capture the cursor for mouselook.
    /// Fired on the RPC listener thread.
    /// </summary>
    public event Action<string>? MouseLockModeChanged;

    /// <summary>
    /// Raised for every view.transformEdited notification: the engine's transform gizmo finished moving one
    /// or more entities, so the editor should mark the world dirty and refresh. Fired on the RPC thread.
    /// </summary>
    public event Action? TransformEdited;

    /// <summary>Raised when the connection drops for any reason.</summary>
    public event Action? Disconnected;

    public bool IsConnected => _rpc.IsConnected;

    /// <summary>
    /// Connects, retrying while the engine boots, then performs the hello handshake. Returns the handshake
    /// on success or a failure result (e.g. on timeout or a failed handshake); never throws.
    /// </summary>
    public async Task<Result<Hello>> ConnectAsync(int port, TimeSpan timeout, CancellationToken ct)
    {
        var connected = await _rpc.ConnectAsync(port, timeout, RegisterHandlers, ct).ContinueOnAnyContext();
        if (!connected.Success)
            return Result<Hello>.Fail(connected.Error!);

        var hello = await InvokeAsync<Hello>(
                "editor.hello", new { ProtocolVersion = 1, Client = "Toybox Studio" }, ct)
            .ContinueOnAnyContext();
        if (!hello.Success)
        {
            _rpc.Disconnect();
            return Result<Hello>.Fail(hello.Error!);
        }

        return Result<Hello>.Ok(hello.Value!);
    }

    /// <summary>Tears down the current connection (no-op if already disconnected).</summary>
    public void Disconnect() => _rpc.Disconnect();

    public ValueTask DisposeAsync() => _rpc.DisposeAsync();

    /// <summary>Round-trips an <c>engine.ping</c> and, on success, reports the measured latency.</summary>
    public async Task<Result<TimeSpan>> PingAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await InvokeAsync("engine.ping", null, ct).ContinueOnAnyContext();
        return result.Success
            ? Result<TimeSpan>.Ok(stopwatch.Elapsed)
            : Result<TimeSpan>.Fail(result.Error!);
    }

    /// <summary>Pauses or resumes the engine's simulation; rendering keeps running.</summary>
    public Task<Result> SetPausedAsync(bool isPaused, CancellationToken ct) =>
        InvokeAsync("engine.setPaused", new { IsPaused = isPaused }, ct);

    /// <summary>
    /// Enters or exits play mode. Entering snapshots the world and runs the game loop; exiting
    /// restores the pre-play world and halts simulation. The engine keeps rendering either way.
    /// </summary>
    public Task<Result> SetPlayingAsync(bool isPlaying, CancellationToken ct) =>
        InvokeAsync("engine.setPlaying", new { IsPlaying = isPlaying }, ct);

    /// <summary>Asks the engine to exit gracefully.</summary>
    public Task<Result> ShutdownAsync(CancellationToken ct) =>
        InvokeAsync("engine.shutdown", null, ct);

    /// <summary>
    /// Pushes the editor's log colors to the engine console. Best-effort cosmetic sync, so it returns a bare
    /// task the logger can fire and forget.
    /// </summary>
    public Task SetLogColorsAsync(string info, string warning, string error, CancellationToken ct) =>
        InvokeAsync("engine.setLogColors", new { Info = info, Warning = warning, Error = error }, ct);

    /// <summary>Pushes one editor-originated log line into the engine's unified log (fire-and-forget).</summary>
    public Task WriteLogAsync(string level, string message) =>
        NotifyAsync("editor.log", new { Level = level, Message = message });

    /// <summary>
    /// Asks the engine to start a new view and returns its unique id. Each call creates an
    /// independent view with its own engine camera: an editor camera (free, spawned at the game
    /// camera), a game view that mirrors the game camera, or an asset-preview view that orbits an
    /// isolated world holding the asset identified by <paramref name="assetId"/>. The view's shared
    /// GPU texture arrives shortly after as a <see cref="SurfaceReceived"/> notification (created on
    /// the render lane).
    /// </summary>
    public Task<Result<ViewInfo>> StartViewAsync(ViewKind kind, CancellationToken ct, long assetId = 0)
    {
        var kindToken = kind switch
        {
            ViewKind.Game => "game",
            ViewKind.AssetPreview => "asset",
            _ => "editor",
        };
        return InvokeAsync<ViewInfo>("view.start", new { Kind = kindToken, AssetId = assetId }, ct);
    }

    /// <summary>Stops the engine view with the given id (from <see cref="StartViewAsync"/>).</summary>
    public Task<Result> StopViewAsync(string name, CancellationToken ct) =>
        InvokeAsync("view.stop", new { Name = name }, ct);

    /// <summary>
    /// Forwards the focused viewport's input to its engine view (fire-and-forget notification). Drives
    /// the editor fly camera; mouse/wheel values are deltas since the last call. Buttons: bit0 left,
    /// bit1 right, bit2 middle. MoveKeys: bit0 fwd, 1 back, 2 left, 3 right, 4 up, 5 down.
    /// </summary>
    public Task SendViewInputAsync(
        string view, bool focused, int buttons, int moveKeys,
        IReadOnlyList<InputKey> keys, double mouseX, double mouseY, double dx, double dy, double wheel,
        double cursorU, double cursorV) =>
        NotifyAsync(
            "view.input",
            new
            {
                View = view, Focused = focused, Buttons = buttons, MoveKeys = moveKeys,
                Keys = keys, MouseX = mouseX, MouseY = mouseY, Dx = dx, Dy = dy, Wheel = wheel,
                CursorU = cursorU, CursorV = cursorV,
            });

    /// <summary>Sets the engine's active transform tool (fire-and-forget); drives the viewport gizmo.</summary>
    public Task SetGizmoAsync(string mode) =>
        NotifyAsync("view.setGizmo", new { Mode = mode });

    /// <summary>
    /// Frames an asset-preview world's orbit camera to the previewed entity's renderable bounds — called by
    /// the editor after it builds or swaps the previewed entity through the world/entity API.
    /// <paramref name="worldId"/> is the preview world id returned from <see cref="StartViewAsync"/>.
    /// </summary>
    public Task<Result> FrameAssetPreviewAsync(uint worldId, CancellationToken ct) =>
        InvokeAsync("view.frameAssetPreview", new { WorldId = worldId }, ct);

    /// <summary>
    /// Ensures an in-memory material instance that shows <paramref name="textureId"/> on the bundled unlit
    /// preview material, returning its id — the asset viewer sets a Renderer slot to it to show a texture on
    /// a primitive (a texture isn't itself a material, and the editor can't register in-memory assets).
    /// </summary>
    public async Task<Result<long>> PreviewTextureMaterialAsync(long textureId, CancellationToken ct)
    {
        var result = await InvokeAsync<JObject>(
            "editor.previewTextureMaterial", new { TextureId = textureId }, ct).ContinueOnAnyContext();
        return result is { Success: true, Value: { } reply }
            ? Result<long>.Ok(reply.Value<long>("id"))
            : Result<long>.Fail(result.Error ?? "The engine returned no result.");
    }

    /// <summary>
    /// Picks the entity under a viewport click. <paramref name="u"/>/<paramref name="v"/> are normalized image
    /// coordinates in [0,1] with the origin at the top-left (the pointer convention); the engine builds a ray
    /// from that view's camera and returns the nearest hit entity id, or null for empty space.
    /// </summary>
    public async Task<Result<ulong?>> PickAsync(string view, double u, double v, CancellationToken ct)
    {
        var result = await InvokeAsync<JToken>("view.pick", new { View = view, U = u, V = v }, ct)
            .ContinueOnAnyContext();
        return result is { Success: true, Value: { } reply }
            ? Result<ulong?>.Ok(reply.Value<ulong?>("id"))
            : Result<ulong?>.Fail(result.Error ?? "The engine returned no result.");
    }

    /// <summary>
    /// Box-selects entities inside a viewport marquee. The rect corners are normalized image coordinates
    /// in [0,1] (top-left origin); the engine returns the ids of entities whose bounds fall inside.
    /// </summary>
    public async Task<Result<IReadOnlyList<ulong>>> PickRectAsync(
        string view, double u0, double v0, double u1, double v1, CancellationToken ct)
    {
        var result = await InvokeAsync<JToken>(
                "view.pickRect", new { View = view, U0 = u0, V0 = v0, U1 = u1, V1 = v1 }, ct)
            .ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } reply })
            return Result<IReadOnlyList<ulong>>.Fail(result.Error ?? "The engine returned no result.");

        var ids = new List<ulong>();
        if (reply["ids"] is JArray array)
            foreach (var token in array)
                ids.Add(token.Value<ulong>());
        return Result<IReadOnlyList<ulong>>.Ok(ids);
    }

    /// <summary>
    /// Pushes the editor's current entity selection to the engine so every viewport highlights it
    /// (fire-and-forget notification). An empty list clears the highlight.
    /// </summary>
    public Task SetSelectionAsync(IReadOnlyList<ulong> ids) =>
        NotifyAsync("view.setSelection", new { Ids = ids });

    /// <summary>
    /// For each entity id, whether it is hidden behind other geometry from a view's camera — used by the
    /// billboard overlay to show an icon only when its entity is actually visible. Returns a list of flags
    /// aligned with <paramref name="ids"/>. Batched into one engine pass so all icons refresh in one call.
    /// </summary>
    public async Task<Result<IReadOnlyList<bool>>> QueryOcclusionAsync(
        string view, IReadOnlyList<ulong> ids, CancellationToken ct)
    {
        var result = await InvokeAsync<JToken>("view.queryOcclusion", new { View = view, Ids = ids }, ct)
            .ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } reply })
            return Result<IReadOnlyList<bool>>.Fail(result.Error ?? "The engine returned no result.");

        var occluded = new List<bool>();
        if (reply["occluded"] is JArray array)
            foreach (var token in array)
                occluded.Add(token.Value<bool>());
        return Result<IReadOnlyList<bool>>.Ok(occluded);
    }

    /// <summary>
    /// Projects a view's entities to its normalized screen space (top-left origin) for the billboard
    /// overlay. The editor polls this on its own cadence — the engine no longer pushes positions —
    /// and joins the results against the world snapshot to place name labels + icon stacks.
    /// </summary>
    public async Task<Result<IReadOnlyList<BillboardPosition>>> ProjectEntitiesAsync(
        string view, CancellationToken ct)
    {
        var result = await InvokeAsync<JToken>("view.projectEntities", new { View = view }, ct)
            .ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } reply })
            return Result<IReadOnlyList<BillboardPosition>>.Fail(
                result.Error ?? "The engine returned no result.");

        var positions = new List<BillboardPosition>();
        if (reply["items"] is JArray array)
            foreach (var token in array)
                positions.Add(new BillboardPosition(
                    token.Value<ulong>("id"),
                    token.Value<double>("u"),
                    token.Value<double>("v"),
                    token.Value<double>("depth")));
        return Result<IReadOnlyList<BillboardPosition>>.Ok(positions);
    }

    /// <summary>
    /// Executes a data-driven <see cref="RpcCall"/> against the engine (the transport behind data-driven tool
    /// commands; see <c>Widgets/Toolbar/ToolCommandRunner.cs</c>): a fire-and-forget notification when
    /// <see cref="RpcCall.Notify"/> is set, otherwise a bounded, guarded request.
    /// </summary>
    public async Task<Result> RunAsync(RpcCall call, CancellationToken ct)
    {
        if (call.Notify)
        {
            await NotifyAsync(call.Method, call.Params).ContinueOnAnyContext();
            return Result.Ok();
        }

        return await InvokeAsync(call.Method, call.Params, ct).ContinueOnAnyContext();
    }

    /// <summary>
    /// Invokes an engine RPC method expecting a typed reply, bounded by the default request timeout. The
    /// primitive the editor-side constructs (the world / entity / component / asset / settings facades) build
    /// their domain calls on.
    /// </summary>
    internal Task<Result<T>> InvokeAsync<T>(string method, object? args, CancellationToken ct) =>
        InvokeAsync<T>(method, args, DefaultRequestTimeout, ct);

    /// <summary>
    /// Invokes an engine RPC method expecting a typed reply, bounded by <paramref name="timeout"/>: a hung
    /// engine cannot park the call past it. On expiry returns a clear timeout failure rather than the
    /// transport's generic "operation canceled"; the caller's <paramref name="ct"/> still cancels early.
    /// </summary>
    internal async Task<Result<T>> InvokeAsync<T>(
        string method, object? args, TimeSpan timeout, CancellationToken ct)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(timeout);
        var result = await _rpc.InvokeAsync<T>(method, args, bounded.Token).ContinueOnAnyContext();
        return result.Success || !TimedOut(ct, bounded)
            ? result
            : Result<T>.Fail(TimeoutMessage(method, timeout));
    }

    /// <summary>Invokes an engine RPC method whose reply is ignored, bounded by the default request timeout.</summary>
    internal Task<Result> InvokeAsync(string method, object? args, CancellationToken ct) =>
        InvokeAsync(method, args, DefaultRequestTimeout, ct);

    /// <summary>Invokes an engine RPC method whose reply is ignored, bounded by <paramref name="timeout"/>.</summary>
    internal async Task<Result> InvokeAsync(
        string method, object? args, TimeSpan timeout, CancellationToken ct)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(timeout);
        var result = await _rpc.InvokeAsync(method, args, bounded.Token).ContinueOnAnyContext();
        return result.Success || !TimedOut(ct, bounded)
            ? result
            : Result.Fail(TimeoutMessage(method, timeout));
    }

    /// <summary>Sends a fire-and-forget engine notification (no reply); a no-op when not connected.</summary>
    internal Task NotifyAsync(string method, object? args) => _rpc.NotifyAsync(method, args);

    // The call expired on OUR deadline (not the caller's cancellation) when the linked source fired but the
    // caller's own token did not — that's a timeout, reported with a clear message instead of the transport's
    // generic "operation canceled".
    private static bool TimedOut(CancellationToken ct, CancellationTokenSource bounded) =>
        bounded.IsCancellationRequested && !ct.IsCancellationRequested;

    private static string TimeoutMessage(string method, TimeSpan timeout) =>
        $"The engine did not answer '{method}' within {timeout.TotalSeconds:F0}s.";

    // The engine's inbound notification handlers, wired before the connection starts listening. Re-raised as
    // typed events; each fires on the RPC listener thread.
    private void RegisterHandlers(RpcHandlers handlers)
    {
        handlers.On(
            "engine.log",
            (string level, string message) =>
                LogReceived?.Invoke(new LogEntry(LogLevels.Parse(level), message)));
        handlers.On(
            "view.surface",
            (string name, long sharedHandle, int width, int height, string format) =>
                SurfaceReceived?.Invoke(new ViewSurface(name, sharedHandle, width, height, format)));
        handlers.On("view.presented", (string name) => ViewPresented?.Invoke(name));
        handlers.On("input.mouseLock", (string mode) => MouseLockModeChanged?.Invoke(mode));
        handlers.On("view.transformEdited", (ulong[] ids) => TransformEdited?.Invoke());
    }
}
