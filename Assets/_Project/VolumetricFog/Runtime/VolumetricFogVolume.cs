using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class VolumetricFogVolume : VolumeComponent, IPostProcessComponent
{
    [Space]
    public BoolParameter m_Enable = new(false);
    [Space]
    public ClampedFloatParameter  m_NearClip = new(0.1f, 0.1f, 1000.0f);
    public ClampedFloatParameter  m_FarClip = new(100.0f, 0.1f, 1000.0f);
    [Space]
    public ClampedFloatParameter  m_Density = new(1f, 0.0f, 50.0f);
    public ClampedFloatParameter  m_Intensity = new(1.0f, 0.0f, 10.0f);
    [Space]
    public ClampedFloatParameter m_ConstantFog = new(10.0f, 0.0f, 50.0f);
    public ClampedFloatParameter  m_HeightFogAmount = new(0.0f, 0.0f, 50.0f);
    public ClampedFloatParameter  m_HeightFogExponent = new(0.0f, -10.0f, 10.0f);
    public ClampedFloatParameter  m_HeightFogOffset = new(0.0f, -10.0f, 10.0f);
    [Space]
    public ClampedFloatParameter  m_FogNoiseAmount = new(0.0f, 0.0f, 1.0f);
    public ClampedFloatParameter  m_FogNoiseScale = new(1.0f, 0.1f, 10.0f);
    public ClampedFloatParameter  m_FogNoiseSpeed = new(4.0f, 0.0f, 10.0f);
    public Vector3Parameter       m_FogNoiseDirection = new(new Vector3(-0.61f, -0.65f, -0.46f));
    [Space]
    public ClampedFloatParameter  m_Anisotropy = new(0.5f, 0.0f, 1.0f);
    public PhaseFunctionParameter m_PhaseFunction = new(PhaseFunction.Schlick);
    [Space]
    public ClampedFloatParameter  m_AmbientScale = new(0.1f, 0.0f, 1.0f);
    public ColorParameter         m_AmbientColor = new(Color.white);

    public float GetAnisotropy()
    {
        if (m_PhaseFunction == PhaseFunction.HenyeyGreenstein)
            return m_Anisotropy.value;
        else
        {
            float k = 1.55f * m_Anisotropy.value - 0.55f * Mathf.Pow(m_Anisotropy.value, 3);
            return k;
        }
    }
    
    public Vector4 GetNoiseWind()
    {
        return new Vector4(m_FogNoiseDirection.value.x, m_FogNoiseDirection.value.y, m_FogNoiseDirection.value.z, m_FogNoiseSpeed.value);
    }

    public Vector3 GetAmbient()
    {
        Color ambient = m_AmbientColor.value * m_AmbientScale.value;
        return new Vector3(ambient.r, ambient.g, ambient.b);
    }
    
    public bool IsActive() => m_Enable.value && m_Density.value > 0.0f && m_Intensity.value > 0.0f;
    public bool IsTileCompatible() => false;
}

public enum PhaseFunction
{
    Schlick = 0, HenyeyGreenstein = 1, CornetteShanks = 2, //Rayleigh = 3
}

[Serializable]
public class PhaseFunctionParameter : VolumeParameter<PhaseFunction>
{
    public PhaseFunctionParameter(PhaseFunction value, bool overrideState = false) : base(value, overrideState) {}
}