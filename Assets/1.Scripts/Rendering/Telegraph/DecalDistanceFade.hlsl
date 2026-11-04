// ----------------------------------------------------------------------------
//  DecalDistanceFade.hlsl — 바닥 예고·표시 데칼의 거리 감쇠 그라데이션(할 일 202)
//
//  로컬 플레이어와 가까운 곳 = 원래 알파, 멀어질수록 옅어진다.
//    atten = 1 / (att0 + att1·d + att2·d²)   (d = 픽셀 월드 위치와 로컬 플레이어 사이 바닥 평면(xz) 거리, m)
//    분모는 1e-4 아래로 내려가지 않고, 결과는 0~1 로 자른다(가까운 곳이 원래보다 진해지지 않는다).
//    기본 계수 (1, 0, 0) → atten = 1 = 202 이전 화면과 같다.
//  C# 같은 식 = DecalDistanceFade.Evaluate(EditMode 테스트 기준).
//
//  🔴 이 파일은 재질 칸을 선언하지 않는다 — 쓰는 셰이더가 _DecalFade(x = att0, y = att1, z = att2)를 선언한다
//     (SG_EnemyC3Decal·SkillRangeIndicator·AimIndicator 블랙보드, Monster/EnemyC3Plate·Hidden/EnemyC3DecalCompare Properties).
//  전역 _DecalFadePlayerPos(xz = 로컬 플레이어 바닥 위치, w = 1 이면 유효)는 DecalDistanceFadeDriver 가 매 프레임 넣는다.
//  w = 0(로컬 플레이어 없음·에디터·테스트)이면 감쇠 1.
//  쓰는 곳: EnemyC3Decal.hlsl(C3 데칼 외곽선·채움) · EnemyC3Plate.shader(C3 바닥 판) · 플레이어 스킬 범위·조준 그래프 Custom Function.
// ----------------------------------------------------------------------------
#ifndef DECAL_DISTANCE_FADE_INCLUDED
#define DECAL_DISTANCE_FADE_INCLUDED

float4 _DecalFadePlayerPos;

float DecalDistanceFadeAtten(float d, float3 att)
{
    float denom = att.x + att.y * d + att.z * d * d;
    return saturate(1.0 / max(denom, 1.0e-4));
}

float DecalDistanceFadeAt(float3 positionWS, float3 att)
{
    if (_DecalFadePlayerPos.w < 0.5) return 1.0;
    float d = length(positionWS.xz - _DecalFadePlayerPos.xz);
    return DecalDistanceFadeAtten(d, att);
}

// 그래프 Custom Function 진입점(플레이어 스킬 범위·조준). 재질 칸 _DecalFade 를 읽는다.
void DecalDistanceFade_float(float3 WorldPos, out float Fade)
{
    Fade = DecalDistanceFadeAt(WorldPos, _DecalFade.xyz);
}

void DecalDistanceFade_half(half3 WorldPos, out half Fade)
{
    Fade = DecalDistanceFadeAt(WorldPos, _DecalFade.xyz);
}

#endif
