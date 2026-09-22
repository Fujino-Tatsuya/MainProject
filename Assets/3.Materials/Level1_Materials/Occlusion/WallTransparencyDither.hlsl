#ifndef WALL_TRANSPARENCY_DITHER_INCLUDED
#define WALL_TRANSPARENCY_DITHER_INCLUDED

// 구역 벽 투명화의 프래그먼트 수식 전체.
//
// 그래프에서 노드를 여러 개 잇는 대신 여기에 모아 둔다. Shader Graph 는 SVN(Assets/50.Art)
// 이고 이 파일은 git 이라, 수식을 여기 두면 리뷰와 이력이 git 에 남는다.
//
// 그래프 쪽 노드는 4개면 된다 — Screen Position · Position(World) · Custom Function · Keyword.
// 화면 픽셀 변환과 월드 Y 추출을 여기서 하기 때문이다.
//
// 커스텀 전역 유니폼은 선언하지 않는다. baseY / fadeHeight / opacity 는 전부 인자로 받고
// WallTransparencyGroup 이 머티리얼 인스턴스에 써 준다 — 전역 드라이버가 필요 없다.
// (_ScreenParams 는 URP 가 이미 선언해 둔 내장 유니폼이라 그냥 쓴다.)

// interleaved gradient noise. 기존 WallOcclusionDither.shader 와 같은 식을 쓴다 —
// 두 시스템이 같은 무늬로 보여야 한다.
float WallTransparencyDitherThreshold(float2 screenPositionNormalized)
{
    float2 pixel = floor(screenPositionNormalized * _ScreenParams.xy);
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

// 높이 그라데이션과 구역 강도를 합쳐 최종 불투명도를 구한다.
//
// 🔴 방향: **아래가 사라지고 위가 남는다.** baseY 에서 알파 0, 위로 갈수록 선형으로 1 에
// 도달한다. 벽 한 층이 2.5 이므로 fadeHeight 5.0 이면
//   1층(baseY ~ +2.5)   알파 0   → 0.5
//   2층(+2.5 ~ +5.0)    알파 0.5 → 1.0
//   3·4층(+5.0 이상)    알파 1.0 (saturate) = 그대로 남는다
//
// opacity 는 _WallOcclusionOpacity(1 = 평소대로, 0 = 구역이 완전히 켜짐)다.
// 구역 밖이면 1 이 들어와 strength 가 0 이 되고, 결과는 높이와 무관하게 1 — 아무 변화 없다.
float WallTransparencyOpacity(
    float worldPositionY,
    float baseY,
    float fadeHeight,
    float opacity)
{
    float heightMask = saturate((worldPositionY - baseY) / max(fadeHeight, 1e-4));
    float strength = 1.0 - opacity;
    return lerp(1.0, heightMask, strength);
}

void WallTransparencyDither_float(
    float2 ScreenPosition,
    float3 WorldPosition,
    float BaseY,
    float FadeHeight,
    float Opacity,
    out float Alpha)
{
    float opacity = WallTransparencyOpacity(WorldPosition.y, BaseY, FadeHeight, Opacity);
    Alpha = opacity - WallTransparencyDitherThreshold(ScreenPosition);
}

void WallTransparencyDither_half(
    half2 ScreenPosition,
    half3 WorldPosition,
    half BaseY,
    half FadeHeight,
    half Opacity,
    out half Alpha)
{
    // half 그래프에서도 계산은 float 로 유지해야 기존 셰이더와 같은 무늬가 나온다.
    float opacity = WallTransparencyOpacity(
        (float)WorldPosition.y,
        (float)BaseY,
        (float)FadeHeight,
        (float)Opacity);
    Alpha = (half)(opacity - WallTransparencyDitherThreshold((float2)ScreenPosition));
}

#endif
