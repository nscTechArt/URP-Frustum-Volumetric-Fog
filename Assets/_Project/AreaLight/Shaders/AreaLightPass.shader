Shader "Hidden/AreaLightPass"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        ENDHLSL

        Pass
        {
            Name "Area Light Radiance"
            
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            float4 _CameraTopLeftCorner;
            float4 _CameraXExtent, _CameraYExtent;
            TEXTURE2D(_GBuffer0);
            TEXTURE2D(_GBuffer1);
            TEXTURE2D(_GBuffer2);
            TEXTURE2D(_TransformInv_Diffuse);
            TEXTURE2D(_TransformInv_Specular);
            TEXTURE2D(_AmpDiffAmpSpecFresnel);
            TEXTURE2D_ARRAY(_FilteredDiffuseTextureArray);
            TEXTURE2D_ARRAY(_FilteredSpecularTextureArray);
            
            #include "AreaLightPass.hlsl"
            ENDHLSL
        }
    }
}
