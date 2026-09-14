namespace Treemapolis.Graphics;

public sealed class PipelineState : InterlockedComObject<ID3D12PipelineState>
{
    private PipelineState(IComObject<ID3D12PipelineState> state, RootSignature rootSignature)
        : base(state)
    {
        RootSignature = rootSignature;
    }

    public RootSignature RootSignature { get; }

    public static PipelineState CreateGraphics(GraphicsDevice device, GraphicsPipelineDescription description)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(description);
        return new PipelineState(device.ComObject.CreateGraphicsPipelineState(description.ToDesc()), description.RootSignature);
    }

    public static PipelineState CreateCompute(GraphicsDevice device, RootSignature rootSignature, Shader shader)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(rootSignature);
        ArgumentNullException.ThrowIfNull(shader);
        var desc = new D3D12_COMPUTE_PIPELINE_STATE_DESC
        {
            pRootSignature = rootSignature.NativePointer,
            CS = shader.Bytecode,
        };
        return new PipelineState(device.ComObject.CreateComputePipelineState(desc), rootSignature);
    }
}
