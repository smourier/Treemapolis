namespace Treemapolis.Chrome;

// what every drawn thing that takes input has in common, in client pixels.
public abstract class Control
{
    public D2D_RECT_F Bounds { get; set; }

    // whether it takes input at all just now, a menu that is not up takes none.
    public virtual bool IsInteractive => true;
    public virtual bool IsModal => false;

    // set while something inside is dragged, the window keeps the mouse until the button comes up.
    public virtual bool IsCapturing => false;

    public virtual bool Contains(float x, float y) => x >= Bounds.left && x < Bounds.right && y >= Bounds.top && y < Bounds.bottom;

    // each of these returns true when it dealt with the message.
    public virtual bool OnMouseMove(float x, float y) => false;
    public virtual bool OnMouseDown(float x, float y) => false;
    public virtual bool OnMouseUp() => false;
    public virtual bool OnWheel(float x, float y, int delta) => false;
    public virtual bool OnKeyDown(VIRTUAL_KEY key) => false;
    public virtual void OnMouseLeave()
    {
    }
}
