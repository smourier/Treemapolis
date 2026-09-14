namespace Treemapolis.Graphics;

// every root signature is declared in HLSL and travels inside the compiled shader.
public sealed class RootSignature : InterlockedComObject<ID3D12RootSignature>
{
    public RootSignature(GraphicsDevice device, Shader shader)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(shader);
        ExchangeDisposable(device.ComObject.CreateRootSignature(0, shader.Pointer, shader.Length));
        NativePointer = DirectN.Extensions.Com.ComObject.ToComInstanceOfTypeNoAddRef<ID3D12RootSignature>(ComObject.Object);
    }

    public nint NativePointer { get; }
}
