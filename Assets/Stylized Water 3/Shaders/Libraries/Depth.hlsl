#ifndef WATER_DEPTH_INCLUDED
#define WATER_DEPTH_INCLUDED

#define ORTHOGRAPHIC_SUPPORT

#if defined(USING_STEREO_MATRICES)
//Will never be used in VR, saves a per-fragment matrix multiplication
#undef ORTHOGRAPHIC_SUPPORT
#endif

#if !SHADERGRAPH_PREVIEW
TEXTURE2D_X(_WaterDepthTexture);
uniform bool _WaterDepthTextureAvailable;

struct SceneDepth
{
    float raw;
    float linear01;
    float eye;
};

#define FAR_CLIP _ProjectionParams.z
#define NEAR_CLIP _ProjectionParams.y
//Scale linear values to the clipping planes for orthographic projection (unity_OrthoParams.w = 1 = orthographic)
#define DEPTH_SCALAR lerp(1.0, FAR_CLIP - NEAR_CLIP, unity_OrthoParams.w)

//Linear depth difference between scene and current (transparent) geometry pixel
float SurfaceDepth(SceneDepth depth, float4 positionCS)
{
    const float sceneDepth = (unity_OrthoParams.w == 0) ? depth.eye : LinearDepthToEyeDepth(depth.raw);
    const float clipSpaceDepth = (unity_OrthoParams.w == 0) ? LinearEyeDepth(positionCS.z, _ZBufferParams) : LinearDepthToEyeDepth(positionCS.z / positionCS.w);

    return sceneDepth - clipSpaceDepth;
}

#if UNITY_REVERSED_Z
#define DEPTH_DEFAULT_VALUE 1.0
#define DEPTH_OP min
#else
#define DEPTH_DEFAULT_VALUE 0.0
#define DEPTH_OP max
#endif

//Return depth based on the used technique (buffer, vertex color, baked texture)
SceneDepth SampleDepth(float4 screenPos)
{
    SceneDepth depth = (SceneDepth)0;
    depth.raw = DEPTH_DEFAULT_VALUE;
    depth.eye = DEPTH_DEFAULT_VALUE;
    depth.linear01 = DEPTH_DEFAULT_VALUE;
	
    #if !_DISABLE_DEPTH_TEX
    screenPos.xyz /= screenPos.w;

    depth.raw = SampleSceneDepth(screenPos.xy);
    
    //Untested! Samples the environment's depth and combines the two
    #if defined(META_XR)
    float passThroughDepth = SampleEnvironmentDepth(screenPos.xy);
    depth.raw = DEPTH_OP(depth.raw, passThroughDepth);
    #endif
    
    depth.eye = LinearEyeDepth(depth.raw, _ZBufferParams);
    depth.linear01 = Linear01Depth(depth.raw, _ZBufferParams) * DEPTH_SCALAR;
    #endif

    return depth;
}

bool SampleWaterSurfaceDepth(float2 screenPos, out float depth)
{
    if (_WaterDepthTextureAvailable == 0) 
    {
        depth = 0;
        return false;
    }
	
    depth = SAMPLE_TEXTURE2D_X_LOD(_WaterDepthTexture, sampler_PointClamp, screenPos, 0).r;
	
    //Void, no surface found at pixel
    return depth > 0;
}

//Reconstruct world-space position from depth.
float3 ReconstructWorldPosition(float4 screenPos, float3 viewDir, SceneDepth sceneDepth)
{
    #if UNITY_REVERSED_Z
    real rawDepth = sceneDepth.raw;
    #else
    // Adjust z to match NDC for OpenGL
    real rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, sceneDepth.raw);
    #endif

    //return ComputeWorldSpacePosition(screenPos.xy / screenPos.w, rawDepth, UNITY_MATRIX_I_VP);

    #if defined(ORTHOGRAPHIC_SUPPORT)
    //View to world position
    float4 viewPos = float4((screenPos.xy/screenPos.w) * 2.0 - 1.0, rawDepth, 1.0);
    float4x4 viewToWorld = UNITY_MATRIX_I_VP;
    #if UNITY_REVERSED_Z //Wrecked since 7.3.1 "fix" and causes warping, invert second row https://issuetracker.unity3d.com/issues/shadergraph-inverse-view-projection-transformation-matrix-is-not-the-inverse-of-view-projection-transformation-matrix
    //Commit https://github.com/Unity-Technologies/Graphics/pull/374/files
    viewToWorld._12_22_32_42 = -viewToWorld._12_22_32_42;              
    #endif
    float4 viewWorld = mul(viewToWorld, viewPos);
    float3 viewWorldPos = viewWorld.xyz / viewWorld.w;
    #endif

    //Projection to world position
    float3 camPos = GetCameraPositionWS().xyz;
    float3 worldPos = sceneDepth.eye * (viewDir/screenPos.w) - camPos;
    float3 perspWorldPos = -worldPos;

    #if defined(ORTHOGRAPHIC_SUPPORT)
    return lerp(perspWorldPos, viewWorldPos, unity_OrthoParams.w);
    #else
    return perspWorldPos;
    #endif
}

float3 ReconstructWaterPosition(float2 screenPos)
{
    float waterDepth = 0;
    if (!SampleWaterSurfaceDepth(screenPos, waterDepth))
    {
        //No water surface for this pixel, early out
        return 0;
    }
    
    // Convert to the raw device depth Unity expects
    #if UNITY_REVERSED_Z
    // In reversed-Z, the sampled value is already in the right range
    float rawDepth = waterDepth;
    #else
    // For non-reversed Z, remap to match Unity’s NDC convention
    float rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, waterDepth);
    #endif
    
    float3 positionWS = ComputeWorldSpacePosition(screenPos, rawDepth, UNITY_MATRIX_I_VP);

    return positionWS;
}
#endif

//Shader Graph
void ReconstructWaterPosition_float(float2 screenPos, out float3 positionWS)
{
    #if SHADERGRAPH_PREVIEW
    positionWS = frac(1000);
    #else
    positionWS = ReconstructWaterPosition(screenPos);
    #endif
}
#endif
