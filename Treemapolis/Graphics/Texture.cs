namespace Treemapolis.Graphics;

public sealed class Texture : Resource
{
    private readonly DescriptorHeaps? _heaps;

    public Texture(GraphicsDevice device, DescriptorHeaps heaps, TextureDescription description)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(heaps);
        ArgumentNullException.ThrowIfNull(description);
        _heaps = heaps;
        Description = description;

        var flags = D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_NONE;
        if (description.RenderTarget)
        {
            flags |= D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;
        }

        if (description.DepthStencil)
        {
            flags |= D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;
            if (!description.ShaderResource)
            {
                flags |= D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_DENY_SHADER_RESOURCE;
            }
        }

        if (description.UnorderedAccess)
        {
            flags |= D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
        }

        var desc = new D3D12_RESOURCE_DESC
        {
            Dimension = D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_TEXTURE2D,
            Width = description.Width,
            Height = description.Height,
            DepthOrArraySize = description.ArraySize,
            MipLevels = description.MipLevels,
            Format = description.ResourceFormat,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = description.SampleCount },
            Flags = flags,
        };

        D3D12_CLEAR_VALUE? clear = null;
        if (description.RenderTarget)
        {
            var value = new D3D12_CLEAR_VALUE { Format = description.RenderTargetFormat };
            value.Anonymous.Color[0] = description.ClearColor.X;
            value.Anonymous.Color[1] = description.ClearColor.Y;
            value.Anonymous.Color[2] = description.ClearColor.Z;
            value.Anonymous.Color[3] = description.ClearColor.W;
            clear = value;
        }
        else if (description.DepthStencil)
        {
            var value = new D3D12_CLEAR_VALUE { Format = description.DepthStencilFormat };
            value.Anonymous.DepthStencil.Depth = description.ClearDepth;
            clear = value;
        }

        var props = new D3D12_HEAP_PROPERTIES { Type = D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_DEFAULT };
        SetResource(device.ComObject.CreateCommittedResource(props, D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE, desc, description.InitialState, clear), description.InitialState);

        if (description.RenderTarget)
        {
            RenderTargetView = heaps.RenderTargets.Allocate();
            var rtv = new D3D12_RENDER_TARGET_VIEW_DESC
            {
                Format = description.RenderTargetFormat,
                ViewDimension = description.IsMultisampled ? D3D12_RTV_DIMENSION.D3D12_RTV_DIMENSION_TEXTURE2DMS : D3D12_RTV_DIMENSION.D3D12_RTV_DIMENSION_TEXTURE2D,
            };
            device.ComObject.CreateRenderTargetView(ComObject, rtv, RenderTargetView.Cpu);
        }

        if (description.DepthStencil)
        {
            DepthStencilView = heaps.DepthStencils.Allocate();
            var dsv = new D3D12_DEPTH_STENCIL_VIEW_DESC
            {
                Format = description.DepthStencilFormat,
                ViewDimension = description.IsMultisampled ? D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2DMS : D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2D,
            };
            device.ComObject.CreateDepthStencilView(ComObject, dsv, DepthStencilView.Cpu);
        }

        if (description.ShaderResource)
        {
            ShaderResourceView = heaps.ShaderResources.Allocate();
            var srv = new D3D12_SHADER_RESOURCE_VIEW_DESC
            {
                Format = description.ShaderResourceFormat,
                ViewDimension = description.IsMultisampled ? D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2DMS
                    : description.ArraySize > 1 ? D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2DARRAY : D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D,
                Shader4ComponentMapping = Constants.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
            };
            if (description.ArraySize > 1)
            {
                srv.Anonymous.Texture2DArray.MipLevels = description.MipLevels;
                srv.Anonymous.Texture2DArray.ArraySize = description.ArraySize;
            }
            else if (!description.IsMultisampled)
            {
                srv.Anonymous.Texture2D.MipLevels = description.MipLevels;
            }
            device.ComObject.CreateShaderResourceView(ComObject, srv, ShaderResourceView.Cpu);
        }

        if (description.UnorderedAccess)
        {
            var views = new DescriptorHandle[description.MipLevels];
            for (uint mip = 0; mip < description.MipLevels; mip++)
            {
                views[mip] = heaps.ShaderResources.Allocate();
                var uav = new D3D12_UNORDERED_ACCESS_VIEW_DESC { Format = description.ShaderResourceFormat, ViewDimension = D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2D };
                uav.Anonymous.Texture2D.MipSlice = mip;
                device.ComObject.CreateUnorderedAccessView(ComObject, null, uav, views[mip].Cpu);
            }
            UnorderedAccessViews = views;
        }
    }

    public TextureDescription Description { get; }
    public uint Width => Description.Width;
    public uint Height => Description.Height;
    public DescriptorHandle RenderTargetView { get; }
    public DescriptorHandle DepthStencilView { get; }
    public DescriptorHandle ShaderResourceView { get; }
    public IReadOnlyList<DescriptorHandle> UnorderedAccessViews { get; } = [];

    protected override void Dispose(bool disposing)
    {
        if (disposing && _heaps != null && !IsDisposed)
        {
            _heaps.RenderTargets.Free(RenderTargetView);
            _heaps.DepthStencils.Free(DepthStencilView);
            _heaps.ShaderResources.Free(ShaderResourceView);
            foreach (var view in UnorderedAccessViews)
            {
                _heaps.ShaderResources.Free(view);
            }
        }
        base.Dispose(disposing);
    }
}
