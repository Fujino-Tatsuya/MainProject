// ----------------------------------------------------------------------------
//  SkillRangeDecalAlpha.hlsl — 플레이어 스킬 범위·대상 원 데칼(SkillRangeIndicator.shadergraph)의 알파 배율 한 곳
//
//  그래프: Alpha = 텍스처 알파 × SkillRangeDecalFade(…)
//  배율 = 바닥 블렌딩(할 일 221, DecalFloorBlend.hlsl — 스킬 전역 값) × 거리 감쇠(할 일 202, 재질 칸 _DecalFade)
//  바닥 블렌딩 전역 값이 없으면 1, _DecalFade 기본 (1, 0, 0) 이면 1 → 텍스처 알파 그대로(202·221 이전 화면).
//  TexAlpha 는 가장자리(진한 선·흰 테두리)·채움(안쪽 그라데이션)을 가르는 데만 쓴다.
//  🔴 그래프 블랙보드가 _DecalFade 를 선언한다(이 파일은 선언하지 않는다). 판 셰이더는 이 파일을 쓰지 않는다.
// ----------------------------------------------------------------------------
#ifndef SKILL_RANGE_DECAL_ALPHA_INCLUDED
#define SKILL_RANGE_DECAL_ALPHA_INCLUDED

#include "Assets/1.Scripts/Rendering/Telegraph/DecalDistanceFade.hlsl"
#include "Assets/1.Scripts/Rendering/Telegraph/DecalFloorBlend.hlsl"

void SkillRangeDecalFade_float(float3 WorldPos, float2 UV, float TexAlpha, out float Fade)
{
    Fade = SkillRangeDecalFloorBlend(UV, TexAlpha) * DecalDistanceFadeAt(WorldPos, _DecalFade.xyz);
}

void SkillRangeDecalFade_half(half3 WorldPos, half2 UV, half TexAlpha, out half Fade)
{
    float f;
    SkillRangeDecalFade_float(WorldPos, UV, TexAlpha, f);
    Fade = f;
}

#endif
