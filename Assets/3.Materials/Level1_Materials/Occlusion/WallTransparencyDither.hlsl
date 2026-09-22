#ifndef WALL_TRANSPARENCY_DITHER_INCLUDED
#define WALL_TRANSPARENCY_DITHER_INCLUDED

// 구역 벽 투명화의 프래그먼트 수식 전체.
//
// 그래프에서 노드를 여러 개 잇는 대신 여기에 모아 둔다. Shader Graph 는 SVN(Assets/50.Art)
// 이고 이 파일은 git 이라, 수식을 여기 두면 리뷰와 이력이 git 에 남는다.
//
// 전역 유니폼을 선언하지 않는다. 필요한 값은 전부 인자로 받는다 — 그래서 구 시스템의
// WallOcclusionDriver 같은 전역 드라이버가 필요 없고, 그룹이 자기 머티리얼 인스턴스에
// 값을 써 주면 끝난다.

// interleaved gradient noise. 기존 WallOcclusionDither.shader 와 같은 식을 쓴다 —
// 두 시스템이 같은 무늬로 보여야 한다.
float WallTransparencyDitherThreshold(float2 screenPixelPosition)
{
    float2 pixel = floor(screenPixelPosition);
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

// 높이 그라데이션과 구역 강도를 합쳐 최종 불투명도를 구한다.
//
// baseY 는 1층 벽 바닥의 월드 Y, fadeHeight 는 램프가 끝나는 높이차다.
// 벽 한 층이 2.5 이므로 fadeHeight 5.0 이면 1층 0~0.5 / 2층 0.5~1 의 선형 램프가 되고,
// 3층 이상은 saturate 로 완전히 사라진다.
//
// opacity 인자는 _WallOcclusionOpacity(1 = 평소대로, 0 = 구역이 완전히 켜짐)다.
// 구역 밖이면 1 이 들어와 strength 가 0 이 되고, 결과는 높이와 무관하게 1 — 아무 변화 없다.
float WallTransparencyOpacity(
    float worldPositionY,
    float baseY,
    float fadeHeight,
    float opacity)
{
    float fade = saturate((worldPositionY - baseY) / max(fadeHeight, 1e-4));
    float heightMask = 1.0 - fade;
    float strength = 1.0 - opacity;
    return lerp(1.0, heightMask, strength);
}

void WallTransparencyDither_float(
    float2 ScreenPixelPosition,
    float WorldPositionY,
    float BaseY,
    float FadeHeight,
    float Opacity,
    out float Alpha)
{
    float opacity = WallTransparencyOpacity(WorldPositionY, BaseY, FadeHeight, Opacity);
    Alpha = opacity - WallTransparencyDitherThreshold(ScreenPixelPosition);
}

void WallTransparencyDither_half(
    half2 ScreenPixelPosition,
    half WorldPositionY,
    half BaseY,
    half FadeHeight,
    half Opacity,
    out half Alpha)
{
    // half 그래프에서도 계산은 float 로 유지해야 기존 셰이더와 같은 무늬가 나온다.
    float opacity = WallTransparencyOpacity(
        (float)WorldPositionY,
        (float)BaseY,
        (float)FadeHeight,
        (float)Opacity);
    Alpha = (half)(opacity - WallTransparencyDitherThreshold((float2)ScreenPixelPosition));
}

#endif
