// ----------------------------------------------------------------------------
//  DecalFloorBlend.hlsl — 스킬 범위 데칼·몬스터 공격 예고의 바닥 블렌딩(할 일 221)
//
//  1차: 안쪽(채움) 알파를 공용 배율로 낮춰 바닥 무늬·명암이 비치게.
//  후속(11-05 "외곽선이 너무 붕 뜸"): 외곽선(바깥 외곽선·안쪽 선)도 바닥에 물들게 —
//    외곽선 알파 배율 + 물들임(0 = 일반 섞기, 1 = 완전 물들임) + 소프트(외곽선 안·밖 가장자리 둘 다).
//  C# 같은 식 = DecalFloorBlend.cs(EditMode 테스트 기준).
//
//  🔴 투영 데칼(DBuffer)은 바닥 알베도와 lerp(덮기)만 된다 — 셰이더가 바닥 색을 읽을 수 없다. 그래서 물들임은
//     "기준 바닥 밝기(DFB_REF_FLOOR_*)에서 목표 섞기와 같아지는 (색, 알파)" 로 근사한다. 투명 판도 같은 근사를 쓴다(같은 느낌).
//     어느 근사든 알파가 (1 − DFB_STAIN_KEEP) 배로 줄어 바닥 무늬·명암이 그만큼 남는다.
//    모드 0 밝기 보존(근사) — 색의 밝기를 기준 바닥 밝기 쪽으로 절반 당긴다(색조는 유지). 흰 선은 회색 쪽, 어두운 바닥에서도 밝게 남는다.
//    모드 1 곱하기(근사)    — 기준 바닥에서 바닥 × 색 과 같게. 흰 선은 기준 바닥에서 거의 사라진다(곱하기의 성질).
//
//  전역 값 4개(DecalFloorBlendDriver 가 DecalFloorBlendSettings 에서 매 프레임 넣는다):
//    _DecalFloorBlendSkill / _DecalFloorBlendTelegraph
//        x = 채움 알파 배율(1 = 그대로)  y = 소프트(0~1, 외곽선 폭에 대한 비율, 0 = 그대로)  z = 판 전용 채움 추가 배율  w = 1 유효
//    _DecalFloorBlendSkillEdge / _DecalFloorBlendTelegraphEdge
//        x = 외곽선 알파 배율(1 = 그대로)  y = 물들임(0 = 그대로)  z = 모드(0 밝기 보존 · 1 곱하기)  w = 1 유효
//  🔴 w < 0.5(값을 넣은 적 없음 — 전역 기본 0·에디터 Play 밖·테스트)이면 221 이전 화면과 같다.
//  이 파일은 재질 칸을 선언하지 않는다(전역만).
//  쓰는 곳: EnemyC3Decal.hlsl · EnemyC3Plate.shader · TelegraphQuad.shader · SkillLineIndicator.shader ·
//           SkillRangeIndicator.shadergraph(Custom Function SkillRangeDecalFade, SkillRangeDecalAlpha.hlsl).
// ----------------------------------------------------------------------------
#ifndef DECAL_FLOOR_BLEND_INCLUDED
#define DECAL_FLOOR_BLEND_INCLUDED

float4 _DecalFloorBlendSkill;
float4 _DecalFloorBlendTelegraph;
float4 _DecalFloorBlendSkillEdge;
float4 _DecalFloorBlendTelegraphEdge;

// 물들임 근사의 기준 바닥 밝기 — 맵 바닥(Generic_01_A)이 중간보다 어두운 편이라 감마 0.3(선형 ≈ 0.073).
#define DFB_REF_FLOOR_GAMMA 0.3
#define DFB_REF_FLOOR_LINEAR 0.0732
// 완전 물들임(1)에서 남기는 바닥 비율 — 알파 × (1 − 이 값).
#define DFB_STAIN_KEEP 0.5

// 전역 값 → (채움 배율, 소프트, 판 추가 배율). 값이 없으면 그대로(1, 0, 1).
float3 DecalFloorBlendResolve(float4 g)
{
    return g.w < 0.5 ? float3(1.0, 0.0, 1.0) : saturate(g.xyz);
}

// 외곽선 전역 값 → (외곽선 알파 배율, 물들임, 모드). 값이 없으면 그대로(1, 0, 0).
float3 DecalFloorBlendResolveEdge(float4 g)
{
    return g.w < 0.5 ? float3(1.0, 0.0, 0.0) : float3(saturate(g.x), saturate(g.y), g.z);
}

// 안쪽 채움 배율 — 가장자리 가중치 1 = 그대로, 0 = 채움 배율.
float DecalFloorBlendFill(float edgeWeight, float fillScale)
{
    return lerp(fillScale, 1.0, saturate(edgeWeight));
}

// 한쪽 가장자리 소프트 — norm = 그 가장자리에서 안쪽으로 잰 거리 ÷ 선 폭. 소프트 0 이면 1(그대로).
float DecalFloorBlendOuterSoft(float norm, float softness)
{
    return softness <= 1.0e-4 ? 1.0 : smoothstep(0.0, softness, norm);
}

// 선 양쪽 가장자리 소프트 — t = 선 안 위치(0 = 한쪽 끝, 1 = 다른 끝). 소프트 0 이면 1.
float DecalFloorBlendLineSoft(float t, float softness)
{
    return softness <= 1.0e-4 ? 1.0 : smoothstep(0.0, softness, t) * smoothstep(0.0, softness, 1.0 - t);
}

// 텍스처 그림처럼 가장자리·채움을 따로 모를 때 — 진한 알파(선·테두리)를 가장자리로 본다.
float DecalFloorBlendEdgeFromAlpha(float alpha, float lo, float hi)
{
    return smoothstep(lo, hi, alpha);
}

float DecalFloorBlendLuma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

// 외곽선 색 물들임 — c = 직선 알파 색(refFloor 와 같은 색공간), edge = DecalFloorBlendResolveEdge. 결과도 직선 알파.
// 알파 배율 1·물들임 0 이면 c 그대로.
float4 DecalFloorBlendStain(float4 c, float3 edge, float refFloor)
{
    c.a *= edge.x;
    float m = edge.y;
    if (m <= 1.0e-4 || c.a <= 0.0) return c;
    float aS = c.a * (1.0 - DFB_STAIN_KEEP);
    float3 dS;
    if (edge.z < 0.5)
    {
        // 밝기 보존(근사) — 색조 유지, 밝기를 기준 바닥 쪽으로 절반.
        float l = DecalFloorBlendLuma(c.rgb);
        float target = lerp(l, refFloor, 0.5);
        dS = l > 1.0e-3 ? c.rgb * (target / l) : float3(target, target, target);
    }
    else
    {
        // 곱하기(근사) — 기준 바닥 F 에서 F·(1 − aS) + dS·aS = F·c.
        dS = refFloor * (c.rgb - (1.0 - aS)) / max(aS, 1.0e-4);
    }
    dS = saturate(dS);
    float4 pm = lerp(float4(c.rgb * c.a, c.a), float4(dS * aS, aS), m);
    return float4(pm.rgb / max(pm.a, 1.0e-5), pm.a);
}

// ── 플레이어 스킬 범위·대상 원(SkillRangeIndicator.shadergraph) ──────────────────────────────
//  텍스처(SkillRangeIndicatorC·SelectedTargetCircleC, 1024²)는 원 그림이다: 안쪽 그라데이션 알파 ≤ 약 0.45, 선·흰 테두리 ≥ 0.7,
//  흰 바깥 테두리 = 정규화 반경 약 0.979 ~ 0.996. 그래서 가장자리 가중치 = 알파 0.45 → 0.7, 테두리 = 반경 0.978 ~ 0.996.
#define SKILL_RANGE_EDGE_ALPHA_LO 0.45
#define SKILL_RANGE_EDGE_ALPHA_HI 0.7
#define SKILL_RANGE_OUTER_RADIUS 0.996
#define SKILL_RANGE_RIM_WIDTH 0.018

// color = 텍스처 색 × _BaseColor(선형), texAlpha = 텍스처 알파. 결과 = (색, 알파 배율 — 텍스처 알파에 곱할 값).
float4 SkillRangeDecalFloorBlend(float2 uv, float3 color, float texAlpha)
{
    float3 b = DecalFloorBlendResolve(_DecalFloorBlendSkill);
    float3 e = DecalFloorBlendResolveEdge(_DecalFloorBlendSkillEdge);
    float edge = DecalFloorBlendEdgeFromAlpha(texAlpha, SKILL_RANGE_EDGE_ALPHA_LO, SKILL_RANGE_EDGE_ALPHA_HI);
    float r = length(uv - 0.5) * 2.0;
    float rimT = (r - (SKILL_RANGE_OUTER_RADIUS - SKILL_RANGE_RIM_WIDTH)) / SKILL_RANGE_RIM_WIDTH;   // 0 = 테두리 안쪽, 1 = 바깥 끝
    // 바깥 끝 소프트는 전체(채움이 테두리 밖으로 새지 않게), 안쪽 끝 소프트는 테두리 띠 안의 가장자리만.
    float soft = DecalFloorBlendOuterSoft(1.0 - rimT, b.y);
    if (rimT > 0.0 && rimT < 1.0) soft *= lerp(1.0, DecalFloorBlendOuterSoft(rimT, b.y), edge);
    float a = texAlpha * DecalFloorBlendFill(edge, b.x) * soft;
    float4 plain = float4(color, a);
    float4 stained = DecalFloorBlendStain(plain, e, DFB_REF_FLOOR_LINEAR);
    float4 pm = lerp(float4(plain.rgb * plain.a, plain.a), float4(stained.rgb * stained.a, stained.a), edge);
    float3 rgb = pm.a > 1.0e-5 ? pm.rgb / pm.a : color;
    return float4(rgb, pm.a / max(texAlpha, 1.0e-5));
}

#endif
