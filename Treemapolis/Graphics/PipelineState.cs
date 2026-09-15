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

        // said before the call, a driver that crashes while it compiles a shader takes the process down with no exception to report.
        var desc = description.ToDesc();
        Application.TraceInfo(string.Create(CultureInfo.InvariantCulture, $"creating the graphics pipeline {description.VertexShader} ({description.VertexShader.Length} bytes) {description.PixelShader} ({description.PixelShader?.Length ?? 0} bytes), targets {string.Join(" ", description.RenderTargetFormats)}, depth {description.DepthStencilFormat}, {description.SampleCount} samples, cull {description.CullMode}, depth test {description.DepthTest} write {description.DepthWrite} {description.DepthFunction}, blend {description.AlphaBlend} lighten {description.Lighten}, bias {description.DepthBias} {description.SlopeScaledDepthBias}"));
        device.FlushMessages();

        var state = device.ComObject.CreateGraphicsPipelineState(desc);
        device.FlushMessages();
        Application.TraceInfo($"created the graphics pipeline {description.VertexShader} {description.PixelShader}");
        return new PipelineState(state, description.RootSignature);
    }

    public static PipelineState CreateCompute(GraphicsDevice device, RootSignature rootSignature, Shader shader)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(rootSignature);
        ArgumentNullException.ThrowIfNull(shader);
        Application.TraceInfo($"creating the compute pipeline {shader} ({shader.Length} bytes)");
        device.FlushMessages();
        var desc = new D3D12_COMPUTE_PIPELINE_STATE_DESC
        {
            pRootSignature = rootSignature.NativePointer,
            CS = shader.Bytecode,
        };
        return new PipelineState(device.ComObject.CreateComputePipelineState(desc), rootSignature);
    }
}
