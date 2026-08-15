#ifndef VEYTRACE_WALL_OCCLUSION_CLIP_INCLUDED
#define VEYTRACE_WALL_OCCLUSION_CLIP_INCLUDED

float4 _WallOccCapsuleA;
float4 _WallOccCapsuleB;
float4 _WallOccMask;
float4 _WallOccCameraWS;
float4 _WallOccCameraForwardWS;
float4 _WallOccDepth;
float4x4 _WallOccViewProjection;
float4 _WallOccScreenRect;

float WallOcclusionDistanceToSegment(float2 samplePoint, float2 a, float2 b)
{
    float2 segment = b - a;
    float denominator = max(dot(segment, segment), 1e-4);
    float t = saturate(dot(samplePoint - a, segment) / denominator);
    return distance(samplePoint, a + segment * t);
}

float WallOcclusionOpacity(float3 positionWS, float strength, out float2 gameplayPixel)
{
    gameplayPixel = 0.0;
    if (_WallOccMask.z < 0.5 || strength <= 0.0001)
        return 1.0;

    float4 gameplayClip = mul(_WallOccViewProjection, float4(positionWS, 1.0));
    if (gameplayClip.w <= 1e-4)
        return 1.0;

    float2 viewport = gameplayClip.xy / gameplayClip.w * 0.5 + 0.5;
    gameplayPixel = _WallOccScreenRect.xy + viewport * _WallOccScreenRect.zw;
    float distancePixels = WallOcclusionDistanceToSegment(
        gameplayPixel,
        _WallOccCapsuleA.xy,
        _WallOccCapsuleB.xy);
    float opacity = smoothstep(
        _WallOccMask.x,
        _WallOccMask.x + max(_WallOccMask.y, 1.0),
        distancePixels);

    float viewDepth = dot(
        positionWS - _WallOccCameraWS.xyz,
        _WallOccCameraForwardWS.xyz);
    float behind = saturate(
        (viewDepth - _WallOccDepth.x) / max(_WallOccDepth.y, 1e-4));
    opacity = lerp(opacity, 1.0, behind);
    return lerp(1.0, opacity, saturate(strength));
}

void WallOcclusionClipMargin_float(
    float3 PositionWS,
    float BaseAlpha,
    float BaseThreshold,
    float Strength,
    out float Margin)
{
    float2 gameplayPixel;
    float opacity = WallOcclusionOpacity(PositionWS, Strength, gameplayPixel);
    float2 pixel = floor(gameplayPixel);
    float ditherThreshold =
        frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));

    float baseMargin = BaseAlpha - BaseThreshold;
    // clip(0) survives in HLSL, so keep the capsule core strictly negative.
    float occlusionMargin = opacity - max(ditherThreshold, 1e-5);
    Margin = min(baseMargin, occlusionMargin);
}

void WallOcclusionClipMargin_half(
    half3 PositionWS,
    half BaseAlpha,
    half BaseThreshold,
    half Strength,
    out half Margin)
{
    float margin;
    WallOcclusionClipMargin_float(
        PositionWS,
        BaseAlpha,
        BaseThreshold,
        Strength,
        margin);
    Margin = (half)margin;
}

#endif
