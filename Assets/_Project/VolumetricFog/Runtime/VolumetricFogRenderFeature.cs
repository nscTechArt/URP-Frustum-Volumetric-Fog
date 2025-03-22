using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[Serializable]
public class VolumetricFogSettings
{
    [Space]
    public RenderPassEvent m_RenderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    [Space] 
    public ComputeShader   m_FroxelShader;
    public ComputeShader   m_ScatterShader;
    public Shader          m_CompositeShader;
}

public class VolumetricFogRenderFeature : ScriptableRendererFeature
{
    public VolumetricFogSettings m_Settings = new ();
    private VolumetricFogRenderPass mPass;
    
    public override void Create()
    {
        if (m_Settings.m_FroxelShader == null || m_Settings.m_ScatterShader == null || m_Settings.m_CompositeShader == null)
        {
            Debug.LogWarning("VolumetricFogRenderFeature: Missing shaders");
            return;
        }
        mPass = new VolumetricFogRenderPass(name, m_Settings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (mPass == null) return;
        
        VolumetricFogVolume volumetricFogVolume = VolumeManager.instance.stack.GetComponent<VolumetricFogVolume>();
        if (!volumetricFogVolume || !volumetricFogVolume.IsActive()) return;

        // only render volumetric fog for Game camera
        // ------------------------------------------
        if (renderingData.cameraData.cameraType != CameraType.Game) return;
        
        mPass.Setup(volumetricFogVolume);
        renderer.EnqueuePass(mPass);
    }

    protected override void Dispose(bool disposing)
    {
        mPass.Dispose();
    }
}
