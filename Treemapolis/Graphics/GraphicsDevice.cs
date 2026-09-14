namespace Treemapolis.Graphics;

public sealed class GraphicsDevice : InterlockedComObject<ID3D12Device>
{
    private static readonly D3D12MessageFunc _messageCallback = OnMessage;
    private GCHandle _handle;
    private uint _messageCallbackCookie;
    private int _errorCount;
    private int _warningCount;
    private const int _maxRecentMessages = 32;
    private readonly ConcurrentQueue<string> _recentMessages = new();

    public GraphicsDevice(bool useWarp, bool debug, bool gpuValidation = false)
    {
        DXGI_CREATE_FACTORY_FLAGS factoryFlags = 0;

        // breadcrumbs and page faults are recorded for the device removal report in every build, a published one included,
        // they must be enabled before the device exists.
        using (var dred = D3D12Functions.D3D12GetDebugInterface<ID3D12DeviceRemovedExtendedDataSettings>())
        {
            if (dred != null)
            {
                dred.Object.SetAutoBreadcrumbsEnablement(D3D12_DRED_ENABLEMENT.D3D12_DRED_ENABLEMENT_FORCED_ON);
                dred.Object.SetPageFaultEnablement(D3D12_DRED_ENABLEMENT.D3D12_DRED_ENABLEMENT_FORCED_ON);
                if (dred.Object is ID3D12DeviceRemovedExtendedDataSettings1 dred1)
                {
                    dred1.SetBreadcrumbContextEnablement(D3D12_DRED_ENABLEMENT.D3D12_DRED_ENABLEMENT_FORCED_ON);
                }
            }
        }

        if (debug || gpuValidation)
        {
            using var debugController = D3D12Functions.D3D12GetDebugInterface<ID3D12Debug>();
            if (debugController != null)
            {
                debugController.Object.EnableDebugLayer();
                if (gpuValidation && debugController.Object is ID3D12Debug1 debug1)
                {
                    debug1.SetEnableGPUBasedValidation(true);
                    Application.TraceWarning("GPU based validation is on, frames are much slower.");
                }
                IsDebug = true;
                if (DXGIFunctions.IsDebugLayerAvailable)
                {
                    factoryFlags |= DXGI_CREATE_FACTORY_FLAGS.DXGI_CREATE_FACTORY_DEBUG;
                }
            }
        }

        Factory = DXGIFunctions.CreateDXGIFactory2<IDXGIFactory4>(factoryFlags);
        var adapter = useWarp ? null : Factory.Object.GetHardwareAdapter(DXGI_GPU_PREFERENCE.DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_0);
        if (adapter == null)
        {
            Factory.Object.EnumWarpAdapter(typeof(IDXGIAdapter1).GUID, out var warp).ThrowOnError();
            adapter = DirectN.Extensions.Com.ComObject.FromPointer<IDXGIAdapter1>(warp)!;
        }
        Adapter = adapter;

        ExchangeDisposable(D3D12Functions.D3D12CreateDevice<ID3D12Device>(Adapter.Object, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_0));
        Adapter.Object.GetDesc1(out var desc).ThrowOnError();
        AdapterName = desc.Description.ToString();
        IsWarp = ((DXGI_ADAPTER_FLAG)desc.Flags).HasFlag(DXGI_ADAPTER_FLAG.DXGI_ADAPTER_FLAG_SOFTWARE);
        Features = new FeatureSupport(this);

        if (NativeObject is ID3D12InfoQueue1 infoQueue)
        {
            _handle = GCHandle.Alloc(this, GCHandleType.Weak);
            infoQueue.RegisterMessageCallback(_messageCallback, D3D12_MESSAGE_CALLBACK_FLAGS.D3D12_MESSAGE_CALLBACK_FLAG_NONE, GCHandle.ToIntPtr(_handle), ref _messageCallbackCookie);
        }

        DirectQueue = new CommandQueue(this, D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT);
        CopyQueue = new CommandQueue(this, D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_COPY);
        ComputeQueue = new CommandQueue(this, D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_COMPUTE);
    }

    public IComObject<IDXGIFactory4> Factory { get; }
    public IComObject<IDXGIAdapter1> Adapter { get; }
    public string AdapterName { get; }
    public bool IsWarp { get; }
    public bool IsDebug { get; }
    public FeatureSupport Features { get; }
    public CommandQueue DirectQueue { get; }
    public CommandQueue CopyQueue { get; }
    public CommandQueue ComputeQueue { get; }
    public int ErrorCount => _errorCount;
    public int WarningCount => _warningCount;
    public IEnumerable<string> RecentMessages => _recentMessages;

    public HRESULT RemovedReason => NativeObject.GetDeviceRemovedReason();
    public bool IsRemoved => RemovedReason.IsError;

    // the reason, and when breadcrumbs were on, the last operation each command list completed before the device was lost.
    public unsafe string DescribeRemoval()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"reason {RemovedReason}");
        if (NativeObject is not ID3D12DeviceRemovedExtendedData dred)
            return sb.ToString();

        if (dred.GetAutoBreadcrumbsOutput(out var breadcrumbs).IsSuccess)
        {
            for (var node = (D3D12_AUTO_BREADCRUMB_NODE*)breadcrumbs.pHeadAutoBreadcrumbNode; node != null; node = (D3D12_AUTO_BREADCRUMB_NODE*)node->pNext)
            {
                var completed = node->pLastBreadcrumbValue == 0 ? 0 : *(uint*)node->pLastBreadcrumbValue;
                var history = (D3D12_AUTO_BREADCRUMB_OP*)node->pCommandHistory;
                if (completed >= node->BreadcrumbCount)
                    continue;

                sb.AppendLine();
                sb.Append(CultureInfo.InvariantCulture, $"list 0x{node->pCommandList:X} completed {completed} of {node->BreadcrumbCount}");
                var first = (int)Math.Max(0, completed - 3);
                var last = (int)Math.Min(node->BreadcrumbCount - 1, completed + 3);
                for (var i = first; i <= last; i++)
                {
                    sb.AppendLine();
                    sb.Append(CultureInfo.InvariantCulture, $"  {(i == completed ? '>' : ' ')} {i} {history[i]}");
                }
            }
        }

        if (dred.GetPageFaultAllocationOutput(out var pageFault).IsSuccess && pageFault.PageFaultVA != 0)
        {
            sb.AppendLine();
            sb.Append(CultureInfo.InvariantCulture, $"page fault at 0x{pageFault.PageFaultVA:X}");
        }
        return sb.ToString();
    }

    public DXGI_QUERY_VIDEO_MEMORY_INFO QueryLocalVideoMemory() => QueryVideoMemory(DXGI_MEMORY_SEGMENT_GROUP.DXGI_MEMORY_SEGMENT_GROUP_LOCAL);

    public DXGI_QUERY_VIDEO_MEMORY_INFO QueryVideoMemory(DXGI_MEMORY_SEGMENT_GROUP group)
    {
        ((IDXGIAdapter3)Adapter.Object).QueryVideoMemoryInfo(0, group, out var info);
        return info;
    }

    public void WaitForIdle()
    {
        DirectQueue.WaitForIdle();
        ComputeQueue.WaitForIdle();
        CopyQueue.WaitForIdle();
    }

    private static void OnMessage(D3D12_MESSAGE_CATEGORY category, D3D12_MESSAGE_SEVERITY severity, D3D12_MESSAGE_ID id, PSTR description, nint context)
    {
        if (GCHandle.FromIntPtr(context).Target is not GraphicsDevice device)
            return;

        switch (severity)
        {
            case D3D12_MESSAGE_SEVERITY.D3D12_MESSAGE_SEVERITY_CORRUPTION:
            case D3D12_MESSAGE_SEVERITY.D3D12_MESSAGE_SEVERITY_ERROR:
                Interlocked.Increment(ref device._errorCount);
                device.Remember(description.ToString());
                Application.TraceError($"{description} [#{(int)id} {id}]");
                break;

            case D3D12_MESSAGE_SEVERITY.D3D12_MESSAGE_SEVERITY_WARNING:
                Interlocked.Increment(ref device._warningCount);
                device.Remember(description.ToString());
                Application.TraceWarning($"{description} [#{(int)id} {id}]");
                break;
        }
    }

    private void Remember(string? message)
    {
        _recentMessages.Enqueue(message ?? string.Empty);
        while (_recentMessages.Count > _maxRecentMessages && _recentMessages.TryDequeue(out _))
        {
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DirectQueue.Dispose();
            CopyQueue.Dispose();
            ComputeQueue.Dispose();
            if (_messageCallbackCookie != 0 && NativeObject is ID3D12InfoQueue1 infoQueue)
            {
                infoQueue.UnregisterMessageCallback(_messageCallbackCookie);
                _messageCallbackCookie = 0;
            }

            if (_handle.IsAllocated)
            {
                _handle.Free();
            }
        }

        base.Dispose(disposing);
        if (disposing)
        {
            Adapter.Dispose();
            Factory.Dispose();
        }
    }
}
