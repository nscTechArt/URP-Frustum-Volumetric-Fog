Shader "Hidden/VolumetricFog/Composite"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        ENDHLSL
        
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Fragment

            SAMPLER(sampler_BlitTexture);
            
            TEXTURE3D(_ScatterVolumeTexture);
            SAMPLER(sampler_ScatterVolumeTexture);
            float4 _ScreenTexelSize;
            float4 _VolumeResolution;
            float _CameraFarOverMaxFar;
            float _NearOverFarClip;
            float _VolumetricFogNearClip;
            float _VolumetricFogFarClip;
            
            half4 Fog(float linear01Depth, float2 screenuv)
            {
                float z = linear01Depth * _CameraFarOverMaxFar;
			    float ratio = (z - _NearOverFarClip) / (1 - _NearOverFarClip);
                if (ratio < 0.0) return half4(0, 0, 0, 1);

                float3 uvw = float3(screenuv.x, screenuv.y, ratio);
                return SAMPLE_TEXTURE3D(_ScatterVolumeTexture, sampler_ScatterVolumeTexture, uvw);
            }
            
            half4 Fragment(Varyings input) : SV_Target
            {
                float depth = SampleSceneDepth(input.texcoord);
                depth = Linear01Depth(depth, _ZBufferParams);
                float4 fog = Fog(depth, input.texcoord);

                float3 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, input.texcoord).rgb;
                color *= fog.a;
                color += fog.rgb;
                return half4(color, 1);
            }
                
            ENDHLSL
        }
    }
}
