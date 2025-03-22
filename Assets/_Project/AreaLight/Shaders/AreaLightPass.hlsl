#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/Deferred.hlsl"

#include "AreaLightShadow.hlsl"
#include "AreaLight.hlsl"

float3 ReconstructionPositionWS(float2 uv)
{
	float rawDepth = SampleSceneDepth(uv);
    float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

	uv.y = 1.0 - uv.y;
	float zScale = linearDepth * rcp(_ProjectionParams.y);
	float3 positionVS = _CameraTopLeftCorner.xyz +
						_CameraXExtent.xyz * uv.x +
						_CameraYExtent.xyz * uv.y;
	positionVS *= zScale;
	
    return _WorldSpaceCameraPos + positionVS;
}

void FetchGBuffers(float2 screenUV, out float3 normalWS, out float3 baseColor, out float3 specColor, out float smoothness)
{
    // sample g-buffers
    // ----------------
    float4 gBuffer0 = SAMPLE_TEXTURE2D(_GBuffer0, sampler_LinearClamp, screenUV);
    float4 gBuffer1 = SAMPLE_TEXTURE2D(_GBuffer1, sampler_LinearClamp, screenUV);
    float4 gBuffer2 = SAMPLE_TEXTURE2D(_GBuffer2, sampler_LinearClamp, screenUV);
    // retrieve g-buffer data
    // ----------------------
    baseColor = gBuffer0.rgb;
    specColor = gBuffer1.rgb;
    normalWS = gBuffer2.rgb;
	// intentionally use gBuffer1.a as smoothness
    smoothness = gBuffer1.a;
    // unpack normal if necessary
    // --------------------------
    #if defined(_GBUFFER_NORMALS_OCT)
    float2 remappedOctNormalWS = Unpack888ToFloat2(normalWS);
    float2 octNormalWS = remappedOctNormalWS.xy * 2.0 - 1.0;
    normalWS = UnpackNormalOctQuadEncode(octNormalWS);
    #endif
}

float3 IntegrateEdge(float3 v1, float3 v2)
{
    float x = dot(v1, v2);
    float y = abs(x);

    float a = 0.8543985 + (0.4965155 + 0.0145206 * y) * y;
    float b = 3.4175940 + (4.1616724 + y) * y;
    float v = a / b;

    float theta_sintheta = (x > 0.0) ? v : 0.5 * rsqrt(max(1.0 - x * x, 1e-7)) - v;

    return cross(v1, v2) * theta_sintheta;
}

bool RayPlaneIntersect(float3 dir, float3 origin, float4 plane, out float t)
{
    t = -dot(plane, float4(origin, 1.0)) / dot(plane.xyz, dir);
    return t > 0.0;
}

float3 FetchLightFilteredTexture(float4x3 L, float3 dir, int index, bool spec)
{
	// area light plane basis
	float3 v1 = L[1] - L[0];
	float3 v2 = L[3] - L[0];
	float3 planeOrtho = cross(v1, v2);
	float planeAreaSquared = dot(planeOrtho, planeOrtho);

	float4 plane = float4(planeOrtho, -dot(planeOrtho, L[0]));
	float planeDist;
	RayPlaneIntersect(dir, 0, plane, planeDist);

	float3 P = planeDist * dir - L[0];

	// find tex coord of P
	float dotV1V2 = dot(v1, v2);
	float invDotV1V1 = 1.0 / dot(v1, v1);
	float3 v2What = v2 - v1 * dotV1V2 * invDotV1V1;
	float2 uv;
	uv.y = dot(v2What, P) / dot(v2What, v2What);
	uv.x = dot(v1, P) * invDotV1V1 - dotV1V2 * invDotV1V1 * uv.y;
	uv = float2(uv.y, 1 - uv.x);

	// lod
	float d = abs(planeDist) / pow(planeAreaSquared, 0.25);
	float lod = log(2048.0 * d) / log(3.0);
	lod = min(lod, 7.0);

	if (spec)
	{
		return SAMPLE_TEXTURE2D_ARRAY_LOD(_FilteredSpecularTextureArray, sampler_LinearClamp, uv, index, lod);
	}
	else
	{
		uv = uv / 2 + 0.25;
		return SAMPLE_TEXTURE2D_ARRAY_LOD(_FilteredDiffuseTextureArray, sampler_LinearClamp, uv, index, lod);
	}
}

float4 PolygonRadiance(float4x3 L, int index, bool spec)
{
	float4x3 LL = L;
	LL[0] = L[0];
	LL[1] = L[1];
	LL[2] = L[2];
	LL[3] = L[3];
	
	uint config = 0;
	if (L[0].z > 0) config += 1;
	if (L[1].z > 0) config += 2;
	if (L[2].z > 0) config += 4;
	if (L[3].z > 0) config += 8;
	
	float3 L4 = L[3];
	
	uint n = 0;
	switch(config)
	{
		case 0: // clip all
			break;
			
		case 1: // V1 clip V2 V3 V4
			n = 3;
			L[1] = -L[1].z * L[0] + L[0].z * L[1];
			L[2] = -L[3].z * L[0] + L[0].z * L[3];
			break;
			
		case 2: // V2 clip V1 V3 V4
			n = 3;
			L[0] = -L[0].z * L[1] + L[1].z * L[0];
			L[2] = -L[2].z * L[1] + L[1].z * L[2];
			break;
			
		case 3: // V1 V2 clip V3 V4
			n = 4;
			L[2] = -L[2].z * L[1] + L[1].z * L[2];
			L[3] = -L[3].z * L[0] + L[0].z * L[3];
			break;
			
		case 4: // V3 clip V1 V2 V4
			n = 3;	
			L[0] = -L[3].z * L[2] + L[2].z * L[3];
			L[1] = -L[1].z * L[2] + L[2].z * L[1];				
			break;
			
		case 5: // V1 V3 clip V2 V4: impossible
			break;
			
		case 6: // V2 V3 clip V1 V4
			n = 4;
			L[0] = -L[0].z * L[1] + L[1].z * L[0];
			L[3] = -L[3].z * L[2] + L[2].z * L[3];			
			break;
			
		case 7: // V1 V2 V3 clip V4
			n = 5;
			L4 = -L[3].z * L[0] + L[0].z * L[3];
			L[3] = -L[3].z * L[2] + L[2].z * L[3];
			break;
			
		case 8: // V4 clip V1 V2 V3
			n = 3;
			L[0] = -L[0].z * L[3] + L[3].z * L[0];
			L[1] = -L[2].z * L[3] + L[3].z * L[2];
			L[2] =  L[3];
			break;
			
		case 9: // V1 V4 clip V2 V3
			n = 4;
			L[1] = -L[1].z * L[0] + L[0].z * L[1];
			L[2] = -L[2].z * L[3] + L[3].z * L[2];
			break;
			
		case 10: // V2 V4 clip V1 V3: impossible
			break;
			
		case 11: // V1 V2 V4 clip V3
			n = 5;
			L[3] = -L[2].z * L[3] + L[3].z * L[2];
			L[2] = -L[2].z * L[1] + L[1].z * L[2];			
			break;
			
		case 12: // V3 V4 clip V1 V2
			n = 4;
			L[1] = -L[1].z * L[2] + L[2].z * L[1];
			L[0] = -L[0].z * L[3] + L[3].z * L[0];
			break;
			
		case 13: // V1 V3 V4 clip V2
			n = 5;
			L[3] = L[2];
			L[2] = -L[1].z * L[2] + L[2].z * L[1];
			L[1] = -L[1].z * L[0] + L[0].z * L[1];
			break;
			
		case 14: // V2 V3 V4 clip V1
			n = 5;
			L4 = -L[0].z * L[3] + L[3].z * L[0];
			L[0] = -L[0].z * L[1] + L[1].z * L[0];
			break;
			
		case 15: // V1 V2 V3 V4
			n = 4;
			break;
	}

	if (n == 0) return 0;

	// normalize
	L[0] = normalize(L[0]);
	L[1] = normalize(L[1]);
	L[2] = normalize(L[2]);
	if(n == 3)
		L[3] = L[0];
	else
	{
		L[3] = normalize(L[3]);
		if (n == 4) L4 = L[0];
		else  L4 = normalize(L4);
	}
	
	// integrate
	float3 sum = 0;
	sum += IntegrateEdge(L[0], L[1]);
	sum += IntegrateEdge(L[1], L[2]);
	sum += IntegrateEdge(L[2], L[3]);
	if(n >= 4)	
		sum += IntegrateEdge(L[3], L4);
	if(n == 5)
		sum += IntegrateEdge(L4, L[0]);

	float3 fetchDir = normalize(sum);

	float3 lightSourceColor;
	if (_AreaLightTextureIndices[index] == -1)
		lightSourceColor = 1;
	else
		lightSourceColor = FetchLightFilteredTexture(LL, fetchDir, _AreaLightTextureIndices[index], spec).rgb;
	
	return float4(max(0, sum.z * 0.15915) * lightSourceColor, fetchDir.z);
	
	// sum.z *= 0.15915; // 1/2pi
	// sum.z = max(0, sum.z);
	// return float4(lightSourceColor * sum.z, fetchDir.z);
}

float4 TransformedPolygonRadiance(float4x3 L, float2 uv, TEXTURE2D(transformInv), int index, float amplitude, bool spec = false)
{
    float3x3 invMatrix = 0;
    invMatrix._m22 = 1;
    invMatrix._m00_m02_m11_m20 = SAMPLE_TEXTURE2D(transformInv, sampler_LinearClamp, uv);

    float4x3 transformedL = mul(L, invMatrix);
    
    return PolygonRadiance(transformedL, index, spec) * float4(amplitude.xxx, 1);
}

float3 CalculateLighting(float3 positionWS, float3 normal, float3 baseColor, float3 specColor, float smoothness)
{
	// build basis
	// -----------
    float3 viewDir = normalize(_WorldSpaceCameraPos - positionWS);
    float3x3 basis;
    basis[0] = normalize(viewDir - normal * dot(viewDir, normal));
    basis[1] = normalize(cross(normal, basis[0]));
    basis[2] = normal;

    float theta = acos(dot(viewDir, normal));
    float roughness = 1 - smoothness;
	// TODO: 0.64 or 0.98
    float2 uv = float2(lerp(0.09, 0.98, roughness), theta / 1.57);
    float3 ampDiffAmpSpecFresnel = SAMPLE_TEXTURE2D(_AmpDiffAmpSpecFresnel, sampler_LinearClamp, uv).rgb;
    float fresnel = specColor + (1.0 - specColor) * ampDiffAmpSpecFresnel.z;

    float3 color = 0.0;
	
    for (int index = 0; index < GetAreaLightCount(); index++)
    {
    	float shadow;
    	if (_AreaLightRenderShadow[index] == 0)
    		shadow = 1;
    	else
    		shadow = 1 - Shadow(positionWS);
    	
    	float3 result = 0;
    	
        float4x3 L = GetAreaLight(index).vertices - float4x3(positionWS, positionWS, positionWS, positionWS);
        L = mul(L, transpose(basis));
    	
        float4 diffuse = TransformedPolygonRadiance(L, uv, _TransformInv_Diffuse, index, ampDiffAmpSpecFresnel.x);
    	float4 specular = TransformedPolygonRadiance(L, uv, _TransformInv_Specular, index, ampDiffAmpSpecFresnel.y, true);

    	result += diffuse * baseColor;
    	result += specular * fresnel * 3.14159265359f;
    	result *= GetAreaLight(index).color.rgb * shadow;
    	color += result;
    }

	return color;
}

float4 Frag(Varyings input) : SV_Target
{
	// retrieve screen-space UV
	// ------------------------
	float2 uv = input.positionCS.xy / _ScaledScreenParams.xy;
	
    // prepare necessary data
    // ----------------------
    float3 positionWS = ReconstructionPositionWS(uv);
    float3 normalWS, baseColor, specColor;
    float smoothness;
    FetchGBuffers(uv, normalWS, baseColor, specColor, smoothness);

    // do the calculation
    // ------------------
	float3 color = CalculateLighting(positionWS, normalWS, baseColor, specColor, smoothness);
	float3 cameraColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).rgb;
	
    return float4(color + cameraColor, 1.0);
}