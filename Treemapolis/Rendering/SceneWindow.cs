namespace Treemapolis.Rendering;

// the window the tearing swap chain presents to, a child covering the client area of the main window.
// once a window has presented through a swap chain of its own, Windows keeps that last frame and draws no backdrop under it,
// so the main window only ever holds composition visuals and this child is destroyed when the window is made of a material.
public sealed class SceneWindow : Window
{
    private const int _hitTransparent = -1;

    public SceneWindow(HWND parent, RECT client)
        : base(null, WINDOW_STYLE.WS_CHILD | WINDOW_STYLE.WS_VISIBLE | WINDOW_STYLE.WS_CLIPSIBLINGS, WINDOW_EX_STYLE.WS_EX_NOREDIRECTIONBITMAP, client, parent)
    {
        IsBackground = true;
    }

    // every mouse message belongs to the main window, which does the picking and hosts the chrome.
    protected override LRESULT? WindowProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        if (msg == MessageDecoder.WM_NCHITTEST)
            return new LRESULT { Value = _hitTransparent };

        // the swap chain covers it entirely, there is nothing to paint or erase.
        if (msg is MessageDecoder.WM_PAINT or MessageDecoder.WM_ERASEBKGND)
        {
            Functions.ValidateRect(hwnd, 0);
            return new LRESULT { Value = 1 };
        }
        return base.WindowProc(hwnd, msg, wParam, lParam);
    }
}
