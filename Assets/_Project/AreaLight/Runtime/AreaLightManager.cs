using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode, DisallowMultipleComponent]
public partial class AreaLightManager : MonoBehaviour
{
    private static AreaLightManager sInstance;
    private static AreaLightManager Instance
    {
        get
        {
            if (sInstance == null)
                sInstance = (AreaLightManager)FindObjectOfType(typeof(AreaLightManager));
            
            return sInstance;
        }
    }
    
    private HashSet<AreaLight> mContainer = new();

    public static HashSet<AreaLight> Get()
    {
        AreaLightManager instance = Instance;
        return instance == null ? new HashSet<AreaLight>() : instance.mContainer;
    }

    public static bool Add(AreaLight areaLight)
    {
        AreaLightManager instance = Instance;
        if (instance == null) return false;
        
        instance.mContainer.Add(areaLight);
        return true;
    }
    
    public static void Remove(AreaLight areaLight)
    {
        AreaLightManager instance = Instance;
        if (instance == null) return;
        
        instance.mContainer.Remove(areaLight);
    }
}
