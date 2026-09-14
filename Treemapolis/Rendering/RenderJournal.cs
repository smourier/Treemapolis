namespace Treemapolis.Rendering;

// the last frames and the last things that happened around them, written out only when the device is lost.
// recording is a struct copy per frame, so it stays on in every build.
public sealed class RenderJournal
{
    private const int _frameCapacity = 120;
    private const int _noteCapacity = 64;

    private readonly FrameRecord[] _frames = new FrameRecord[_frameCapacity];
    private readonly Queue<string> _notes = new();
    private readonly Lock _lock = new();
    private readonly long _start = Stopwatch.GetTimestamp();
    private long _frameCount;

    public long FrameCount => Interlocked.Read(ref _frameCount);

    public void Record(FrameRecord record)
    {
        var frame = Interlocked.Increment(ref _frameCount) - 1;
        _frames[frame % _frameCapacity] = record with { Frame = frame };
    }

    public void Note(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var line = string.Create(CultureInfo.InvariantCulture, $"#{FrameCount} {Stopwatch.GetElapsedTime(_start).TotalSeconds:0.000}s thread {Environment.CurrentManagedThreadId}: {text}");
        lock (_lock)
        {
            _notes.Enqueue(line);
            while (_notes.Count > _noteCapacity)
            {
                _notes.Dequeue();
            }
        }
    }

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine("last events:");
        lock (_lock)
        {
            foreach (var note in _notes)
            {
                sb.Append("  ").AppendLine(note);
            }
        }

        sb.AppendLine("last frames, oldest first:");
        var count = FrameCount;
        for (var frame = Math.Max(0, count - _frameCapacity); frame < count; frame++)
        {
            sb.Append("  ").AppendLine(_frames[frame % _frameCapacity].ToString());
        }
        return sb.ToString();
    }
}
