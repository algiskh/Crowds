#ifndef CROWDS_SEE_THROUGH_INCLUDED
#define CROWDS_SEE_THROUGH_INCLUDED

// "See-through" hole for occluders between the camera and the player (Crowds/Lit See-Through).
// Globals are set every frame by ECS.SeeThroughSystem (Shader.SetGlobalVector), so the cost is a handful of ALU
// per fragment — no extra passes, no transparency sorting, shadows untouched. With strength 0 (menu, edit mode,
// no player) the shader is plain Lit.
//
// _CrowdsSeeThrough       xy = player position in normalized screen UV, z = radius (fraction of screen height),
//                         w = player eye depth (distance along the camera forward)
// _CrowdsSeeThroughParams x = edge softness (share of the radius), y = depth margin (world units in front of the
//                         player before the cut starts), z = depth fade length (world units), w = strength 0..1
// _CrowdsSeeThroughHeight x = world Y where the cut starts (player feet + offset), y = height fade length
//                         — keeps the floor in front of the player intact, so floors may share the shader.
float4 _CrowdsSeeThrough;
float4 _CrowdsSeeThroughParams;
float4 _CrowdsSeeThroughHeight;

float CrowdsEyeDepth(float deviceDepth)
{
    if (unity_OrthoParams.w == 0)
        return LinearEyeDepth(deviceDepth, _ZBufferParams);
    #if UNITY_REVERSED_Z
        deviceDepth = 1.0 - deviceDepth;
    #endif
    return lerp(_ProjectionParams.y, _ProjectionParams.z, deviceDepth);
}

// Discards the fragment if it lies inside the circle around the player, is closer to the camera than the player
// and is above the player's feet. The rim is dithered (interleaved gradient noise in screen pixels), which reads
// as a soft round edge while the surface stays opaque.
void CrowdsSeeThroughClip(float4 positionCS)
{
    float strength = _CrowdsSeeThroughParams.w;
    if (strength <= 0.0)
        return;

    float2 uv = GetNormalizedScreenSpaceUV(positionCS);
    float2 screen = GetScaledScreenParams().xy;
    float2 delta = (uv - _CrowdsSeeThrough.xy) * float2(screen.x / screen.y, 1.0);
    float r = length(delta) / max(_CrowdsSeeThrough.z, 1e-4);
    float radial = saturate((1.0 - r) / max(_CrowdsSeeThroughParams.x, 1e-3));
    if (radial <= 0.0)
        return;

    float inFront = _CrowdsSeeThrough.w - CrowdsEyeDepth(positionCS.z) - _CrowdsSeeThroughParams.y;
    float depthFade = saturate(inFront / max(_CrowdsSeeThroughParams.z, 1e-3));

    float3 positionWS = ComputeWorldSpacePosition(uv, positionCS.z, UNITY_MATRIX_I_VP);
    float heightFade = saturate((positionWS.y - _CrowdsSeeThroughHeight.x) / max(_CrowdsSeeThroughHeight.y, 1e-3));

    float cut = radial * depthFade * heightFade * strength;
    if (cut <= 0.0)
        return;
    float noise = frac(52.9829189 * frac(dot(positionCS.xy, float2(0.06711056, 0.00583715))));
    clip(noise - cut * 1.0001);
}

#endif
