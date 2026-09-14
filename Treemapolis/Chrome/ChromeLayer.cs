namespace Treemapolis.Chrome;

// the 2D layer over the scene: a Direct2D swap chain in a DirectComposition visual that is topmost on the window.
// it is presented only when the chrome changes, never at the rate the scene renders, and its visual can carry the scene's own
// composition swap chain underneath it when the window is made of a material.
public sealed class ChromeLayer : IDisposable
{
    private const uint _bufferCount = 2;
    private const DXGI_FORMAT _format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM;
    private const uint _bytesPerPixel = 4;

    private readonly IComObject<ID3D11Device> _device;
    private readonly IComObject<ID3D11DeviceContext> _deviceContext;
    private readonly IComObject<ID2D1Device> _d2dDevice;
    private readonly IComObject<IDCompositionDevice> _composition;
    private readonly ComObject<IDCompositionTarget> _target;
    private readonly ComObject<IDCompositionVisual> _root;
    private readonly ComObject<IDCompositionVisual> _chromeVisual;
    private readonly IComObject<IDXGISwapChain1> _swapChain;
    private ComObject<IDCompositionVisual>? _sceneVisual;
    private nint _sceneSwapChain;
    private IComObject<ID2D1Bitmap1>? _targetBitmap;

    public ChromeLayer(HWND hwnd, bool debug)
    {
        var flags = D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT;
        if (debug && DXGIFunctions.IsDebugLayerAvailable)
        {
            flags |= D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_DEBUG;
        }

        _device = CreateDevice(flags, out _deviceContext);
        using var dxgiDevice = _device.As<IDXGIDevice1>()!;
        _d2dDevice = D2D1Functions.D2D1CreateDevice(dxgiDevice.As<IDXGIDevice>()!);
        DeviceContext = _d2dDevice.CreateDeviceContext();

        // pixels, not DIPs, every size in the chrome is already multiplied by the monitor scale.
        DeviceContext.Object.SetDpi(Constants.USER_DEFAULT_SCREEN_DPI, Constants.USER_DEFAULT_SCREEN_DPI);
        DeviceContext.Object.SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE.D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
        Factory = DWriteFunctions.DWriteCreateFactory();

        Functions.GetClientRect(hwnd, out var client);
        Width = (uint)Math.Max(1, client.Width);
        Height = (uint)Math.Max(1, client.Height);
        var desc = new DXGI_SWAP_CHAIN_DESC1
        {
            Width = Width,
            Height = Height,
            Format = _format,
            BufferUsage = DXGI_USAGE.DXGI_USAGE_RENDER_TARGET_OUTPUT,
            BufferCount = _bufferCount,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            SwapEffect = DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_DISCARD,
            Scaling = DXGI_SCALING.DXGI_SCALING_STRETCH,
            AlphaMode = DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_PREMULTIPLIED,
        };

        using var adapter = dxgiDevice.GetAdapter();
        using var factory = adapter.GetFactory2()!;
        _swapChain = factory.CreateSwapChainForComposition<IDXGISwapChain1>(dxgiDevice, desc);
        CreateTargetBitmap();

        var iid = typeof(IDCompositionDevice).GUID;
        Functions.DCompositionCreateDevice(dxgiDevice.As<IDXGIDevice>()!.Object, iid, out var unknown).ThrowOnError();
        _composition = DirectN.Extensions.Com.ComObject.FromPointer<IDCompositionDevice>(unknown)!;
        _composition.Object.CreateTargetForHwnd(hwnd, true, out var target).ThrowOnError();
        _target = new ComObject<IDCompositionTarget>(target);
        _composition.Object.CreateVisual(out var root).ThrowOnError();
        _root = new ComObject<IDCompositionVisual>(root);
        _composition.Object.CreateVisual(out var chrome).ThrowOnError();
        _chromeVisual = new ComObject<IDCompositionVisual>(chrome);
        _chromeVisual.Object.SetContent(_swapChain.ToComInstanceNoAddRef()).ThrowOnError();
        _root.Object.AddVisual(_chromeVisual.Object, true, null).ThrowOnError();
        _target.Object.SetRoot(_root.Object).ThrowOnError();
        _composition.Object.Commit().ThrowOnError();
    }

    public IComObject<ID2D1DeviceContext> DeviceContext { get; }
    public IComObject<IDWriteFactory> Factory { get; }
    public uint Width { get; private set; }
    public uint Height { get; private set; }

    // the scene's composition swap chain goes behind the chrome, null takes it away when the scene presents to the window again.
    public void SetScene(nint swapChain)
    {
        if (_sceneVisual != null)
        {
            _root.Object.RemoveVisual(_sceneVisual.Object).ThrowOnError();
            _sceneVisual.Dispose();
            _sceneVisual = null;
        }

        _sceneSwapChain = swapChain;
        if (swapChain != 0)
        {
            _composition.Object.CreateVisual(out var visual).ThrowOnError();
            _sceneVisual = new ComObject<IDCompositionVisual>(visual);
            _sceneVisual.Object.SetContent(swapChain).ThrowOnError();
            _root.Object.AddVisual(_sceneVisual.Object, false, _chromeVisual.Object).ThrowOnError();
        }
        _composition.Object.Commit().ThrowOnError();
    }

    public void Resize(uint width, uint height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height)
            return;

        DeviceContext.Object.SetTarget(null);
        _targetBitmap?.Dispose();
        _targetBitmap = null;
        _swapChain.Object.ResizeBuffers(_bufferCount, width, height, DXGI_FORMAT.DXGI_FORMAT_UNKNOWN, 0).ThrowOnError();
        Width = width;
        Height = height;
        CreateTargetBitmap();
    }

    // a visual keeps showing the buffers its swap chain had when it was set, resized buffers are only seen once the content is set again.
    public void RefreshContent()
    {
        _chromeVisual.Object.SetContent(_swapChain.ToComInstanceNoAddRef()).ThrowOnError();
        _sceneVisual?.Object.SetContent(_sceneSwapChain).ThrowOnError();
        _composition.Object.Commit().ThrowOnError();
    }

    // draws a whole chrome frame on a transparent buffer and presents it, and when asked, reads it back first as premultiplied BGRA rows.
    public byte[]? Draw(Action<IComObject<ID2D1DeviceContext>> draw, bool readBack)
    {
        ArgumentNullException.ThrowIfNull(draw);
        var context = DeviceContext.Object;
        context.BeginDraw();
        context.Clear(0);
        draw(DeviceContext);
        var hr = context.EndDraw(0, 0);
        if (hr.IsError)
        {
            Application.TraceError($"the chrome could not be drawn: {hr}");
            return null;
        }

        var pixels = readBack ? ReadBack() : null;
        _swapChain.Object.Present(0, 0);
        return pixels;
    }

    private unsafe byte[] ReadBack()
    {
        using var buffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        buffer.Object.GetDesc(out var desc);
        desc.Usage = D3D11_USAGE.D3D11_USAGE_STAGING;
        desc.BindFlags = 0;
        desc.CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
        desc.MiscFlags = 0;
        nint texture;
        _device.Object.CreateTexture2D(desc, 0, (nint)(&texture)).ThrowOnError();
        using var staging = DirectN.Extensions.Com.ComObject.FromPointer<ID3D11Texture2D>(texture)!;
        _deviceContext.Object.CopyResource(staging.Object, buffer.Object);

        var pixels = new byte[Width * Height * _bytesPerPixel];
        D3D11_MAPPED_SUBRESOURCE mapped;
        _deviceContext.Object.Map(staging.Object, 0, D3D11_MAP.D3D11_MAP_READ, 0, (nint)(&mapped)).ThrowOnError();
        for (var y = 0; y < Height; y++)
        {
            new ReadOnlySpan<byte>((byte*)mapped.pData + y * mapped.RowPitch, (int)(Width * _bytesPerPixel)).CopyTo(pixels.AsSpan((int)(y * Width * _bytesPerPixel)));
        }
        _deviceContext.Object.Unmap(staging.Object, 0);
        return pixels;
    }

    private void CreateTargetBitmap()
    {
        using var surface = _swapChain.GetBuffer<IDXGISurface>(0);
        var properties = new D2D1_BITMAP_PROPERTIES1
        {
            bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_CANNOT_DRAW,
            pixelFormat = new D2D1_PIXEL_FORMAT { format = _format, alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED },
            dpiX = Constants.USER_DEFAULT_SCREEN_DPI,
            dpiY = Constants.USER_DEFAULT_SCREEN_DPI,
        };
        _targetBitmap = DeviceContext.CreateBitmapFromDxgiSurface(surface, properties);
        DeviceContext.Object.SetTarget(_targetBitmap.Object);
    }

    // hardware first and WARP after, proved by creating the device rather than by asking whether it ought to work.
    private static IComObject<ID3D11Device> CreateDevice(D3D11_CREATE_DEVICE_FLAG flags, out IComObject<ID3D11DeviceContext> context)
    {
        try
        {
            return D3D11Functions.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, flags, out context);
        }
        catch (Exception ex)
        {
            Application.TraceWarning($"no hardware device for the chrome ({ex.Message}), WARP is used.");
            return D3D11Functions.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP, flags & ~D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_DEBUG, out context);
        }
    }

    public void Dispose()
    {
        DeviceContext.Object.SetTarget(null);
        _targetBitmap?.Dispose();
        _sceneVisual?.Dispose();
        _chromeVisual.Dispose();
        _root.Dispose();
        _target.Dispose();
        _composition.Dispose();
        _swapChain.Dispose();
        Factory.Dispose();
        DeviceContext.Dispose();
        _d2dDevice.Dispose();
        _deviceContext.Dispose();
        _device.Dispose();
    }
}
