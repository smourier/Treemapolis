namespace Treemapolis.Graphics;

public sealed class GraphicsPipelineDescription
{
    public required RootSignature RootSignature { get; init; }
    public required Shader VertexShader { get; init; }
    public Shader? PixelShader { get; init; }
    public IReadOnlyList<DXGI_FORMAT> RenderTargetFormats { get; init; } = [];
    public DXGI_FORMAT DepthStencilFormat { get; init; }
    public D3D12_CULL_MODE CullMode { get; init; } = D3D12_CULL_MODE.D3D12_CULL_MODE_BACK;
    public bool DepthTest { get; init; } = true;
    public bool DepthWrite { get; init; } = true;

    // the scene renders with reversed depth, near is 1 and far is 0, which keeps precision over a landscape that spans kilometers.
    public D3D12_COMPARISON_FUNC DepthFunction { get; init; } = D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_GREATER;
    public bool AlphaBlend { get; init; }

    // the brighter of the new and the existing color is kept on the color target, the other targets are left as they are.
    public bool Lighten { get; init; }
    public int DepthBias { get; init; }
    public float SlopeScaledDepthBias { get; init; }
    public uint SampleCount { get; init; } = 1;
    public D3D12_PRIMITIVE_TOPOLOGY_TYPE Topology { get; init; } = D3D12_PRIMITIVE_TOPOLOGY_TYPE.D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;

    // every field holds a valid value, the defaults of CD3DX12, even those of states that are off,
    // a software renderer may read them anyway and a zero is not a valid stencil operation, comparison or blend factor.
    public D3D12_GRAPHICS_PIPELINE_STATE_DESC ToDesc()
    {
        var stencil = new D3D12_DEPTH_STENCILOP_DESC
        {
            StencilFailOp = D3D12_STENCIL_OP.D3D12_STENCIL_OP_KEEP,
            StencilDepthFailOp = D3D12_STENCIL_OP.D3D12_STENCIL_OP_KEEP,
            StencilPassOp = D3D12_STENCIL_OP.D3D12_STENCIL_OP_KEEP,
            StencilFunc = D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_ALWAYS,
        };
        var desc = new D3D12_GRAPHICS_PIPELINE_STATE_DESC
        {
            pRootSignature = RootSignature.NativePointer,
            VS = VertexShader.Bytecode,
            SampleMask = uint.MaxValue,
            PrimitiveTopologyType = Topology,
            DSVFormat = DepthStencilFormat,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = SampleCount },
            NumRenderTargets = (uint)RenderTargetFormats.Count,
            RasterizerState = new D3D12_RASTERIZER_DESC
            {
                FillMode = D3D12_FILL_MODE.D3D12_FILL_MODE_SOLID,
                CullMode = CullMode,
                FrontCounterClockwise = true,
                DepthClipEnable = true,
                DepthBias = DepthBias,
                SlopeScaledDepthBias = SlopeScaledDepthBias,
                ConservativeRaster = D3D12_CONSERVATIVE_RASTERIZATION_MODE.D3D12_CONSERVATIVE_RASTERIZATION_MODE_OFF,
            },
            DepthStencilState = new D3D12_DEPTH_STENCIL_DESC
            {
                DepthEnable = DepthTest,
                DepthWriteMask = DepthWrite ? D3D12_DEPTH_WRITE_MASK.D3D12_DEPTH_WRITE_MASK_ALL : D3D12_DEPTH_WRITE_MASK.D3D12_DEPTH_WRITE_MASK_ZERO,
                DepthFunc = DepthFunction,
                StencilReadMask = byte.MaxValue,
                StencilWriteMask = byte.MaxValue,
                FrontFace = stencil,
                BackFace = stencil,
            },
        };

        if (PixelShader != null)
        {
            desc.PS = PixelShader.Bytecode;
        }

        // only the color target blends, the entry target next to it is an integer format that cannot.
        desc.BlendState.IndependentBlendEnable = (AlphaBlend || Lighten) && RenderTargetFormats.Count > 1;
        for (var i = 0; i < Constants.D3D12_SIMULTANEOUS_RENDER_TARGET_COUNT; i++)
        {
            if (i < RenderTargetFormats.Count)
            {
                desc.RTVFormats[i] = RenderTargetFormats[i];
            }

            var blend = new D3D12_RENDER_TARGET_BLEND_DESC
            {
                SrcBlend = D3D12_BLEND.D3D12_BLEND_ONE,
                DestBlend = D3D12_BLEND.D3D12_BLEND_ZERO,
                BlendOp = D3D12_BLEND_OP.D3D12_BLEND_OP_ADD,
                SrcBlendAlpha = D3D12_BLEND.D3D12_BLEND_ONE,
                DestBlendAlpha = D3D12_BLEND.D3D12_BLEND_ZERO,
                BlendOpAlpha = D3D12_BLEND_OP.D3D12_BLEND_OP_ADD,
                LogicOp = D3D12_LOGIC_OP.D3D12_LOGIC_OP_NOOP,
                RenderTargetWriteMask = (byte)D3D12_COLOR_WRITE_ENABLE.D3D12_COLOR_WRITE_ENABLE_ALL,
            };

            // the other targets keep the default blend and are only masked, the older WARP crashes on a max blend given to an integer target.
            var used = i < RenderTargetFormats.Count;
            if (used && Lighten && i == 0)
            {
                blend.BlendEnable = true;
                blend.DestBlend = D3D12_BLEND.D3D12_BLEND_ONE;
                blend.DestBlendAlpha = D3D12_BLEND.D3D12_BLEND_ONE;
                blend.BlendOp = D3D12_BLEND_OP.D3D12_BLEND_OP_MAX;
                blend.BlendOpAlpha = D3D12_BLEND_OP.D3D12_BLEND_OP_MAX;
            }
            else if (used && Lighten)
            {
                blend.RenderTargetWriteMask = 0;
            }
            else if (used && AlphaBlend && i == 0)
            {
                blend.BlendEnable = true;
                blend.DestBlend = D3D12_BLEND.D3D12_BLEND_INV_SRC_ALPHA;
                blend.DestBlendAlpha = D3D12_BLEND.D3D12_BLEND_INV_SRC_ALPHA;
            }
            desc.BlendState.RenderTarget[i] = blend;
        }
        return desc;
    }
}
