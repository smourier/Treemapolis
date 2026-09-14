namespace Treemapolis.Graphics;

public sealed class FeatureSupport
{
    public FeatureSupport(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var shaderModel = new D3D12_FEATURE_DATA_SHADER_MODEL { HighestShaderModel = D3D_SHADER_MODEL.D3D_SHADER_MODEL_6_6 };
        if (device.ComObject.CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_SHADER_MODEL, ref shaderModel).IsSuccess)
        {
            HighestShaderModel = shaderModel.HighestShaderModel;
        }

        var options = new D3D12_FEATURE_DATA_D3D12_OPTIONS();
        if (device.ComObject.CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS, ref options).IsSuccess)
        {
            ResourceBindingTier = options.ResourceBindingTier;
        }

        var options5 = new D3D12_FEATURE_DATA_D3D12_OPTIONS5();
        if (device.ComObject.CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS5, ref options5).IsSuccess)
        {
            RaytracingTier = options5.RaytracingTier;
        }

        var options7 = new D3D12_FEATURE_DATA_D3D12_OPTIONS7();
        if (device.ComObject.CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS7, ref options7).IsSuccess)
        {
            MeshShaderTier = options7.MeshShaderTier;
        }
    }

    public D3D_SHADER_MODEL HighestShaderModel { get; }
    public D3D12_RESOURCE_BINDING_TIER ResourceBindingTier { get; }
    public D3D12_RAYTRACING_TIER RaytracingTier { get; }
    public D3D12_MESH_SHADER_TIER MeshShaderTier { get; }

    // the tiers are encoded as major times ten plus minor, 12 is 1.2.
    public string? RaytracingVersion => RaytracingTier == D3D12_RAYTRACING_TIER.D3D12_RAYTRACING_TIER_NOT_SUPPORTED ? null : FormatTier((int)RaytracingTier);
    public string? MeshShaderVersion => MeshShaderTier == D3D12_MESH_SHADER_TIER.D3D12_MESH_SHADER_TIER_NOT_SUPPORTED ? null : FormatTier((int)MeshShaderTier);
    public string ShaderModelVersion => string.Create(CultureInfo.InvariantCulture, $"{(int)HighestShaderModel >> 4}.{(int)HighestShaderModel & 0xF}");

    public bool SupportsShaderModel65 => HighestShaderModel >= D3D_SHADER_MODEL.D3D_SHADER_MODEL_6_5;
    public bool SupportsInlineRaytracing => RaytracingTier >= D3D12_RAYTRACING_TIER.D3D12_RAYTRACING_TIER_1_1 && SupportsShaderModel65;
    public bool SupportsMeshShaders => MeshShaderTier >= D3D12_MESH_SHADER_TIER.D3D12_MESH_SHADER_TIER_1 && SupportsShaderModel65;
    public bool SupportsBindless => ResourceBindingTier >= D3D12_RESOURCE_BINDING_TIER.D3D12_RESOURCE_BINDING_TIER_2;

    // a sample count is usable only when every target the scene renders into supports it.
    public static IReadOnlyList<uint> GetSampleCounts(GraphicsDevice device, IEnumerable<DXGI_FORMAT> formats)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(formats);
        uint[] candidates = [2, 4, 8];
        var counts = new List<uint> { 1 };
        foreach (var count in candidates)
        {
            var supported = true;
            foreach (var format in formats)
            {
                var levels = new D3D12_FEATURE_DATA_MULTISAMPLE_QUALITY_LEVELS { Format = format, SampleCount = count };
                if (device.ComObject.CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_MULTISAMPLE_QUALITY_LEVELS, ref levels).IsError || levels.NumQualityLevels == 0)
                {
                    supported = false;
                    break;
                }
            }

            if (supported)
            {
                counts.Add(count);
            }
        }
        return counts;
    }

    private static string FormatTier(int tier) => string.Create(CultureInfo.InvariantCulture, $"{tier / 10}.{tier % 10}");
}
