using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class AreaLightRenderPass : ScriptableRenderPass
{
    public AreaLightRenderPass(string featureName, AreaLightFeatureSettings settings)
    {
        // create profiling sampler for this pass
        // --------------------------------------
        mProfilingSampler = new ProfilingSampler(featureName);
        
        // pass settings
        // -------------
        renderPassEvent    = settings.m_RenderPassEvent;
        mPassMaterial      = CoreUtils.CreateEngineMaterial(settings.m_AreaLightPassShader);
        mMaxAreaLightCount = settings.m_MaxAreaLightCount;
        
        // retrieve area light luts and pass them to shader
        // ------------------------------------------------
        PrepareAreaLightLUTs();
        
        // allocate data array with given max area light count
        // ---------------------------------------------------
        mAreaLightColors         = new Vector4[mMaxAreaLightCount];
        mAreaLightVertices       = new Matrix4x4[mMaxAreaLightCount];
        mAreaLightRenderShadow   = new float[mMaxAreaLightCount];
        mAreaLightTextureIndices = new float[mMaxAreaLightCount];
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        // setup RTHandles
        // ---------------
        mSource = mDestination = renderingData.cameraData.renderer.cameraColorTargetHandle;
        var desc = renderingData.cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;
        RenderingUtils.ReAllocateIfNeeded(ref mTemporaryColorTexture, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name:kTemporaryColorTextureName);
        
        // calculate camera frustum properties
        // -----------------------------------
        CameraData cameraData = renderingData.cameraData;
        Matrix4x4 proj = cameraData.GetProjectionMatrix();
        Matrix4x4 viewNoTrans = cameraData.GetViewMatrix();
        viewNoTrans.SetColumn(3, new Vector4(0, 0, 0, 1));
        Matrix4x4 invViewProj    = (proj * viewNoTrans).inverse;
        Vector4 topLeftCorner    = invViewProj.MultiplyPoint(new Vector3(-1,  1, -1));
        Vector4 topRightCorner   = invViewProj.MultiplyPoint(new Vector3( 1,  1, -1));
        Vector4 bottomLeftCorner = invViewProj.MultiplyPoint(new Vector3(-1, -1, -1));
        // pass to shader
        mPassMaterial.SetVector(CameraTopLeftCornerID, topLeftCorner);
        mPassMaterial.SetVector(CameraXExtentID, topRightCorner - topLeftCorner);
        mPassMaterial.SetVector(CameraYExtentID, bottomLeftCorner - topLeftCorner);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        CommandBuffer cmd = CommandBufferPool.Get();
        using (new ProfilingScope(cmd, mProfilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            // retrieve data for each area light
            // ---------------------------------
            HashSet<AreaLight> areaLights = AreaLightManager.Get();
            if (areaLights.Count <= 0) return;
            int index = 0;
            foreach (AreaLight light in areaLights)
            {
                if (index >= mMaxAreaLightCount) break;
                // basic area light data
                mAreaLightColors[index] = light.GetLightColor();
                mAreaLightVertices[index] = light.GetLightVertices();
                // texture related
                mAreaLightTextureIndices[index] = light.TextureIndex;
                // shadow related
                if (!light.m_RenderShadow)
                {
                    mAreaLightRenderShadow[index] = 0;
                }
                else
                {
                    mAreaLightRenderShadow[index] = 1;
                    mAreaLightShadowMapDummy = light.mShadowMapDummy;
                    mAreaLightShadowMap = light.m_ShadowMap;
                    mAreaLightShadowParams = light.GetShadowParams();
                    mAreaLightShadowNearClip = light.GetShadowNearClip();
                    mAreaLightShadowFarClip = light.GetShadowFarClip();
                    mAreaLightShadowProjMatrix = light.GetProjMatrix();
                }
                index++;
            }
            
            // pass area light data to shader
            // ------------------------------
            cmd.SetGlobalInteger(AreaLightCountID, index);
            cmd.SetGlobalFloatArray(AreaLightRenderShadowID, mAreaLightRenderShadow);
            cmd.SetGlobalFloatArray(AreaLightTextureIndicesID, mAreaLightTextureIndices);
            cmd.SetGlobalVectorArray(AreaLightColorsID, mAreaLightColors);
            cmd.SetGlobalMatrixArray(AreaLightVerticesID, mAreaLightVertices);
            cmd.SetGlobalVector(AreaLightShadowParamsID, mAreaLightShadowParams);
            cmd.SetGlobalFloat(AreaLightShadowNearClipID, mAreaLightShadowNearClip);
            cmd.SetGlobalFloat(AreaLightShadowFarClipID, mAreaLightShadowFarClip);
            cmd.SetGlobalMatrix(AreaLightShadowProjMatrixID, mAreaLightShadowProjMatrix);
            cmd.SetGlobalTexture(AreaLightShadowMapID, mAreaLightShadowMap);
            cmd.SetGlobalTexture(AreaLightShadowMapDummyID, mAreaLightShadowMapDummy);
            
            // blit
            // ----
            Blitter.BlitCameraTexture(cmd, mSource, mTemporaryColorTexture, mPassMaterial, 0);
            Blitter.BlitCameraTexture(cmd, mTemporaryColorTexture, mDestination);
            
        }
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        CommandBufferPool.Release(cmd);
    }

    public void Dispose()
    {
        CoreUtils.Destroy(mPassMaterial);
        mPassMaterial = null;
    }

    private void PrepareAreaLightLUTs()
    {
        // retrieve area light luts
        // ------------------------
        mTransformInvDiffuseTexture = AreaLightLUT.LoadLUT(AreaLightLUT.LUTType.TransformInvDisneyDiffuse);
        mTransformInvSpecularTexture = AreaLightLUT.LoadLUT(AreaLightLUT.LUTType.TransformInvDisneyGGX);
        mFresnelTexture = AreaLightLUT.LoadLUT(AreaLightLUT.LUTType.AmpDiffAmpSpecFresnel);
        
        // pass luts to shader
        // -------------------
        mPassMaterial.SetTexture(TransformInvDiffuseID, mTransformInvDiffuseTexture);
        mPassMaterial.SetTexture(TransformInvSpecularID, mTransformInvSpecularTexture);
        mPassMaterial.SetTexture(AmpDiffAmpSpecFresnelID, mFresnelTexture);
    }
    
    // profiling related
    // -----------------
    private ProfilingSampler mProfilingSampler;
    // blit related
    // ------------
    private Material mPassMaterial;
    private RTHandle mSource, mDestination;
    private RTHandle mTemporaryColorTexture;
    private const string kTemporaryColorTextureName = "_TemporaryColorTexture";
    // area light luts related
    // -----------------------
    // TODO: can these lut textures be local variables?
    private Texture2D mTransformInvDiffuseTexture;
    private Texture2D mTransformInvSpecularTexture;
    private Texture2D mFresnelTexture;
    // area light data from scene
    // --------------------------
    private int           mMaxAreaLightCount;
    // single area light data
    private float[]       mAreaLightRenderShadow;
    private float[]       mAreaLightTextureIndices;
    private Vector4[]     mAreaLightColors;
    private Matrix4x4[]   mAreaLightVertices;
    // TODO: 
    private float         mAreaLightShadowNearClip;
    private float         mAreaLightShadowFarClip;
    private Vector4       mAreaLightShadowParams;
    private Matrix4x4     mAreaLightShadowProjMatrix;
    private Texture2D     mAreaLightShadowMapDummy;
    private RenderTexture mAreaLightShadowMap;
    
    // cached shader property IDs
    // --------------------------
    private static readonly int TransformInvDiffuseID = Shader.PropertyToID("_TransformInv_Diffuse");
    private static readonly int TransformInvSpecularID = Shader.PropertyToID("_TransformInv_Specular");
    private static readonly int AmpDiffAmpSpecFresnelID = Shader.PropertyToID("_AmpDiffAmpSpecFresnel");
    private static readonly int AreaLightCountID = Shader.PropertyToID("_AreaLightCount");
    private static readonly int AreaLightColorsID = Shader.PropertyToID("_AreaLightColors");
    private static readonly int AreaLightVerticesID = Shader.PropertyToID("_AreaLightVertices");
    private static readonly int AreaLightRenderShadowID = Shader.PropertyToID("_AreaLightRenderShadow");
    private static readonly int AreaLightShadowParamsID = Shader.PropertyToID("_AreaLightShadowParams");
    private static readonly int AreaLightShadowNearClipID = Shader.PropertyToID("_AreaLightShadowNearClip");
    private static readonly int AreaLightShadowFarClipID = Shader.PropertyToID("_AreaLightShadowFarClip");
    private static readonly int AreaLightShadowProjMatrixID = Shader.PropertyToID("_AreaLightShadowProjMatrix");
    private static readonly int AreaLightShadowMapID = Shader.PropertyToID("_AreaLightShadowMap");
    private static readonly int AreaLightShadowMapDummyID = Shader.PropertyToID("_AreaLightShadowMapDummy");
    private static readonly int CameraTopLeftCornerID = Shader.PropertyToID("_CameraTopLeftCorner");
    private static readonly int CameraXExtentID = Shader.PropertyToID("_CameraXExtent");
    private static readonly int CameraYExtentID = Shader.PropertyToID("_CameraYExtent");
    private static readonly int AreaLightTextureIndicesID = Shader.PropertyToID("_AreaLightTextureIndices");
}
