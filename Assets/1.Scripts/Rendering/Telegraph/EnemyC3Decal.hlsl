// ----------------------------------------------------------------------------
//  EnemyC3Decal.hlsl — 적 범위표시 C3 데칼 외곽선(할 일 57 셰이더 재개발 S2, PLAN-57-enemy-c3-shader §4.2)
//
//  EnemyC3Visuals.Decal(CPU 384×384 텍스처)이 그리던 부채꼴·띠·합집합 외곽선을 데칼 UV 에서 바로 그린다.
//  모양 식은 EnemyC3Sdf.hlsl, 색·마스크 합성은 TelegraphMask.hlsl(TelegraphShade) — 둘 다 그대로 쓴다.
//
//  쓰는 곳: SG_EnemyC3Decal(Custom Function → EnemyC3DecalShade) · 픽셀 비교 테스트 셰이더(Hidden/EnemyC3DecalCompare).
//  🔴 이 파일도 속성을 선언하지 않는다 — 그래프 블랙보드(또는 테스트 셰이더)가 선언한다.
//
//  _C3Shape   x = 모양(0 부채꼴 · 1 띠 · 2 합집합)  y = unit(디자인 단위 → m)  z = 반각(도)  w = 쐐기 표시(0/1)
//  _C3Extent  xy = 모양 크기(m, 외곽선 제외)  zw = 데칼 전체 크기(= Bake 의 sampleExtent, 외곽선 포함)
//  _C3Path    x = 쐐기 폭(띠 = 데칼 폭, 합집합 = 경로 폭)  y = 경로 길이  z = 끝 원 중심  w = 부채꼴·끝 원 반경
//  _C3Geom    x = 바깥 외곽선 폭  y = 안쪽 선 폭  z = 간격  w = 띠 그라데이션 시작(%)
//  _C3Fins    x = 쐐기 크기(%)  y = 간격  z = 시작
//  _C3Outline·_C3InnerLine·_C3Band·_C3Markers = 감마 직선 알파 색(SetVector — SetColor 는 선형으로 바뀐다)
// ----------------------------------------------------------------------------
#ifndef ENEMY_C3_DECAL_INCLUDED
#define ENEMY_C3_DECAL_INCLUDED

#include "Assets/1.Scripts/Rendering/Telegraph/EnemyC3Sdf.hlsl"
#include "Assets/1.Scripts/Rendering/Telegraph/TelegraphMask.hlsl"

// Bake.Decal 의 한 픽셀(감마, 직선 알파). uv = 데칼 UV(0~1, +V = 정면).
float4 EnemyC3DecalGamma(float2 uv)
{
    float2 p = (uv - 0.5) * _C3Extent.zw;
    // 미분은 분기 밖에서 한 번. 깊이가 끊기는 곳(물체 가장자리)에서 미분이 튀어 번지지 않게 상한을 둔다
    // — 비교 해상도(384)에서는 상한에 닿지 않는다.
    float aa = min(EnemyC3Aa(p), max(_C3Extent.z, _C3Extent.w) / 64.0);

    float kind = _C3Shape.x, unit = _C3Shape.y, halfAngle = _C3Shape.z;
    float2 extent = _C3Extent.xy;
    bool band = kind > 0.5 && kind < 1.5, junction = kind > 1.5;

    float d = band ? EnemyC3Box(p, extent * 0.5)
            : junction ? EnemyC3UnionSdf(p, extent.y, _C3Path.x, _C3Path.y, _C3Path.z, _C3Path.w)
            : EnemyC3SectorSdf(p, _C3Path.w, halfAngle);

    // 원(반각 179.9 이상 부채꼴)만 중심에서 바깥으로, 나머지는 경계에서 안쪽으로.
    float bandCover = !band && !junction && halfAngle >= 179.9
        ? EnemyC3BandCircle(p, extent.x * 0.5, _C3Geom.w)
        : EnemyC3BandLinear(d, unit, _C3Geom.w);

    // 쐐기 — 띠·합집합만. 원점을 모양 아래 끝(시작점)으로 옮긴다(Bake.Decal 의 marker 클로저).
    float marker = 0.0;
    if (_C3Shape.w > 0.5)
    {
        float2 q = float2(p.x, p.y + extent.y * 0.5);
        if ((!junction || abs(q.x) <= _C3Path.x * 0.5) && q.y < _C3Path.y)
            marker = EnemyC3Fins(q, _C3Path.x, _C3Path.y, unit, _C3Fins.x, _C3Fins.y, _C3Fins.z, _C3Geom.z, _C3Geom.y, aa);
    }

    EnemyC3Palette pal;
    pal.outline = _C3Outline; pal.innerLine = _C3InnerLine; pal.band = _C3Band;
    pal.fill = float4(0, 0, 0, 0); pal.markers = _C3Markers;
    return EnemyC3Boundary(d, aa, unit, bandCover, marker, false, _C3Geom.x, _C3Geom.y, _C3Geom.z, pal);
}

// 그래프 Custom Function 진입점. 출력 = 직선 알파 색(출력 색공간) — 마스크·_BaseColor 는 기존 데칼과 같은 TelegraphShade.
void EnemyC3DecalShade_float(float2 UV, float3 WorldPos, out float3 Color, out float Alpha)
{
    float4 c = EnemyC3DecalGamma(UV);
    // Bake 텍스처는 8비트였다 — 1/255 미만 알파 꼬리(띠 그라데이션 끝·바깥 AA)는 0 으로 잘렸다. 같게 자른다.
    // 화면 기여는 어차피 0.5/255 미만이라 보이는 차이는 없고, 기존과 같은 범위에서 끝난다.
    c.a = round(c.a * 255.0) / 255.0;
    TelegraphShade_float(float4(EnemyC3ToOutput(c.rgb), c.a), WorldPos, Color, Alpha);
}

void EnemyC3DecalShade_half(half2 UV, half3 WorldPos, out half3 Color, out half Alpha)
{
    float3 c; float a;
    EnemyC3DecalShade_float(UV, WorldPos, c, a);
    Color = c; Alpha = a;
}

#endif
