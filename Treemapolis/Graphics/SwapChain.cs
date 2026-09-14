namespace Treemapolis.Graphics;

public sealed class SwapChain : InterlockedComObject<IDXGISwapChain3>
{
    private const uint _waitTimeout = 1000;
    private readonly GraphicsDevice _device;
    private readonly DescriptorHeap _heap;
    private readonly ulong[] _frameFences;
    private readonly DXGI_SWAP_CHAIN_FLAG _flags;
    private BackBuffer[] _buffers = [];

    // a composition swap chain has no window of its own, it is shown by a DirectComposition visual and can be transparent,
    // but DXGI allows no tearing on it, which is why the window only uses one when it is made of a material.
    public SwapChain(GraphicsDevice device, DescriptorHeap heap, HWND hwnd, uint bufferCount, DXGI_FORMAT format, DXGI_FORMAT renderTargetFormat, bool composition)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(heap);
        _device = device;
        _heap = heap;
        BufferCount = bufferCount;
        Format = format;
        RenderTargetFormat = renderTargetFormat;
        _frameFences = new ulong[bufferCount];

        IsComposition = composition;
        SupportsTearing = !composition && CheckTearingSupport(device);
        _flags = DXGI_SWAP_CHAIN_FLAG.DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT;
        if (SupportsTearing)
        {
            _flags |= DXGI_SWAP_CHAIN_FLAG.DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING;
        }

        Functions.GetClientRect(hwnd, out var rc);
        var desc = new DXGI_SWAP_CHAIN_DESC1
        {
            BufferCount = bufferCount,
            Format = format,
            BufferUsage = DXGI_USAGE.DXGI_USAGE_RENDER_TARGET_OUTPUT,
            SwapEffect = DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_DISCARD,
            Scaling = composition ? DXGI_SCALING.DXGI_SCALING_STRETCH : DXGI_SCALING.DXGI_SCALING_NONE,
            AlphaMode = composition ? DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_PREMULTIPLIED : DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_UNSPECIFIED,
            Width = (uint)Math.Max(1, rc.Width),
            Height = (uint)Math.Max(1, rc.Height),
            Flags = (uint)_flags,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
        };

        if (composition)
        {
            var queue = device.DirectQueue.ComObject.ToComInstanceNoAddRef();
            device.Factory.Object.CreateSwapChainForComposition(queue, desc, null, out var created).ThrowOnError();
            ExchangeDisposable(new ComObject<IDXGISwapChain3>((IDXGISwapChain3)created));
        }
        else
        {
            ExchangeDisposable(device.Factory.CreateSwapChainForHwnd<IDXGISwapChain3>(device.DirectQueue.ComObject, hwnd, desc));

            // Alt+Enter is handled by the window as a borderless fullscreen, which keeps the flip model and tearing.
            device.Factory.Object.MakeWindowAssociation(hwnd, DXGI_MWA_FLAGS.DXGI_MWA_NO_ALT_ENTER).ThrowOnError();
        }

        NativeObject.SetMaximumFrameLatency(1).ThrowOnError();
        WaitableObject = NativeObject.GetFrameLatencyWaitableObject();

        Width = desc.Width;
        Height = desc.Height;
        CreateBuffers();
    }

    public uint BufferCount { get; }
    public DXGI_FORMAT Format { get; }
    public DXGI_FORMAT RenderTargetFormat { get; }
    public bool SupportsTearing { get; }
    public bool IsComposition { get; }
    public HANDLE WaitableObject { get; }
    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public uint FrameIndex { get; private set; }
    public BackBuffer CurrentBuffer => _buffers[FrameIndex];

    public void WaitForFrame()
    {
        if (WaitableObject.Value != 0)
        {
            Functions.WaitForSingleObjectEx(WaitableObject, _waitTimeout, true);
        }
    }

    public void Resize(uint width, uint height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height)
            return;

        _device.DirectQueue.WaitForIdle();
        DisposeBuffers();
        NativeObject.ResizeBuffers(BufferCount, width, height, DXGI_FORMAT.DXGI_FORMAT_UNKNOWN, (uint)_flags).ThrowOnError();
        Width = width;
        Height = height;
        CreateBuffers();
    }

    public HRESULT Present(bool vsync)
    {
        var flags = !vsync && SupportsTearing ? DXGI_PRESENT.DXGI_PRESENT_ALLOW_TEARING : 0;
        return NativeObject.Present(vsync ? 1u : 0u, flags);
    }

    // a frame slot is reused only once the GPU is done with the frame that last used it.
    public void MoveToNextFrame()
    {
        var queue = _device.DirectQueue;
        _frameFences[FrameIndex] = queue.SignalAndIncrementFence();
        FrameIndex = NativeObject.GetCurrentBackBufferIndex();
        queue.WaitForFence(_frameFences[FrameIndex]);
    }

    private void CreateBuffers()
    {
        _buffers = new BackBuffer[BufferCount];
        for (uint i = 0; i < BufferCount; i++)
        {
            _buffers[i] = new BackBuffer(_device, _heap, ComObject.GetBuffer<ID3D12Resource>(i), RenderTargetFormat);
        }
        FrameIndex = NativeObject.GetCurrentBackBufferIndex();
    }

    private void DisposeBuffers()
    {
        foreach (var buffer in _buffers)
        {
            buffer.Dispose();
        }
        _buffers = [];
    }

    private static unsafe bool CheckTearingSupport(GraphicsDevice device)
    {
        if (device.Factory.Object is not IDXGIFactory5 factory5)
            return false;

        BOOL allow = false;
        return factory5.CheckFeatureSupport(DXGI_FEATURE.DXGI_FEATURE_PRESENT_ALLOW_TEARING, (nint)(&allow), (uint)sizeof(BOOL)).IsSuccess && allow;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeBuffers();
            if (WaitableObject.Value != 0)
            {
                Functions.CloseHandle(WaitableObject);
            }
        }
        base.Dispose(disposing);
    }
}
