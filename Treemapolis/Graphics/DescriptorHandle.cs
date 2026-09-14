namespace Treemapolis.Graphics;

public readonly record struct DescriptorHandle(uint Index, D3D12_CPU_DESCRIPTOR_HANDLE Cpu, D3D12_GPU_DESCRIPTOR_HANDLE Gpu)
{
    public bool IsValid => Cpu.ptr != 0;
}
