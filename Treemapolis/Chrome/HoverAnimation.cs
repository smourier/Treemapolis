namespace Treemapolis.Chrome;

// a hover that fades instead of snapping, advanced by the time the last frame took, so it costs nothing while nothing moves.
public struct HoverAnimation
{
    private const float _fadeInSeconds = 0.12f;
    private const float _fadeOutSeconds = 0.20f;

    private float _value;

    public readonly float Opacity => _value * _value * (3 - 2 * _value);

    // true while it is still moving, which is what asks for another frame.
    public bool Advance(bool hot, float elapsedSeconds)
    {
        var target = hot ? 1f : 0f;
        if (_value == target)
            return false;

        // leaving is slower than arriving, a row of buttons then feels settled rather than twitchy.
        var step = elapsedSeconds / (hot ? _fadeInSeconds : _fadeOutSeconds);
        _value = hot ? MathF.Min(target, _value + step) : MathF.Max(target, _value - step);
        return _value != target;
    }
}
