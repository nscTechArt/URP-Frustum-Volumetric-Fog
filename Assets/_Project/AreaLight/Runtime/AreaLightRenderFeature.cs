using UnityEngine;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class AreaLightFeatureSettings
{
    public RenderPassEvent m_RenderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    public Shader          m_AreaLightPassShader;
    public int             m_MaxAreaLightCount = 16;
}

public class AreaLightRenderFeature : ScriptableRendererFeature
{
    [SerializeField]
    private AreaLightFeatureSettings m_Settings = new();
    private AreaLightRenderPass mPass;
    
    public override void Create()
    {
        mPass = new AreaLightRenderPass(name, m_Settings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(mPass);
    }

    protected override void Dispose(bool disposing)
    {
        mPass.Dispose();
    }
}
