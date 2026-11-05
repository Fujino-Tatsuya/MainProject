// ----------------------------------------------------------------------------
//  SkillRangeDecalAlpha.hlsl — 플레이어 스킬 범위·대상 원 데칼(SkillRangeIndicator.shadergraph)의 색·알파 배율 한 곳
//
//  그래프: BaseColor = SkillRangeDecalFade 의 Color · Alpha = 텍스처 알파 × SkillRangeDecalFade 의 Fade
//  Fade  = 바닥 블렌딩(할 일 221, DecalFloorBlend.hlsl — 스킬 전역 값) × 거리 감쇠(할 일 202, 재질 칸 _DecalFade)
//  Color = 텍스처 색 × _BaseColor, 가장자리(진한 선·흰 테두리)만 물들임(221 후속)
//  바닥 블렌딩 전역 값이 없으면 Color = ColorIn·Fade = 거리 감쇠, _DecalFade 기본 (1, 0, 0) 이면 1 → 202·221 이전 화면.
//  🔴 그래프 블랙보드가 _DecalFade 를 선언한다(이 파일은 선언하지 않는다). 판 셰이더는 이 파일을 쓰지 않는다.
// ----------------------------------------------------------------------------
#ifndef SKILL_RANGE_DECAL_ALPHA_INCLUDED
#define SKILL_RANGE_DECAL_ALPHA_INCLUDED

#include "Assets/1.Scripts/Rendering/Telegraph/DecalDistanceFade.hlsl"
#include "Assets/1.Scripts/Rendering/Telegraph/DecalFloorBlend.hlsl"

void SkillRangeDecalFade_float(float3 WorldPos, float2 UV, float TexAlpha, float4 ColorIn, out float Fade, out float3 Color)
{
    float4 b = SkillRangeDecalFloorBlend(UV, ColorIn.rgb, TexAlpha);
    Color = b.rgb;
    Fade = b.a * DecalDistanceFadeAt(WorldPos, _DecalFade.xyz);
}

void SkillRangeDecalFade_half(half3 WorldPos, half2 UV, half TexAlpha, half4 ColorIn, out half Fade, out half3 Color)
{
    float f; float3 c;
    SkillRangeDecalFade_float(WorldPos, UV, TexAlpha, ColorIn, f, c);
    Fade = f; Color = c;
}

#endif
