using System.IO.MemoryMappedFiles;

namespace Toybox.Studio.Services;

/// <summary>One frame read from the engine's shared memory, top-down BGRA8888.</summary>
public sealed record ViewportFrame(int Width, int Height, int Stride, byte[] Data);

/// <summary>
/// Starts the engine's frame streaming on connect, then pumps frames out of the shared memory
/// region on a background task. The shared layout is a small header plus two slots guarded by
/// per-slot sequence counters (odd while the engine is writing).
/// </summary>
public sealed class EngineViewportService : IDisposable
{
    private const uint FrameMagic = 0x46584254; // "TBXF"
    private const int LatestSlotOffset = 12;
    private const int SlotsOffset = 16;
    private const int SlotSize = 24;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(15);

    private readonly EngineSessionService _session;
    private readonly byte[][] _buffers = [[], []];
    private int _nextBuffer;
    private CancellationTokenSource? _cts;

    public EngineViewportService(EngineSessionService session)
    {
        _session = session;
        session.StateChanged += OnSessionStateChanged;
    }

    /// <summary>Raised from a background thread for every new frame. Buffers are reused.</summary>
    public event Action<ViewportFrame>? FrameArrived;

    public void Dispose()
    {
        StopReading();
    }

    private void OnSessionStateChanged(EngineConnectionState state)
    {
        if (state == EngineConnectionState.Connected)
            _ = StartReadingAsync();
        else
            StopReading();
    }

    private async Task StartReadingAsync()
    {
        var client = _session.Client;
        if (client is not { IsConnected: true })
            return;

        try
        {
            var view = await client.StartViewAsync(CancellationToken.None).ConfigureAwait(false);
            StopReading();
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ReadLoopAsync(view.Name, _cts.Token));
        }
        catch (Exception)
        {
            // The engine has no rendering service or went away; the viewport just stays empty.
        }
    }

    private void StopReading()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task ReadLoopAsync(string name, CancellationToken ct)
    {
        using var mapping = TryOpenMapping(name);
        if (mapping is null)
            return;

        using var accessor = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        if (accessor.ReadUInt32(0) != FrameMagic)
            return;

        uint lastSequence = 0;
        while (!ct.IsCancellationRequested)
        {
            if (TryReadFrame(accessor, ref lastSequence, out var frame))
                FrameArrived?.Invoke(frame);

            try
            {
                await Task.Delay(PollInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private bool TryReadFrame(
        MemoryMappedViewAccessor accessor,
        ref uint lastSequence,
        out ViewportFrame frame)
    {
        frame = null!;
        var slotBase = SlotsOffset + (int)accessor.ReadUInt32(LatestSlotOffset) * SlotSize;
        var sequence = accessor.ReadUInt32(slotBase);
        if (sequence == 0 || sequence % 2 != 0 || sequence == lastSequence)
            return false;

        var width = (int)accessor.ReadUInt32(slotBase + 4);
        var height = (int)accessor.ReadUInt32(slotBase + 8);
        var stride = (int)accessor.ReadUInt32(slotBase + 12);
        var offset = (long)accessor.ReadUInt64(slotBase + 16);
        var byteCount = height * stride;
        if (width <= 0 || height <= 0 || byteCount <= 0)
            return false;

        var buffer = _buffers[_nextBuffer];
        if (buffer.Length < byteCount)
            _buffers[_nextBuffer] = buffer = new byte[byteCount];

        accessor.ReadArray(offset, buffer, 0, byteCount);

        // The engine bumped the sequence to odd if it started writing while we copied.
        if (accessor.ReadUInt32(slotBase) != sequence)
            return false;

        lastSequence = sequence;
        _nextBuffer = (_nextBuffer + 1) % _buffers.Length;
        frame = new ViewportFrame(width, height, stride, buffer);
        return true;
    }

    private static MemoryMappedFile? TryOpenMapping(string name)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                return MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
            }
            catch (FileNotFoundException)
            {
                Thread.Sleep(100);
            }
        }

        return null;
    }
}
