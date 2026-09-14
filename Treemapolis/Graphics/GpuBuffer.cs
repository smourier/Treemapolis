namespace Treemapolis.Graphics;

public sealed class GpuBuffer : Resource
{
    private readonly DescriptorHeap? _heap;

    public GpuBuffer(GraphicsDevice device, DescriptorHeap? heap, uint elementCount, uint stride, bool unorderedAccess, D3D12_RESOURCE_STATES initialState = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfZero(elementCount);
        ArgumentOutOfRangeException.ThrowIfZero(stride);
        ElementCount = elementCount;
        Stride = stride;
        Size = (ulong)elementCount * stride;
        var flags = unorderedAccess ? D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS : D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_NONE;
        SetResource(CreateBuffer(device, Size, D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_DEFAULT, flags, initialState), initialState);
        if (heap == null)
            return;

        _heap = heap;
        ShaderResourceView = heap.Allocate();
        var srv = new D3D12_SHADER_RESOURCE_VIEW_DESC
        {
            Format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
            ViewDimension = D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_BUFFER,
            Shader4ComponentMapping = Constants.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        srv.Anonymous.Buffer.NumElements = elementCount;
        srv.Anonymous.Buffer.StructureByteStride = stride;
        device.ComObject.CreateShaderResourceView(ComObject, srv, ShaderResourceView.Cpu);

        if (unorderedAccess)
        {
            UnorderedAccessView = heap.Allocate();
            var uav = new D3D12_UNORDERED_ACCESS_VIEW_DESC
            {
                Format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
                ViewDimension = D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_BUFFER,
            };
            uav.Anonymous.Buffer.NumElements = elementCount;
            uav.Anonymous.Buffer.StructureByteStride = stride;
            device.ComObject.CreateUnorderedAccessView(ComObject, null, uav, UnorderedAccessView.Cpu);
        }
    }

    public uint ElementCount { get; }
    public uint Stride { get; }
    public ulong Size { get; }
    public DescriptorHandle ShaderResourceView { get; }
    public DescriptorHandle UnorderedAccessView { get; }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _heap != null)
        {
            _heap.Free(ShaderResourceView);
            _heap.Free(UnorderedAccessView);
        }
        base.Dispose(disposing);
    }
}
