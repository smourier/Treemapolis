namespace Treemapolis.Graphics;

public sealed class CommandAllocator(IComObject<ID3D12CommandAllocator> comObject) : InterlockedComObject<ID3D12CommandAllocator>(comObject)
{
}
