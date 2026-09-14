namespace Treemapolis.Navigation;

// back and forward, the way a browser keeps them: going somewhere new forgets what was ahead.
public sealed class NavigationHistory
{
    private readonly Stack<NavigationLocation> _back = new();
    private readonly Stack<NavigationLocation> _forward = new();

    public int BackCount => _back.Count;
    public int ForwardCount => _forward.Count;

    public void Push(NavigationLocation current)
    {
        ArgumentNullException.ThrowIfNull(current);
        _back.Push(current);
        _forward.Clear();
    }

    public NavigationLocation? Back(NavigationLocation current) => Move(_back, _forward, current);
    public NavigationLocation? Forward(NavigationLocation current) => Move(_forward, _back, current);

    private static NavigationLocation? Move(Stack<NavigationLocation> from, Stack<NavigationLocation> to, NavigationLocation current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!from.TryPop(out var target))
            return null;

        to.Push(current);
        return target;
    }
}
