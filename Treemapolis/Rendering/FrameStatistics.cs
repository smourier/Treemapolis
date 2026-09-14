namespace Treemapolis.Rendering;

public sealed class FrameStatistics
{
    private const double _windowSeconds = 1;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private double _windowStart;
    private long _lastTicks;
    private int _windowFrames;
    private double _windowCpuMilliseconds;

    public long FrameCount { get; private set; }
    public double FramesPerSecond { get; private set; }
    public double FrameMilliseconds { get; private set; }
    public double CpuMilliseconds { get; private set; }
    public double Seconds => _clock.Elapsed.TotalSeconds;

    // the Stopwatch timestamp Seconds counts from.
    public long StartTimestamp => _startTimestamp;

#pragma warning disable CA1822 // Mark members as static
    public long BeginFrame() => Stopwatch.GetTimestamp();
#pragma warning restore CA1822 // Mark members as static

    public void EndFrame(long frameStart)
    {
        var now = Stopwatch.GetTimestamp();
        _windowCpuMilliseconds += Stopwatch.GetElapsedTime(frameStart, now).TotalMilliseconds;
        _windowFrames++;
        FrameCount++;

        if (_lastTicks != 0)
        {
            FrameMilliseconds = Stopwatch.GetElapsedTime(_lastTicks, now).TotalMilliseconds;
        }
        _lastTicks = now;

        var seconds = Seconds;
        if (seconds - _windowStart >= _windowSeconds)
        {
            FramesPerSecond = _windowFrames / (seconds - _windowStart);
            CpuMilliseconds = _windowCpuMilliseconds / _windowFrames;
            _windowStart = seconds;
            _windowFrames = 0;
            _windowCpuMilliseconds = 0;
        }
    }
}
