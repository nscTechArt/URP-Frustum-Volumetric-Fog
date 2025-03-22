using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ProfilingScope = UnityEngine.Rendering.ProfilingScope;

public class VolumetricFogRenderPass : ScriptableRenderPass
{
    public VolumetricFogRenderPass(string featureName, VolumetricFogSettings settings)
    {
        // initialize
        // ----------
        mProfilingSampler  = new ProfilingSampler(featureName);
        renderPassEvent    = settings.m_RenderPassEvent;
        mFroxelShader      = settings.m_FroxelShader;
        mScatterShader     = settings.m_ScatterShader;
        mCompositeMaterial = CoreUtils.CreateEngineMaterial(settings.m_CompositeShader);
        mCompositeMaterial.hideFlags = HideFlags.HideAndDontSave;
        
        // retrieve kernel indices
        // -----------------------
        mFroxelShaderKernel  = mFroxelShader.FindKernel("CSMain");
        mScatterShaderKernel = mScatterShader.FindKernel("CSMain");

        // prepare volume textures
        // -----------------------
        RenderTextureDescriptor volumeDescriptor = new(_KVolumeResolution.x, _KVolumeResolution.y)
        {
            dimension = TextureDimension.Tex3D,
            enableRandomWrite = true,
            volumeDepth = _KVolumeResolution.z,
            msaaSamples = 1,
            depthBufferBits = 0,
            colorFormat = GetVolumeTextureFormat(),
        };
        mFroxelTexture = RTHandles.Alloc(volumeDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: kFroxelVolumeTexture);
        mScatterTexture = RTHandles.Alloc(volumeDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: kScatterTextureName);
        
        // initialize history view projection matrix
        // -----------------------------------------
        mVolumetricFogData.historyViewProj = Matrix4x4.identity;
    }
    
    public void Setup(VolumetricFogVolume volumetricFogVolume)
    {
        // update VolumetricFogData from VolumetricFog component
        // -----------------------------------------------------
        mVolumetricFogVolume = volumetricFogVolume;
        mVolumetricFogData.intensity         = volumetricFogVolume.m_Intensity.value;
        mVolumetricFogData.anisotropy        = volumetricFogVolume.GetAnisotropy();
        mVolumetricFogData.constantFog       = volumetricFogVolume.m_ConstantFog.value;
        mVolumetricFogData.heightFog         = volumetricFogVolume.m_HeightFogAmount.value;
        mVolumetricFogData.heightFogExponent = volumetricFogVolume.m_HeightFogExponent.value;
        mVolumetricFogData.heightFogOffset   = volumetricFogVolume.m_HeightFogOffset.value;
        mVolumetricFogData.noiseAmount       = volumetricFogVolume.m_FogNoiseAmount.value;
        mVolumetricFogData.noiseScale        = volumetricFogVolume.m_FogNoiseScale.value;
        mVolumetricFogData.ambientColor      = volumetricFogVolume.GetAmbient();
        mVolumetricFogData.wind              = volumetricFogVolume.GetNoiseWind();
        
        // set keywords based on phase function
        // ------------------------------------
        switch (volumetricFogVolume.m_PhaseFunction.value)
        {
            case PhaseFunction.Schlick:
                mFroxelShader.EnableKeyword(kSchlick);
                mFroxelShader.DisableKeyword(kHenyeyGreenstein);
                mFroxelShader.DisableKeyword(kCornetteShanks);
                // mFroxelShader.DisableKeyword(kRayleigh);
                break;
            case PhaseFunction.HenyeyGreenstein:
                mFroxelShader.EnableKeyword(kHenyeyGreenstein);
                mFroxelShader.DisableKeyword(kSchlick);
                mFroxelShader.DisableKeyword(kCornetteShanks);
                // mFroxelShader.DisableKeyword(kRayleigh);
                break;
            case PhaseFunction.CornetteShanks:
                mFroxelShader.EnableKeyword(kCornetteShanks);
                mFroxelShader.DisableKeyword(kSchlick);
                mFroxelShader.DisableKeyword(kHenyeyGreenstein);
                // mFroxelShader.DisableKeyword(kRayleigh);
                break;
            // case PhaseFunction.Rayleigh:
            //     mFroxelShader.EnableKeyword(kRayleigh);
            //     mFroxelShader.DisableKeyword(kSchlick);
            //     mFroxelShader.DisableKeyword(kHenyeyGreenstein);
            //     mFroxelShader.DisableKeyword(kCornetteShanks);
            //     break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        
        // prepare compute buffers
        // -----------------------
        mDummyComputeBuffer ??= new ComputeBuffer(1, 4);
        mVolumetricFogDataBuffer ??= new ComputeBuffer(1, Marshal.SizeOf(typeof(VolumetricFogData)));
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        // setup RTHandles
        // ---------------
        mSource = mDestination = renderingData.cameraData.renderer.cameraColorTargetHandle;
        var descriptor = renderingData.cameraData.cameraTargetDescriptor;
        descriptor.depthBufferBits = 0;
        RenderingUtils.ReAllocateIfNeeded(ref mCompositeTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name:kCompositeTextureName);
        
        // calculate camera frustum properties
        // -----------------------------------
        CameraData cameraData = renderingData.cameraData;
        mProj = cameraData.GetProjectionMatrix();
        mView = cameraData.GetViewMatrix();
        Matrix4x4 viewNoTrans = mView;
        viewNoTrans.SetColumn(3, new Vector4(0, 0, 0, 1));
        Matrix4x4 invViewProj    = (mProj * viewNoTrans).inverse;
        Vector4 topLeftCorner    = invViewProj.MultiplyPoint(new Vector3(-1, 1, 1));
        Vector4 topRightCorner   = invViewProj.MultiplyPoint(new Vector3(1, 1, 1));
        Vector4 bottomLeftCorner = invViewProj.MultiplyPoint(new Vector3(-1, -1, 1));
        
        // fill the remaining VolumetricFogData
        // ------------------------------------
        mFarClip = Mathf.Min(cameraData.camera.farClipPlane, mVolumetricFogVolume.m_FarClip.value);
        float depthCompensation = (mFarClip - mVolumetricFogVolume.m_NearClip.value) * 0.01f;
        mVolumetricFogData.density             = mVolumetricFogVolume.m_Density.value * 0.128f * depthCompensation;
        mVolumetricFogData.cameraTopLeftCorner = topLeftCorner;
        mVolumetricFogData.cameraXExtent       = topRightCorner - topLeftCorner;
        mVolumetricFogData.cameraYExtent       = bottomLeftCorner - topLeftCorner;
        
        // setup global data
        // -----------------
        cmd.SetGlobalFloat(_VolumetricFogNearClip, mVolumetricFogVolume.m_NearClip.value);
        cmd.SetGlobalFloat(_VolumetricFogFarClip, mFarClip);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        CommandBuffer cmd = CommandBufferPool.Get();
        using (new ProfilingScope(cmd, mProfilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            // Step 1: inject density and lighting data into the frustum volume
            // ----------------------------------------------------------------
            using (new ProfilingScope(cmd, mFroxelProfilingSampler))
            {
                // pass global fog parameters and density data to froxel shader
                mVolumetricFogDataBuffer.SetData(new[] { mVolumetricFogData });
                cmd.SetComputeBufferParam(mFroxelShader, mFroxelShaderKernel, _VolumetricFogData, mVolumetricFogDataBuffer);
                // pass light data to froxel shader
                UpdateLightDataBuffer(cmd);
                // set froxel textures
                cmd.SetComputeTextureParam(mFroxelShader, mFroxelShaderKernel, _FroxelVolumeTexture, mFroxelTexture.nameID);
                // dispatch froxel shader
                cmd.DispatchCompute(mFroxelShader, mFroxelShaderKernel, _KVolumeResolution.x / 16, _KVolumeResolution.y / 2, _KVolumeResolution.z / 16);
                // update history view projection matrix
                mVolumetricFogData.historyViewProj = mProj * mView;
            }
            
            // Step 2: scatter to create volumetric fog
            // ----------------------------------------
            using (new ProfilingScope(cmd, mScatterProfilingSampler))
            {
                // dispatch scatter shader
                mScatterShader.SetTexture(mScatterShaderKernel, _ScatterVolumeTexture, mScatterTexture);
                cmd.SetComputeTextureParam(mScatterShader, mScatterShaderKernel, _FroxelVolumeTexture, mFroxelTexture.nameID);
                cmd.DispatchCompute(mScatterShader, mScatterShaderKernel, _KVolumeResolution.x / 32, _KVolumeResolution.y / 2, _KVolumeResolution.z / 1);
            }
            
            // Step 3: composite volumetric fog with the scene
            // -----------------------------------------------
            using (new ProfilingScope(cmd, mCompositeProfilingSampler))
            {
                cmd.SetGlobalTexture(_ScatterVolumeTexture, mScatterTexture);
                cmd.SetGlobalVector(_ScreenTexelSize, new Vector4(1.0f / Screen.width, 1.0f / Screen.height, Screen.width, Screen.height));
                cmd.SetGlobalVector(_VolumeResolution, new Vector4(1.0f / _KVolumeResolution.x, 1.0f / _KVolumeResolution.y, 1.0f / _KVolumeResolution.z, 0));
                cmd.SetGlobalFloat(_CameraFarOverMaxFar, renderingData.cameraData.camera.farClipPlane / mFarClip);
                cmd.SetGlobalFloat(_NearOverFarClip, mVolumetricFogVolume.m_NearClip.value / mFarClip);
                Blitter.BlitCameraTexture(cmd, mSource, mCompositeTexture, mCompositeMaterial, 0);
                Blitter.BlitCameraTexture(cmd, mCompositeTexture, mDestination);
            }
        }
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        CommandBufferPool.Release(cmd);
    }

    public void Dispose()
    {
        CoreUtils.Destroy(mCompositeMaterial);
        mCompositeMaterial = null;
        
        mScatterTexture?.Release();
        
        mDummyComputeBuffer?.Release();
        mVolumetricFogDataBuffer?.Release();

        mSource?.Release();
        mDestination?.Release();
        mCompositeTexture?.Release();
    }
    
    private void RecreateLightDataBuffer()
    {
        // get total light count
        // ---------------------
        mFogLights = VolumetricFogLightManager.Get();
        mTotalFogLightCount = mFogLights.Count(fogLight => fogLight.isActiveAndEnabled);

        // if there are no fog lights, release the buffer
        // ----------------------------------------------
        if (mTotalFogLightCount <= 0)
        {
            mLightDataBuffer?.Release();
            mLightDataBuffer = null;
            return;
        }
        
        // recreate buffer if necessary
        // ----------------------------
        if (mLightDataBuffer == null || mLightDataBuffer.count != mTotalFogLightCount)
        {
            mLightDataBuffer?.Release();
            mLightDataBuffer = new ComputeBuffer(mTotalFogLightCount, Marshal.SizeOf(typeof(LightData)));
        }
    }

    private void UpdateLightDataBuffer(CommandBuffer cmd)
    {
        RecreateLightDataBuffer();
        
        // pass fog light count to GPU
        // ---------------------------
        cmd.SetComputeIntParam(mFroxelShader, _FogLightCount, mTotalFogLightCount);
        
        // if there are no fog lights, return
        // ----------------------------------
        if (mTotalFogLightCount == 0)
        {
            cmd.SetComputeBufferParam(mFroxelShader, mFroxelShaderKernel, _FogLightData, mDummyComputeBuffer);
        }
        
        // check if the light data array needs to be recreated
        // ---------------------------------------------------
        if (mLightDataArray == null || mLightDataArray.Length != mTotalFogLightCount)
        {
            mLightDataArray = new LightData[mTotalFogLightCount];
        }
        
        // fill the light data array
        // -------------------------
        int index = 0;
        foreach (var fogLight in mFogLights)
        {
            // skip if the fog light is not enabled
            // ------------------------------------
            if (!fogLight.isActiveAndEnabled) continue;
            
            // declare a new LightData struct
            // ------------------------------
            LightData lightData = new()
            {
                lightType = (int)fogLight.lightType
            };

            // fill the LightData struct based on the light type
            // -------------------------------------------------
            switch (fogLight.lightType)
            {
                case VolumetricFogLight.LightType.None:
                    continue;
                case VolumetricFogLight.LightType.Point:
                {
                    Light light = fogLight.light;
                
                    float range = light.range * fogLight.m_RangeMultiplier;
                    Vector3 color = new Vector3(light.color.r, light.color.g, light.color.b) * light.intensity * fogLight.m_IntensityMultiplier;
                
                    lightData.pos = light.transform.position;
                    lightData.range = 1.0f / (range * range);
                    lightData.color = color;
                    break;
                }
                case VolumetricFogLight.LightType.Spot:
                {
                    Light light = fogLight.light;
                    float range = light.range * fogLight.m_RangeMultiplier;
                    Vector3 color = new Vector3(light.color.r, light.color.g, light.color.b) * light.intensity * fogLight.m_IntensityMultiplier;
                    float innerCos = Mathf.Cos(Mathf.Deg2Rad * light.innerSpotAngle * 0.5f);
                    float outerCos = Mathf.Cos(Mathf.Deg2Rad * light.spotAngle * 0.5f);
                    float angleRangeInv = 1.0f / Mathf.Max(innerCos - outerCos, 0.001f);
                    lightData.pos = light.transform.position;
                    lightData.direction = light.transform.forward;
                    lightData.range = 1.0f / (range * range);
                    lightData.color = color;
                    lightData.invAngleRange = angleRangeInv;
                    lightData.outerCosScaled = -outerCos * angleRangeInv;
                    break;
                }
                case VolumetricFogLight.LightType.Area:
                {
                    AreaLight light = fogLight.areaLight;
                    lightData.projection = light.GetProjMatrix(true);
                    lightData.pos = light.GetLightPosition();
                    lightData.color = light.GetLightColor() * fogLight.m_IntensityMultiplier;
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
            
            // add the LightData struct to the LightData array
            // -----------------------------------------------
            mLightDataArray[index++] = lightData;
        }
        
        // set the LightData array to the LightData buffer
        // -----------------------------------------------
        mLightDataBuffer.SetData(mLightDataArray);
        cmd.SetComputeBufferParam(mFroxelShader, mFroxelShaderKernel, _FogLightData, mLightDataBuffer);
    }
    
    private static RenderTextureFormat GetVolumeTextureFormat()
    {
        return SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render) ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.DefaultHDR;
    }
     
    #region Fields
    
    private const string kSchlick = "_SCHLICK";
    private const string kHenyeyGreenstein = "_HENYEY_GREENSTEIN";
    private const string kCornetteShanks = "_CORNETTE_SHANKS";
    // private const string kRayleigh = "_RAYLEIGH";
    
    // profiling related
    // -----------------
    private readonly ProfilingSampler mProfilingSampler;
    private readonly ProfilingSampler mFroxelProfilingSampler    = new(kFroxelProfilerTag);
    private readonly ProfilingSampler mScatterProfilingSampler   = new(kScatterProfilerTag);
    private readonly ProfilingSampler mCompositeProfilingSampler = new(kCompositeProfilerTag);
    
    // pass shaders and material
    // -------------------------
    private readonly ComputeShader mFroxelShader;
    private readonly ComputeShader mScatterShader;
    private readonly int           mFroxelShaderKernel;
    private readonly int           mScatterShaderKernel;
    private Material               mCompositeMaterial;
    
    // VolumetricFog volume component
    // ------------------------------
    private VolumetricFogVolume mVolumetricFogVolume;
    
    // render textures
    // ---------------
    private readonly RTHandle mScatterTexture;
    private          RTHandle      mCompositeTexture;
    private          RTHandle      mSource, mDestination;
    private RTHandle mFroxelTexture;
    
    // shader properties and related variables
    // ---------------------------------------
    private VolumetricFogData mVolumetricFogData;
    private ComputeBuffer     mVolumetricFogDataBuffer;
    private ComputeBuffer     mDummyComputeBuffer;
    private float             mFarClip;
    private Matrix4x4         mProj, mView;
    
    // structures
    // ----------
    private struct VolumetricFogData
    {
        [UsedImplicitly] public float density;
        [UsedImplicitly] public float intensity;
        [UsedImplicitly] public float anisotropy;
        
        [UsedImplicitly] public float constantFog;
        [UsedImplicitly] public float heightFog;
        [UsedImplicitly] public float heightFogExponent;
        [UsedImplicitly] public float heightFogOffset;
        
        [UsedImplicitly] public float noiseAmount;
        [UsedImplicitly] public float noiseScale;
        
        [UsedImplicitly] public Vector3 ambientColor;

        [UsedImplicitly] public Vector4 wind;
        
        [UsedImplicitly] public Vector4 cameraTopLeftCorner;
        [UsedImplicitly] public Vector4 cameraXExtent;
        [UsedImplicitly] public Vector4 cameraYExtent;
        
        [UsedImplicitly] public Matrix4x4 historyViewProj;
    }
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct LightData 
    {
        [FieldOffset(0)]  public Vector3 pos;
        [FieldOffset(12)] public float   range;
        [FieldOffset(16)] public Vector3 color;
        [FieldOffset(28)] public float   invAngleRange;
        [FieldOffset(32)] public Vector3 direction;
        [FieldOffset(44)] public float   outerCosScaled;
        [FieldOffset(48)] public int     lightType;
        [FieldOffset(52)] private Vector3 padding;
        [FieldOffset(64)] public Matrix4x4 projection;
    }

    // constants and readonlys
    // -----------------------
    private const string kFroxelProfilerTag    = "Froxel Injection";
    private const string kScatterProfilerTag   = "Scatter";
    private const string kCompositeProfilerTag = "Composite";
    private const string kScatterTextureName   = "_ScatterVolumeTexture";
    private const string kFroxelVolumeTexture   = "_FroxelVolumeTexture";
    private const string kCompositeTextureName = "_CompositeTexture";
    private static readonly Vector3Int _KVolumeResolution = new (160, 90, 128);
    // cached shader property IDs
    // --------------------------
    private static readonly int _VolumetricFogData    = Shader.PropertyToID("_VolumetricFogData");
    private static readonly int _ScatterVolumeTexture = Shader.PropertyToID("_ScatterVolumeTexture");
    private static readonly int _FroxelVolumeTexture  = Shader.PropertyToID("_FroxelVolumeTexture");
    private static readonly int _ScreenTexelSize      = Shader.PropertyToID("_ScreenTexelSize");
    private static readonly int _VolumeResolution     = Shader.PropertyToID("_VolumeResolution");
    private static readonly int _CameraFarOverMaxFar  = Shader.PropertyToID("_CameraFarOverMaxFar");
    private static readonly int _NearOverFarClip      = Shader.PropertyToID("_NearOverFarClip");
    private static readonly int _VolumetricFogNearClip = Shader.PropertyToID("_VolumetricFogNearClip");
    private static readonly int _VolumetricFogFarClip = Shader.PropertyToID("_VolumetricFogFarClip");
    private static readonly int _FogLightCount = Shader.PropertyToID("_FogLightCount");
    private static readonly int _FogLightData = Shader.PropertyToID("_FogLightData");
    
    private ComputeBuffer mLightDataBuffer;
    private LightData[] mLightDataArray;
    private int mTotalFogLightCount;
    private HashSet<VolumetricFogLight> mFogLights;

    #endregion
}




