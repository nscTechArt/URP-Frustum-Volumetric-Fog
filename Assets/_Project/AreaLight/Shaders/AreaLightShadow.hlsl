cbuffer POISSON_DISKS {
    static float2 poisson[40] = {
        float2(0.02971195f, 0.8905211f),
        float2(0.2495298f, 0.732075f),
        float2(-0.3469206f, 0.6437836f),
        float2(-0.01878909f, 0.4827394f),
        float2(-0.2725213f, 0.896188f),
        float2(-0.6814336f, 0.6480481f),
        float2(0.4152045f, 0.2794172f),
        float2(0.1310554f, 0.2675925f),
        float2(0.5344744f, 0.5624411f),
        float2(0.8385689f, 0.5137348f),
        float2(0.6045052f, 0.08393857f),
        float2(0.4643163f, 0.8684642f),
        float2(0.335507f, -0.110113f),
        float2(0.03007669f, -0.0007075319f),
        float2(0.8077537f, 0.2551664f),
        float2(-0.1521498f, 0.2429521f),
        float2(-0.2997617f, 0.0234927f),
        float2(0.2587779f, -0.4226915f),
        float2(-0.01448214f, -0.2720358f),
        float2(-0.3937779f, -0.228529f),
        float2(-0.7833176f, 0.1737299f),
        float2(-0.4447537f, 0.2582748f),
        float2(-0.9030743f, 0.406874f),
        float2(-0.729588f, -0.2115215f),
        float2(-0.5383645f, -0.6681151f),
        float2(-0.07709587f, -0.5395499f),
        float2(-0.3402214f, -0.4782109f),
        float2(-0.5580465f, 0.01399586f),
        float2(-0.105644f, -0.9191031f),
        float2(-0.8343651f, -0.4750755f),
        float2(-0.9959937f, -0.0540134f),
        float2(0.1747736f, -0.936202f),
        float2(-0.3642297f, -0.926432f),
        float2(0.1719682f, -0.6798802f),
        float2(0.4424475f, -0.7744268f),
        float2(0.6849481f, -0.3031401f),
        float2(0.5453879f, -0.5152272f),
        float2(0.9634013f, -0.2050581f),
        float2(0.9907925f, 0.08320642f),
        float2(0.8386722f, -0.5428791f)
    };
};

Texture2D _AreaLightShadowMap; SamplerComparisonState sampler_AreaLightShadowMap;
Texture2D _AreaLightShadowMapDummy; SamplerState sampler_AreaLightShadowMapDummy;

float4 _AreaLightShadowParams;
float _AreaLightShadowNearClip;
float _AreaLightShadowFarClip;
float4x4 _AreaLightShadowProjMatrix;

float EdgeSmooth(float2 xy)
{
    float corner = 0.4;
    float outset = 1.0;
    float smooth = 0.5;

    float d = length(max(abs(xy) - 1 + corner * outset, 0.0)) - corner;
    return saturate(1 - smoothstep(-smooth, 0, d));
}

float ReverseZ(float z)
{
    #if UNITY_REVERSED_Z
    return 1.0 - z;
    #endif
    return z;
}

half Shadow(float3 position)
{
    float4 pClip = mul(_AreaLightShadowProjMatrix, float4(position, 1));

    float3 p = pClip.xyz / pClip.w;
    
    if (any(step(1.0, abs(p.xy))))
        return 0;

    float dist = _AreaLightShadowMapDummy.Sample(sampler_AreaLightShadowMapDummy, 0).a;
    
    float edgeSmooth = EdgeSmooth(p.xy);
    
    p = p * 0.5 + 0.5;

    for (int j = 0; j < 10; j++)
    {
        float2 offset = poisson[j + 24] * _AreaLightShadowParams.x;
        float depth = ReverseZ(_AreaLightShadowMap.SampleLevel(sampler_AreaLightShadowMapDummy, p.xy + offset, 0).r);

        dist += max(0.0, p.z - depth);
    }
    
    dist *= _AreaLightShadowParams.y;
    
    p.z -= _AreaLightShadowParams.z / pClip.w;
    p.z = ReverseZ(p.z);
    
    float shadow = 0;
    for (int i = 0; i < 32; i++)
    {
        float lightWidth = lerp(_AreaLightShadowNearClip, _AreaLightShadowFarClip, min(1.0, dist));
        const float2 offset = poisson[i] * lightWidth;
        shadow += _AreaLightShadowMap.SampleCmp(sampler_AreaLightShadowMap, p.xy + offset, p.z);
    }

    shadow *= edgeSmooth / 32.0;
    
    return shadow;
}
