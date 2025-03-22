using System;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteInEditMode]
public partial class AreaLight : MonoBehaviour
{
    private void Start()
    {
        Initialize();
    }

    private void OnEnable()
    {
        // add to manager, so we can retrieve light data later in render feature
        // ---------------------------------------------------------------------
        if (!mAddedToManager) mAddedToManager = AreaLightManager.Add(this);
        
        // initialize material property block
        // ----------------------------------
        mPropertyBlock = new MaterialPropertyBlock();
    }

    private void OnWillRenderObject()
    {
        if (!isActiveAndEnabled || !Initialize()) return;
        
        UpdateQuadMesh();
        
        // set material properties if light source is displayed
        // ----------------------------------------------------
        if (m_DisplayLightSource)
        {
            mPropertyBlock.SetColor(_BaseColor, m_LightColor);
            mPropertyBlock.SetColor(_EmissionColor, m_LightColor * m_EmissiveIntensity);
            mPropertyBlock.SetTexture(_EmissionMap,
                m_HasTexture ? mAreaLightManager.m_Textures[m_TextureIndex] : mDummyEmissionMap);
            mMeshRenderer.SetPropertyBlock(mPropertyBlock);
        }
        
        // update shadow map if required
        // -----------------------------
        if (m_RenderShadow) UpdateShadow();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.white;
        
        // this is easy
        // ------------
        if (m_LightAngle == 0.0f)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0, 0, 0.5f * m_LightSize.z), m_LightSize);
            return;
        }
        
        // it goes into two parts
        // ----------------------
        // 1. draw outer frustum
        Matrix4x4 offsetMatrix = Matrix4x4.identity;
        offsetMatrix.SetColumn(3, new Vector4(0, 0, -mLightFrustumNear, 1));
        Gizmos.matrix = transform.localToWorldMatrix * offsetMatrix;
        Gizmos.DrawFrustum(Vector3.zero, m_LightAngle, mLightFrustumNear + m_LightSize.z, mLightFrustumNear, m_LightSize.x / m_LightSize.y);
        // 2. draw inner box
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.yellow;
        Bounds bounds = CalculateBounds();
        Gizmos.DrawWireCube(bounds.center, bounds.size);
    }

    private void OnDisable()
    {
        // remove from manager
        // -------------------
        if (mAddedToManager) AreaLightManager.Remove(this);
        mAddedToManager = false;
    }

    private bool Initialize()
    {
        // avoid re-initialization
        // -----------------------
        if (mInitialized) return true;
        
        // find AreaLightManager
        // ---------------------
        if (mAreaLightManager == null)
        {
            mAreaLightManager = FindObjectOfType<AreaLightManager>();
            if (mAreaLightManager == null)
            {
                Debug.LogError("AreaLight: AreaLightManager not found in the scene!");
                return false;
            }
        }
        
        // setup mesh filter with given mesh
        // ---------------------------------
        if (m_UnityDefaultQuad == null)
        {
            Debug.LogError("AreaLight: Please assign Unity's default quad to the defaultQuad field!");
            return false;
        }
        mMesh = Instantiate(m_UnityDefaultQuad);
        mMesh.hideFlags = HideFlags.HideAndDontSave;
        TryGetComponent(out MeshFilter meshFilter);
        meshFilter.sharedMesh = mMesh;
        
        // prepare mesh renderer related stuff
        // -----------------------------------
        TryGetComponent(out mMeshRenderer);
        // casting shadow is unnecessary for our area light
        mMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        
        // initialize shadow related stuff
        // -------------------------------
        if (m_RenderShadow) InitializeShadow();
        
        // create dummy emission map
        // -------------------------
        if (mDummyEmissionMap == null)
        {
            mDummyEmissionMap = new Texture2D(1, 1);
            mDummyEmissionMap.SetPixel(0, 0, Color.white);
            mDummyEmissionMap.Apply();
        }
        
        // all good
        // --------
        mInitialized = true;
        return true;
    }

    private void UpdateQuadMesh()
    {
        // update quad mesh
        // ----------------
        Vector2 quadMeshSize = m_DisplayLightSource && isActiveAndEnabled ? new Vector2(m_LightSize.x, m_LightSize.y) : Vector2.one * 0.0001f;
        if (quadMeshSize != mCurrentQuadSize)
        {
            float x = quadMeshSize.x * 0.5f;
            float y = quadMeshSize.y * 0.5f;
            const float z = -0.001f;
            mQuadVertices[0].Set( x, -y, z);
            mQuadVertices[1].Set(-x, -y, z);
            mQuadVertices[2].Set( x,  y, z);
            mQuadVertices[3].Set(-x,  y, z);
            mMesh.vertices = mQuadVertices;
            mCurrentQuadSize = quadMeshSize;
        }
        
        // update quad mesh bounds if necessary, based on light size and angle
        // -------------------------------------------------------------------
        if (mCurrentLightSize != m_LightSize || Math.Abs(m_LightAngle - mCurrentLightAngle) > 0.01f)
        {
            mMesh.bounds = CalculateBounds();
            mCurrentLightSize = m_LightSize;
            mCurrentLightAngle = m_LightAngle;
        }
    }

    private Bounds CalculateBounds()
    {
        // this is easy
        // ------------
        if (m_LightAngle == 0.0f) return new Bounds(Vector3.zero, m_LightSize);
        
        // hanzo: simple geometry
        // ----------------------
        float tanHalfFov = Mathf.Tan(m_LightAngle * 0.5f * Mathf.Deg2Rad);
        float z = m_LightSize.z;
        float y = (mLightFrustumNear + m_LightSize.z) * tanHalfFov * 2.0f;
        float x = m_LightSize.x / m_LightSize.y * y;
        return new Bounds(Vector3.forward * (m_LightSize.z * 0.5f), new Vector3(x, y, z));
    }
    
    public Vector4 GetLightColor(bool forFog = false)
    {
        Color colorWithIntensity = m_LightColor * m_LightIntensity;

        if (forFog) return colorWithIntensity;
        
        // for linear color space,
        // although this URP project should be in linear space by default
        // --------------------------------------------------------------
        if (QualitySettings.activeColorSpace == ColorSpace.Linear)
        {
            colorWithIntensity.r = Mathf.GammaToLinearSpace(colorWithIntensity.r);
            colorWithIntensity.g = Mathf.GammaToLinearSpace(colorWithIntensity.g);
            colorWithIntensity.b = Mathf.GammaToLinearSpace(colorWithIntensity.b);
        }
        
        // for gamma color space, without any conversion
        // ---------------------------------------------
        return new Vector4(colorWithIntensity.r, colorWithIntensity.g, colorWithIntensity.b, 1.0f);
    }

    public Matrix4x4 GetLightVertices()
    {
        const float z = 0.01f;
        Matrix4x4 vertices = new();
        for (int i = 0; i < 4; i++)
        {
            Vector3 position = new Vector3(m_LightSize.x * _Offsets[i, 0], m_LightSize.y * _Offsets[i, 1], z) * 0.5f;
            vertices.SetRow(i, transform.TransformPoint(position));
        }
        return vertices;
    }
    
    public Vector3 GetLightPosition()
    {
        Transform t = transform;

        if (m_LightAngle == 0.0f)
        {
            Vector3 dir = -t.forward;
            return new Vector4(dir.x, dir.y, dir.z, 0);
        }

        return t.position - mLightFrustumNear * t.forward;
    }
    
    public Matrix4x4 GetProjMatrix(bool linearZ = false)
    {
        Matrix4x4 matrix;

        if (m_LightAngle == 0.0f)
        {
            matrix = Matrix4x4.Ortho
            (
                -0.5f * m_LightSize.x, 0.5f * m_LightSize.x,
                -0.5f * m_LightSize.y, 0.5f * m_LightSize.y,
                0, -m_LightSize.z
            );
        }
        else
        {
            Matrix4x4 offsetMatrix = Matrix4x4.identity;
            offsetMatrix.SetColumn(3, new Vector4(0, 0, mLightFrustumNear, 1));
            
            if (linearZ)
            {
                matrix = PerspectiveLinearZ();
            }
            else
            {
                matrix = Matrix4x4.Perspective
                (
                    m_LightAngle, m_LightSize.x / m_LightSize.y,
                    mLightFrustumNear, mLightFrustumNear + m_LightSize.z
                );
                matrix *= Matrix4x4.Scale(new Vector3(1, 1, -1));
            }
            
            matrix *= offsetMatrix;
        }
        
        return matrix * transform.worldToLocalMatrix;
    }
    
    private Matrix4x4 PerspectiveLinearZ()
    {
        float rad = m_LightAngle * 0.5f * Mathf.Deg2Rad;
        float coTan = Mathf.Cos(rad) / Mathf.Sin(rad);
        float invDelta = 1.0f / (m_LightSize.z);
        float near = mLightFrustumNear;
        float far = near + m_LightSize.z;
        float aspect = m_LightSize.x / m_LightSize.y;

        Matrix4x4 matrix;

        matrix.m00 = coTan / aspect;
        matrix.m01 = 0.0f; 
        matrix.m02 = 0.0f; 
        matrix.m03 = 0.0f;
        
        matrix.m10 = 0.0f;           
        matrix.m11 = coTan; 
        matrix.m12 = 0.0f; 
        matrix.m13 = 0.0f;
        
        matrix.m20 = 0.0f;           
        matrix.m21 = 0.0f; 
        matrix.m22 = 2.0f * invDelta; 
        matrix.m23 = -(far + near) * invDelta;
        matrix.m30 = 0.0f;           
        matrix.m31 = 0.0f; 
        matrix.m32 = 1.0f; 
        matrix.m33 = 0.0f;

        return matrix;
    }
    
    // private variables
    // -----------------
    private bool mInitialized;
    private bool mAddedToManager;
    private AreaLightManager mAreaLightManager;
    
    // basic properties
    // ----------------
    [SerializeField, Space(5)] 
    private bool    m_DisplayLightSource = true;
    [Header("Basic Properties"), SerializeField, Min(0.001f), Space(5)]
    private Vector3 m_LightSize = new(1.0f, 1.0f, 1.0f);
    [SerializeField, Range(0.0f, 179.0f), Tooltip("Vertical Angle")]
    private float   m_LightAngle = 45.0f;
    [SerializeField] 
    private Color   m_LightColor;
    [SerializeField, Min(0.0f)]
    private float   m_LightIntensity = 1.0f;
    public float    m_EmissiveIntensity = 1.0f;
    private Vector3 mCurrentLightSize = Vector3.zero;
    private float   mCurrentLightAngle;
    
    // texture related properties
    // --------------------------
    [Header("Texture Related"), Space(5)] 
    public bool m_HasTexture;
    [SerializeField, Range(0, 3)]
    private int m_TextureIndex;
    public int TextureIndex
    {
        get
        {
            if (!m_HasTexture) return -1;
            return m_TextureIndex;
        }
        set
        {
            if (!m_HasTexture) return;
            m_TextureIndex = value;
        }
    }
    private Texture2D mDummyEmissionMap;
    
    // mesh related
    // ------------
    [SerializeField, Space(5)] 
    private Mesh      m_UnityDefaultQuad;
    private Mesh      mMesh;
    private Vector2   mCurrentQuadSize = Vector2.zero;
    private Vector3[] mQuadVertices = new Vector3[4];
    private float     mLightFrustumNear
    {
        get
        {
            if (m_LightAngle == 0.0f) return 0.0f;
            return m_LightSize.y * 0.5f / Mathf.Tan(m_LightAngle * 0.5f * Mathf.Deg2Rad);
        }
    }
    
    // meshRenderer related
    // --------------------
    private MeshRenderer mMeshRenderer;
    private MaterialPropertyBlock mPropertyBlock;
    
    // constants
    // ---------
    private static readonly float[,] _Offsets = {{1, 1}, {1, -1}, {-1, -1}, {-1, 1}};
    
    // cached shader property IDs
    // --------------------------
    private static readonly int _BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int _EmissionColor = Shader.PropertyToID("_EmissionColor");
    private static readonly int _EmissionMap = Shader.PropertyToID("_EmissionMap");
}
