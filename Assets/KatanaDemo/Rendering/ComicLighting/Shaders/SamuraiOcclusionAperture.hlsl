#ifndef OC_DESIGN_SAMURAI_OCCLUSION_APERTURE_INCLUDED
#define OC_DESIGN_SAMURAI_OCCLUSION_APERTURE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

// Only the camera rendering a marked foreground group may read its fade.
float _SamuraiOcclusionEnabled;
// Logical feet position and the farthest player sample's view depth.
float4 _SamuraiOcclusionPlayer;
static const float SamuraiOcclusionThreshold = 0.0001;

float SamuraiOcclusionFade(float3 positionWS, float encodedFade)
{
    if (_SamuraiOcclusionEnabled <= 0.5) return 0.0;
    // Negative values identify light decoration throughout its fade transition.
    // Leaves may disappear fully without inheriting the solid floor protection.
    if (encodedFade < 0.0) return -encodedFade;

    float eyeDepth = -TransformWorldToView(positionWS).z;
    float foreground = 1.0 - smoothstep(_SamuraiOcclusionPlayer.w,
        _SamuraiOcclusionPlayer.w + 0.35, eyeDepth);
    // Keep the standing floor and lower structure solid, even when a merged
    // timber material also contains the wall that triggered this fade.
    float supportHeight = min(_SamuraiOcclusionPlayer.y + 0.04,
        _WorldSpaceCameraPos.y - 0.12);
    float aboveSupport = smoothstep(supportHeight, supportHeight + 0.16, positionWS.y);
    return max(0.0, encodedFade) * foreground * aboveSupport;
}

void SamuraiOcclusionClip(float3 positionWS, float fade)
{
    clip(SamuraiOcclusionThreshold - SamuraiOcclusionFade(positionWS, fade));
}

float SamuraiOcclusionForegroundAlpha(float3 positionWS, float4 positionCS, float fade)
{
    fade = SamuraiOcclusionFade(positionWS, fade);
    clip(fade - SamuraiOcclusionThreshold);

    // Opaque objects that stayed in front (including the player) still win.
    float sceneDepth = LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(positionCS)), _ZBufferParams);
    float foregroundDepth = -TransformWorldToView(positionWS).z;
    clip(sceneDepth - foregroundDepth - 0.002);

    float reveal = (fade - SamuraiOcclusionThreshold) / (1.0 - SamuraiOcclusionThreshold);
    float opacity = 1.0 - 0.85 * reveal;
    clip(opacity - SamuraiOcclusionThreshold);
    return opacity;
}

#endif
