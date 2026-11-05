// ----------------------------------------------------------------------------
//  DecalFloorBlend.hlsl — 스킬 범위 데칼·몬스터 공격 예고의 바닥 블렌딩(할 일 221 1차)
//
//  바닥 무늬·명암이 비쳐 보이게 — 안쪽(채움) 알파를 공용 배율로 낮추고, 가장자리(외곽선)는 또렷하게 두되 바깥 끝만 부드럽게.
//    알파 × lerp(채움 배율, 1, 가장자리 가중치) × smoothstep(0, 소프트, 바깥 끝에서 안쪽으로 잰 거리 ÷ 바깥 테두리 폭)
//  C# 같은 식 = DecalFloorBlend.cs(EditMode 테스트 기준).
//
//  전역 값 2개(DecalFloorBlendDriver 가 DecalFloorBlendSettings 에서 매 프레임 넣는다):
//    _DecalFloorBlendSkill      플레이어 스킬 인디케이터(사거리 원·대상 원·지면 마커·거너 Q 직선·대시 화살표·조준 표시)
//    _DecalFloorBlendTelegraph  몬스터 공격 예고(C3 데칼·C3 판·TelegraphQuad 판)
//    x = 채움 알파 배율(0~1, 1 = 그대로)  y = 가장자리 소프트(0~1, 바깥 테두리 폭에 대한 비율, 0 = 그대로)
//    z = 판(투명 쿼드) 전용 채움 추가 배율(0~1, 1 = 그대로)  w = 1 이면 유효
//  🔴 w < 0.5(값을 넣은 적 없음 — 전역 기본 0·에디터·테스트)이면 (1, 0, 1) = 221 이전 화면과 같다.
//  이 파일은 재질 칸을 선언하지 않는다(전역만).
//  쓰는 곳: EnemyC3Decal.hlsl · EnemyC3Plate.shader · TelegraphQuad.shader · SkillLineIndicator.shader ·
//           SkillRangeIndicator.shadergraph(Custom Function SkillRangeDecalAlpha).
// ----------------------------------------------------------------------------
#ifndef DECAL_FLOOR_BLEND_INCLUDED
#define DECAL_FLOOR_BLEND_INCLUDED

float4 _DecalFloorBlendSkill;
float4 _DecalFloorBlendTelegraph;

// 전역 값 → (채움 배율, 소프트, 판 추가 배율). 값이 없으면 그대로(1, 0, 1).
float3 DecalFloorBlendResolve(float4 g)
{
    return g.w < 0.5 ? float3(1.0, 0.0, 1.0) : saturate(g.xyz);
}

// 안쪽 채움 배율 — 가장자리 가중치 1 = 그대로, 0 = 채움 배율.
float DecalFloorBlendFill(float edgeWeight, float fillScale)
{
    return lerp(fillScale, 1.0, saturate(edgeWeight));
}

// 바깥 끝 소프트 — outerNorm = 바깥 끝에서 안쪽으로 잰 거리 ÷ 바깥 테두리 폭(0 = 바깥 끝, 1 = 테두리 안쪽 경계).
// 소프트 0 이면 1(그대로). 소프트 s 면 바깥 끝부터 테두리 폭의 s 만큼이 0 → 1 로 부드럽게 올라온다.
float DecalFloorBlendOuterSoft(float outerNorm, float softness)
{
    return softness <= 1.0e-4 ? 1.0 : smoothstep(0.0, softness, outerNorm);
}

// 텍스처 그림처럼 가장자리·채움을 따로 모를 때 — 진한 알파(선·테두리)를 가장자리로 본다.
float DecalFloorBlendEdgeFromAlpha(float alpha, float lo, float hi)
{
    return smoothstep(lo, hi, alpha);
}

// ── 플레이어 스킬 범위·대상 원(SkillRangeIndicator.shadergraph) ──────────────────────────────
//  텍스처(SkillRangeIndicatorC·SelectedTargetCircleC, 1024²)는 원 그림이다: 안쪽 그라데이션 알파 ≤ 약 0.45, 선·흰 테두리 ≥ 0.7,
//  흰 바깥 테두리 = 정규화 반경 약 0.979 ~ 0.996. 그래서 가장자리 가중치 = 알파 0.45 → 0.7, 바깥 테두리 폭 = 반경의 0.018.
#define SKILL_RANGE_EDGE_ALPHA_LO 0.45
#define SKILL_RANGE_EDGE_ALPHA_HI 0.7
#define SKILL_RANGE_OUTER_RADIUS 0.996
#define SKILL_RANGE_RIM_WIDTH 0.018

float SkillRangeDecalFloorBlend(float2 uv, float texAlpha)
{
    float3 b = DecalFloorBlendResolve(_DecalFloorBlendSkill);
    float edge = DecalFloorBlendEdgeFromAlpha(texAlpha, SKILL_RANGE_EDGE_ALPHA_LO, SKILL_RANGE_EDGE_ALPHA_HI);
    float r = length(uv - 0.5) * 2.0;
    float outerNorm = (SKILL_RANGE_OUTER_RADIUS - r) / SKILL_RANGE_RIM_WIDTH;
    return DecalFloorBlendFill(edge, b.x) * DecalFloorBlendOuterSoft(outerNorm, b.y);
}

#endif
