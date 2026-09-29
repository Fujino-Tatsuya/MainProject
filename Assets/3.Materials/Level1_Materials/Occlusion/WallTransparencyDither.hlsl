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

// ── 분리판 (2026-09-29) ─────────────────────────────────────────────────────
//
// 두 그라데이션을 **서로 독립된 프로퍼티**로 받는다.
//
//   ① 상시 하단  _WallOccBaseY / _WallOccFadeHeight        ← 머티리얼에 박힌 값. 그룹이 안 건드린다
//   ② 구역 상단  _WallOccZoneBaseY / _WallOccZoneFadeHeight ← WallTransparencyGroup 이 인스턴스에만 쓴다
//   ③ Opacity   _WallOcclusionOpacity                      ← **항상 1**
//
// 구역 효과는 불투명도가 아니라 **② 설정값의 보간**으로 낸다. 컴포넌트가 설정값1(구역 밖) ↔
// 설정값2(구역 안) 를 FadeIn/FadeOut 시간에 맞춰 Lerp 해서 ② 로 흘려보낸다.
//
// ③ 은 값으로는 죽어 있지만 지우지 않는다 — C# 이 `HasProperty(_WallOcclusionOpacity)` 로
// "투명화를 지원하는 머티리얼인가" 를 판별한다. 지우면 그룹이 모든 머티리얼을 건너뛴다.
//
// 합성은 단순 곱이다.
//   그룹 밖 머티 : bottom × 1     = 상시 하단만 (Zone 값이 0 이라 top = 1)
//   구역 밖      : bottom × top(설정값1)
//   구역 안      : bottom × top(설정값2)
//
// 구판 WallTransparencyDither_float/half 와 그 헬퍼는 그래프가 이 함수로 넘어온 것을 확인한 뒤
// 2026-09-29 에 제거했다.
float WallTransparencyHeightMask(float worldPositionY, float baseY, float fadeHeight)
{
    // fadeHeight 0 = 그라데이션 끔. 안 걸러내면 span 이 0 이라 baseY 를 경계로 칼같이 갈린다.
    if (abs(fadeHeight) < 1e-6)
        return 1.0;

    float mask = saturate((worldPositionY - baseY) / abs(fadeHeight));

    // 부호가 방향 — 양수: 아래가 투명 / 음수: 위가 투명
    return (fadeHeight < 0.0) ? (1.0 - mask) : mask;
}

// 🔴 인자 순서가 곧 배선이다 — Shader Graph 의 Custom Function 은 이름이 아니라 **순서**로
//    바인딩한다. 틀려도 컴파일은 되고 값만 조용히 뒤바뀐다.
//    그래프 노드의 슬롯 순서와 반드시 같아야 한다:
//      ScreenPosition · WorldPosition · BaseY · FadeHeight · ZoneBaseY · ZoneFadeHeight · Opacity
void WallTransparencyDitherSplit_float(
    float2 ScreenPosition,
    float3 WorldPosition,
    float BaseY,
    float FadeHeight,
    float ZoneBaseY,
    float ZoneFadeHeight,
    float Opacity,
    out float Alpha)
{
    float bottom = WallTransparencyHeightMask(WorldPosition.y, BaseY, FadeHeight);
    float top = WallTransparencyHeightMask(WorldPosition.y, ZoneBaseY, ZoneFadeHeight);
    float opacity = bottom * top * Opacity;
    Alpha = opacity - WallTransparencyDitherThreshold(ScreenPosition);
}

void WallTransparencyDitherSplit_half(
    half2 ScreenPosition,
    half3 WorldPosition,
    half BaseY,
    half FadeHeight,
    half ZoneBaseY,
    half ZoneFadeHeight,
    half Opacity,
    out half Alpha)
{
    // half 그래프에서도 계산은 float 로 유지해야 기존 셰이더와 같은 무늬가 나온다.
    float bottom = WallTransparencyHeightMask((float)WorldPosition.y, (float)BaseY, (float)FadeHeight);
    float top = WallTransparencyHeightMask((float)WorldPosition.y, (float)ZoneBaseY, (float)ZoneFadeHeight);
    float opacity = bottom * top * (float)Opacity;
    Alpha = (half)(opacity - WallTransparencyDitherThreshold((float2)ScreenPosition));
}

#endif
