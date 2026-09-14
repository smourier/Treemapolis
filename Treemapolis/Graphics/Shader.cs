namespace Treemapolis.Graphics;

// the DXIL compiled at build time and embedded in the exe, pinned so its address can go straight into a pipeline description.
public sealed class Shader
{
    private const string _resourcePrefix = "Treemapolis.Shaders.";
    private const string _resourceExtension = ".cso";
    private readonly byte[] _bytes;

    public unsafe Shader(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        using var stream = typeof(Shader).Assembly.GetManifestResourceStream(_resourcePrefix + name + _resourceExtension)
            ?? throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, Res.ShaderNotFound, name));

        _bytes = GC.AllocateUninitializedArray<byte>((int)stream.Length, pinned: true);
        stream.ReadExactly(_bytes);
    }

    public string Name { get; }
    public nint Pointer => Marshal.UnsafeAddrOfPinnedArrayElement(_bytes, 0);
    public nuint Length => (nuint)_bytes.Length;

    public D3D12_SHADER_BYTECODE Bytecode => new() { pShaderBytecode = Pointer, BytecodeLength = Length };

    public override string ToString() => Name;
}
