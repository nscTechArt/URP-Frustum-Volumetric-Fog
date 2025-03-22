using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways, DisallowMultipleComponent]
public class VolumetricFogLightManager : MonoBehaviour
{
    public static HashSet<VolumetricFogLight> Get()
    {
        VolumetricFogLightManager instance = Instance;
        return instance == null ? new HashSet<VolumetricFogLight>() : instance.mContainer;
    }

    public static bool Add(VolumetricFogLight volumetricFogLight)
    {
        VolumetricFogLightManager instance = Instance;
        if (instance == null) return false;
        
        instance.mContainer.Add(volumetricFogLight);
        return true;
    }
    
    public static void Remove(VolumetricFogLight volumetricFogLight)
    {
        VolumetricFogLightManager instance = Instance;
        if (instance == null) return;
        
        instance.mContainer.Remove(volumetricFogLight);
    }
    
    private static VolumetricFogLightManager sInstance;
    private static VolumetricFogLightManager Instance
    {
        get
        {
            if (sInstance == null)
                sInstance = (VolumetricFogLightManager)FindObjectOfType(typeof(VolumetricFogLightManager));
            
            return sInstance;
        }
    }
    
    private HashSet<VolumetricFogLight> mContainer = new();
}