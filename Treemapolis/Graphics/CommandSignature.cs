namespace Treemapolis.Graphics;

public sealed class CommandSignature : InterlockedComObject<ID3D12CommandSignature>
{
    private CommandSignature(IComObject<ID3D12CommandSignature> signature)
        : base(signature)
    {
    }

    public static unsafe CommandSignature CreateDraw(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var argument = new D3D12_INDIRECT_ARGUMENT_DESC { Type = D3D12_INDIRECT_ARGUMENT_TYPE.D3D12_INDIRECT_ARGUMENT_TYPE_DRAW };
        var desc = new D3D12_COMMAND_SIGNATURE_DESC
        {
            ByteStride = (uint)sizeof(D3D12_DRAW_ARGUMENTS),
            NumArgumentDescs = 1,
            pArgumentDescs = (nint)(&argument),
        };

        device.NativeObject.CreateCommandSignature(desc, null, typeof(ID3D12CommandSignature).GUID, out var signature).ThrowOnError();
        return new CommandSignature(DirectN.Extensions.Com.ComObject.FromPointer<ID3D12CommandSignature>(signature)!);
    }
}
