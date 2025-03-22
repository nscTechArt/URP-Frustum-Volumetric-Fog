using UnityEngine;

using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

public partial class AreaLight
{
    private void InitializeShadow()
    {
        if (mCameraForShadow != null) return;

        // create an GameObject with Camera component
        // ------------------------------------------
        GameObject cameraGO = new(kGameObjectName)
        {
            hideFlags = HideFlags.DontSave
        };
        cameraGO.AddComponent(typeof(Camera));
        cameraGO.TryGetComponent(out mCameraForShadow);
        
        // set GameObject properties
        // -------------------------
        mCameraTransform = cameraGO.transform;
        mCameraTransform.parent = transform;
        mCameraTransform.localRotation = Quaternion.identity;
        
        // set camera properties
        // ---------------------
        mCameraForShadow.enabled = true;
        mCameraForShadow.cullingMask = m_ShadowCullingMask;
        mCameraForShadow.clearFlags = CameraClearFlags.SolidColor;
        mCameraForShadow.backgroundColor = Color.white;
        mCameraForShadow.targetTexture = m_ShadowMap;
        
        // set URP camera data
        // -------------------
        mCameraData = mCameraForShadow.GetUniversalAdditionalCameraData();
        mCameraData.SetRenderer(1);
        mCameraData.requiresDepthOption = CameraOverrideOption.Off;
        
        // retrieve shadow map resolution
        // ------------------------------
        m_ShadowResolution = m_ShadowMap.width;
        
        // initialize dummy shadow map
        // ---------------------------
        if (mShadowMapDummy != null) return;
        mShadowMapDummy = new Texture2D(1, 1, TextureFormat.R8, false, true)
        {
            filterMode = FilterMode.Point
        };
        mShadowMapDummy.SetPixel(0, 0, new Color(0, 0, 0, 0));
        mShadowMapDummy.Apply();
    }

    private void UpdateShadow()
    {
        if (m_ShadowMap != null && mShadowRenderTime == Time.renderedFrameCount) return;

        // update camera properties based on light properties
        // --------------------------------------------------
        mCameraForShadow.aspect = m_LightSize.x / m_LightSize.y;
        if (m_LightAngle == 0.0f)
        {
            mCameraForShadow.nearClipPlane = 0;
            mCameraForShadow.farClipPlane = m_LightSize.z;
            mCameraForShadow.orthographic = true;
            mCameraForShadow.orthographicSize = 0.5f * m_LightSize.y;
            
            mCameraTransform.localPosition = Vector3.zero;
        }
        else
        {
            mCameraForShadow.fieldOfView = m_LightAngle;
            mCameraForShadow.nearClipPlane = mLightFrustumNear;
            mCameraForShadow.farClipPlane = mLightFrustumNear + m_LightSize.z;
            mCameraForShadow.orthographic = false;
            
            mCameraTransform.localPosition = -mLightFrustumNear * Vector3.forward;
        }
        
        mCameraForShadow.targetTexture = m_ShadowMap;
        mShadowRenderTime = Time.renderedFrameCount;
    }

    public Vector4 GetShadowParams()
    {
        return new Vector4
        (
            1 * m_ReceiverSearchDistance / m_ShadowResolution,
            m_ReceiverDistanceScale * 0.5f / 10f,
            m_ShadowBias,
            1
        );
    }
    
    public float GetShadowNearClip()
    {
        return m_ShadowNearClip / m_ShadowResolution;
    }
    
    public float GetShadowFarClip()
    {
        return (m_ShadowFarClip) / m_ShadowResolution;
    }
    
    // shadow properties
    // -----------------
    [Header("Shadow Properties"), Space(5)]
    public bool       m_RenderShadow;
    [SerializeField] 
    private LayerMask m_ShadowCullingMask;
    [SerializeField]
    private float     m_ReceiverSearchDistance = 24.0f;
    [SerializeField]
    private float     m_ReceiverDistanceScale = 5.0f;
    public float      m_ShadowNearClip = 4.0f;
    public float      m_ShadowFarClip = 22.0f;
    [SerializeField, Range(0.0f, 0.5f)]
    private float     m_ShadowBias = 0.001f;
    
    // camera properties
    // -----------------
    private Transform                     mCameraTransform;
    private Camera                        mCameraForShadow;
    private UniversalAdditionalCameraData mCameraData;
    // shadow mapping related
    // ----------------------
    [HideInInspector]
    public Texture2D      mShadowMapDummy;
    public RenderTexture  m_ShadowMap;
    private int           m_ShadowResolution;
    private int           mShadowRenderTime = -1;
    
    // constants
    // ---------
    private const string kGameObjectName = "Shadow Camera";
    
}
