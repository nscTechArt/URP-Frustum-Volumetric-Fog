using System;
using UnityEngine;

public partial class AreaLightLUT
{
    private const int lutResolution = 64, lutMatrixDim = 3;
    
    public enum LUTType
    {
        TransformInvDisneyDiffuse, TransformInvDisneyGGX, AmpDiffAmpSpecFresnel
    }

    public static Texture2D LoadLUT(LUTType lutType)
    {
        return lutType switch
        {
            LUTType.TransformInvDisneyDiffuse => LoadLUT(lutTransformInvDisneyDiffuse),
            LUTType.TransformInvDisneyGGX => LoadLUT(lutTransformInvGGX),
            LUTType.AmpDiffAmpSpecFresnel => LoadLUT(lutAmplitudeDisneyDiffuse, lutAmplitudeGGX, lutFresnelGGX),
            _ => throw new ArgumentOutOfRangeException(nameof(lutType), lutType, null)
        };
    }
    
    private static Texture2D CreateLUT(TextureFormat format, Color[] pixels)
    {
        Texture2D tex = new (lutResolution, lutResolution, format, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp
            };
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Texture2D LoadLUT(double[,] transformInv)
    {
        const int count = lutResolution * lutResolution;
        Color[] pixels = new Color[count];
        
        // read transformInv
        for (int i = 0; i < count; i++)
        {
            // Only columns 0, 2, 4, 6 contain interesting values
            // (at lease in the case of GGX)
            pixels[i] = new Color(
                (float)transformInv[i, 0],
                (float)transformInv[i, 2],
                (float)transformInv[i, 4],
                (float)transformInv[i, 6]);
        }

        return CreateLUT(TextureFormat.RGBAHalf, pixels);
    }

    private static Texture2D LoadLUT(float[] scalar0, float[] scalar1, float[] scalar2)
    { 
        const int count = lutResolution * lutResolution;
        Color[] pixels = new Color[count];
        
        // read amplitude
        for (int i = 0; i < count; i++)
        {
            pixels[i] = new Color(scalar0[i], scalar1[i], scalar2[i], 0);
        }

        return CreateLUT(TextureFormat.RGBAHalf, pixels);

    }
    
}
