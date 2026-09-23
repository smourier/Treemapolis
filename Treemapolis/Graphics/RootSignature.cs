namespace Treemapolis.Graphics;

// every root signature is declared in HLSL and travels inside the compiled shader.
public sealed class RootSignature : InterlockedComObject<ID3D12RootSignature>
{
    public RootSignature(GraphicsDevice device, Shader shader)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(shader);
        ExchangeDisposable(device.ComObject.CreateRootSignature(0, shader.Pointer, shader.Length));
    }

    // borrowed for the one D3D12 call that receives it, a copy kept past that call can outlive the root signature.
    public nint NativePointer => ComObject.ToComInstanceNoAddRef();
}
