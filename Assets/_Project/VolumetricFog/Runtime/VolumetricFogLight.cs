using System;
using UnityEngine;

[ExecuteInEditMode]
public class VolumetricFogLight : MonoBehaviour
{
    private void OnEnable()
    {
        if (!mAdded)
        {
            mAdded = VolumetricFogLightManager.Add(this);
        }
    }
    
    private void OnDisable()
    {
        if (mAdded)
        {
            VolumetricFogLightManager.Remove(this);
        }
        mAdded = false;
    }
    
    private void Initialize()
    {
        // Simply retrieve the Light component and determine the type of light
        // -------------------------------------------------------------------
        if (mInitialized) return;
        // check Unity's default light types
        // ---------------------------------
        if (TryGetComponent(out mLight))
        {
            switch (mLight.type)
            {
                // currently only Point and Spotlights are supported in Volumetric Fog
                case UnityEngine.LightType.Point : mLightType = LightType.Point; break;
                case UnityEngine.LightType.Spot: mLightType = LightType.Spot; break;
                // other light types are not supported
                case UnityEngine.LightType.Directional:
                case UnityEngine.LightType.Area:
                case UnityEngine.LightType.Disc:
                default: mLightType = LightType.None; break;
            }
        }
        // check custom light types, e.g. AreaLight
        // ----------------------------------------
        else if (TryGetComponent(out mAreaLight))
        {
            mLightType = LightType.Area;
        }
        mInitialized = true;
    }
    
    // public variables
    // ----------------
    [Min(0.0f)] public float m_IntensityMultiplier = 1.0f;
    [Min(0.0f)] public float m_RangeMultiplier = 1.0f;
    
    // public getters
    // --------------
    public LightType lightType { get { Initialize(); return mLightType; } }
    public new Light light { get { Initialize(); return mLight; } }
    public AreaLight areaLight { get { Initialize(); return mAreaLight; } }
    public bool IsEnabled
    {
        // Check if this FogLight is enabled
        // ---------------------------------
        get
        {
            // FogLight will be enabled if and only if when
            // 1. this GameObject is active and enabled
            if (!isActiveAndEnabled) return false;
            // 2. the Light component is enabled
            Initialize();
            switch (mLightType)
            {
                case LightType.Point:
                case LightType.Spot:
                    return mLight.enabled;
                case LightType.Area:
                    return mAreaLight.enabled;
                case LightType.None: break;
                default: throw new ArgumentOutOfRangeException();
            }

            return false;
        }
    }
    
    // protected variables
    // -------------------
    private LightType mLightType = LightType.None;
    private Light     mLight;
    private AreaLight mAreaLight;
    private bool      mInitialized;
    private bool mAdded;
    
    // enums
    // -----
    public enum LightType
    {
        None, Point, Spot, Area,
    }
    
}
