using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public partial class AreaLightManager
{
    private void Awake()
    {
        mBlurMaterial = CoreUtils.CreateEngineMaterial("Hidden/LTCFilteredTexture");
        
        if (m_Textures.Length == 0) return;
        if (m_Textures.Any(texture => texture == null))
        {
            Debug.LogError("Cannot have null textures in the AreaLightManager");
            return;
        }
        
        m_FilteredDiffuseTextures = new RenderTexture[m_Textures.Length];
        m_FilteredSpecularTextures = new RenderTexture[m_Textures.Length];
        CreatePrefilteredTextures();
        
        SetupTextureArrays();
        Shader.SetGlobalTexture(FilteredDiffuseTextureArrayID, mFilteredDiffuseTextureArray);
        Shader.SetGlobalTexture(FilteredSpecularTextureArrayID, mFilteredSpecularTextureArray);
    }

    private void OnDestroy()
    {
        CoreUtils.Destroy(mBlurMaterial);
    }

    private void CreatePrefilteredTextures()
    {
        for (int index = 0; index < m_Textures.Length; index++)
        {
            m_FilteredDiffuseTextures[index] = PrepareTexture(ref m_Textures[index], false);
            m_FilteredSpecularTextures[index] = PrepareTexture(ref m_Textures[index], true);
        }
    }
    
    private RenderTexture PrepareTexture(ref Texture2D texture, bool specular)
    {
        var targetTexture = new RenderTexture(kTextureResolution, kTextureResolution, 0, kTextureFormat, kMipCount)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            useMipMap = true,
            autoGenerateMips = false,
        };
        if (!targetTexture.IsCreated()) targetTexture.Create();

        var temporaryTexture = new RenderTexture(kTextureResolution, kTextureResolution, 0, kTextureFormat, kMipCount)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            useMipMap = true,
            autoGenerateMips = false,
        };
        if (!temporaryTexture.IsCreated()) temporaryTexture.Create();
        
        
        CommandBuffer cmd = CommandBufferPool.Get();

        if (specular)
        {
            cmd.Blit(texture, targetTexture);
        }
        else
        {
            cmd.Blit(texture, targetTexture, mBlurMaterial, 2);
        }
        cmd.Blit(targetTexture, temporaryTexture);
        for (int i = 1; i < texture.mipmapCount; i++)
        {
            cmd.SetRenderTarget(targetTexture, i);
            cmd.SetGlobalInt(LevelID, i - 1);
            cmd.Blit(temporaryTexture, BuiltinRenderTextureType.CurrentActive, mBlurMaterial, 0);
            cmd.SetRenderTarget(temporaryTexture, i);
            cmd.Blit(targetTexture, BuiltinRenderTextureType.CurrentActive, mBlurMaterial, 1);
        }
        
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        CommandBufferPool.Release(cmd);
        temporaryTexture.Release();

        return targetTexture;
    }
    
    private void SetupTextureArrays()
    {
        mFilteredDiffuseTextureArray = new Texture2DArray(
            kTextureResolution, kTextureResolution,
            m_Textures.Length, TextureFormat.RGBAHalf, kMipCount, false);
        mFilteredSpecularTextureArray = new Texture2DArray(
            kTextureResolution, kTextureResolution,
            m_Textures.Length, TextureFormat.RGBAHalf, kMipCount, false);
        
        for (int i = 0; i < m_Textures.Length; i++)
        {
            Graphics.CopyTexture(m_FilteredDiffuseTextures[i], 0, mFilteredDiffuseTextureArray, i);
            Graphics.CopyTexture(m_FilteredSpecularTextures[i], 0, mFilteredSpecularTextureArray, i);
        }
    }
    
    
    public Texture2D[] m_Textures;
    public RenderTexture[] m_FilteredDiffuseTextures;
    public RenderTexture[] m_FilteredSpecularTextures;
    private Texture2DArray mFilteredDiffuseTextureArray;
    private Texture2DArray mFilteredSpecularTextureArray;
    private Material mBlurMaterial;
    // Constants
    // ---------
    private const int                 kTextureResolution = 1024;
    private const int                 kMipCount = 8;
    private const RenderTextureFormat kTextureFormat = RenderTextureFormat.DefaultHDR;
    // cached shader property IDs
    // --------------------------
    private static readonly int LevelID = Shader.PropertyToID("_Level");
    private static readonly int FilteredDiffuseTextureArrayID = Shader.PropertyToID("_FilteredDiffuseTextureArray");
    private static readonly int FilteredSpecularTextureArrayID = Shader.PropertyToID("_FilteredSpecularTextureArray");
}
