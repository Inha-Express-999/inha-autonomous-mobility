// Stylized Water 3 by Staggart Creations (http://staggart.xyz)
// COPYRIGHT PROTECTED UNDER THE UNITY ASSET STORE EULA (https://unity.com/legal/as-terms)
//    • Copying or referencing source code for the production of new asset store, or public, content is strictly prohibited!
//    • Uploading this file to a public repository will subject it to an automated DMCA takedown request.

#ifndef WATER_COMMON_INCLUDED
#define WATER_COMMON_INCLUDED

#define SW_VERSION 330

//As per the "Shader" section of the documentation, this is primarily used for synchronizing animations in networked applications.
float _CustomTime;
#define TIME_FRAG_INPUT _CustomTime > 0 ? _CustomTime : input.uv.z
#define TIME_VERTEX_OUTPUT _CustomTime > 0 ? _CustomTime : output.uv.z

#define TIME ((TIME_FRAG_INPUT * _Speed))
#define TIME_VERTEX ((TIME_VERTEX_OUTPUT * _Speed))

#define HORIZONTAL_DISPLACEMENT_SCALAR 0.01
#define UP_VECTOR float3(0,1,0)
#define RAD2DEGREE 57.29578

struct WaterSurface
{
	uint vFace;
	float3 positionWS;
	float3 viewDelta; //Un-normalized view direction, 
	float3 viewDir;

	//Normal from the base geometry, in world-space
	float3 vertexNormal;
	//Normal of geometry + waves
	float3 waveNormal;	
	half3x3 tangentToWorldMatrix;
	//Tangent-space normal
	float3 tangentNormal;
	//World-space normal, include geometry+waves+normal map
	float3 tangentWorldNormal;
	//The normal used for diffuse lighting.
	float3 diffuseNormal;
	//Per-pixel offset vector
	float4 refractionOffset;
	
	float3 albedo;
	float3 reflections;
	float3 caustics;
	float3 specular;
	half reflectionMask;
	half reflectionLighting;
	
	float3 offset;
	float slope;
	float waveCrest;
	
	float fog;
	float intersection;
	float foam;

	float alpha;
	float edgeFade;
	float shadowMask;
};

//Set through the public static C# parameter: StylizedWater3.WaterObject.PositionOffset
float3 _WaterPositionOffset;

float2 GetSourceUV(float2 uv, float2 wPos, float state) 
{
	#ifdef _RIVER
	//World-space tiling is useless in this case
	return uv;
	#endif
	
	float2 output =  lerp(uv, wPos - _WaterPositionOffset.xz, state);

	//Pixelize
	#ifdef PIXELIZE_UV
	output.x = (int)((output.x / 0.5) + 0.5) * 0.5;
	output.y = (int)((output.y / 0.5) + 0.5) * 0.5;
	#endif
	
	return output;
}

float4 GetVertexColor(float4 inputColor, float4 mask)
{
	return inputColor * mask;
}

float DepthDistance(float3 wPos, float3 viewPos, float3 normal)
{
	return length((wPos - viewPos) * normal);
}

float2 TileOffsetUV(float2 uv, float2 tiling, float2 time, float2 speed)
{
	return (uv.xy * tiling.xy) + (time.xy * speed.xy);
}

float4 PackedUV(float2 sourceUV, float2 tiling, float2 time, float speed, float subTiling, float subSpeed)
{
	float2 uv1 = TileOffsetUV(sourceUV, tiling, time, speed.xx * tiling);

	float2 tiling_uv2 = tiling * subTiling;
	float2 uv2 = TileOffsetUV(sourceUV, tiling_uv2, time, (speed.xx * subSpeed * tiling_uv2));
	
	return float4(uv1.xy, uv2.xy);
}

float DistanceFadeMask(float3 positionWS, float start, float end, float vFace = 1.0)
{
	float3 delta = GetCameraPositionWS().xyz - positionWS.xyz;

	#if UNDERWATER_ENABLED
	//Use vertical distance only for backfaces (underwater). This ensures tiling is reduced when moving deeper into the water, vertically
	delta.y = lerp(0, delta.y, vFace);
	#endif
	
	float pixelDist = length(delta);
	
	float fadeFactor = saturate((end - pixelDist) / (end - start));

	return fadeFactor;
}

//Edge feathering, used by SSR currently
float ScreenEdgeMask(float2 screenPos, float length)
{
	float lengthRcp = 1.0f/length;
	float2 t = Remap10(abs(screenPos.xy * 2.0 - 1.0), lengthRcp, lengthRcp);
	return Smoothstep01(t.x) * Smoothstep01(t.y);
}

struct SurfaceNormalData
{
	float3 geometryNormalWS;
	float3 pixelNormalWS;
	float lightingStrength;
	float mask;
};

float CalculateSlopeMask(float3 normalWS, float threshold, float falloff)
{
	const float surfaceAngle = acos(dot(normalWS, UP_VECTOR) * 2.0 - 1.0) * RAD2DEGREE;

	const float start = surfaceAngle - threshold;
	const float end = threshold - falloff;
	
	return saturate((end - start) / (end - threshold));
}

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/NormalReconstruction.hlsl"

half3 ReconstructWorldNormal(float2 screenPos)
{
	//NormalReconstruction library scales the screen position by the screen size, so counter this first
	screenPos.xy *= _ScreenSize.xy;
	
	half3 normalVS = ReconstructNormalTap3(screenPos.xy);

	return normalVS;
}
#endif