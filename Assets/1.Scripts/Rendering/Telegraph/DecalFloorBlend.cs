using UnityEngine;

/// <summary>
/// 스킬 범위 데칼·몬스터 공격 예고의 바닥 블렌딩(할 일 221 1차) — 셰이더 <c>DecalFloorBlend.hlsl</c> 와 같은 식의 C# 원본.
///
/// 바닥 무늬·명암이 비쳐 보이게 안쪽(채움) 알파를 공용 배율로 낮추고, 가장자리(외곽선)는 또렷하게 두되 바깥 끝만 부드럽게 한다.
///   알파 × <see cref="Fill"/>(가장자리 가중치, 채움 배율) × <see cref="OuterSoft"/>(바깥 끝에서 잰 거리 ÷ 바깥 테두리 폭, 소프트)
///
/// 전역 셰이더 값 2개(스킬 <see cref="SkillGlobal"/>·예고 <see cref="TelegraphGlobal"/>) —
/// x = 채움 알파 배율, y = 가장자리 소프트, z = 판(투명 쿼드) 전용 채움 추가 배율, w = 1 이면 유효.
/// w = 0(값을 넣은 적 없음)이면 셰이더는 (1, 0, 1) = 221 이전 화면으로 본다. 값은 <see cref="DecalFloorBlendDriver"/> 가 넣는다.
/// </summary>
public static class DecalFloorBlend
{
    public const string SkillGlobal = "_DecalFloorBlendSkill";
    public const string TelegraphGlobal = "_DecalFloorBlendTelegraph";

    public static readonly int SkillGlobalId = Shader.PropertyToID(SkillGlobal);
    public static readonly int TelegraphGlobalId = Shader.PropertyToID(TelegraphGlobal);

    /// <summary>외곽선 전역 값(221 후속) — x = 외곽선 알파 배율, y = 물들임(0 = 일반 섞기 ~ 1 = 완전 물들임), z = 모드(<see cref="DecalFloorStainMode"/>), w = 1 유효.</summary>
    public const string SkillEdgeGlobal = "_DecalFloorBlendSkillEdge";
    public const string TelegraphEdgeGlobal = "_DecalFloorBlendTelegraphEdge";
    public static readonly int SkillEdgeGlobalId = Shader.PropertyToID(SkillEdgeGlobal);
    public static readonly int TelegraphEdgeGlobalId = Shader.PropertyToID(TelegraphEdgeGlobal);

    /// <summary>외곽선 그대로 — 알파 1·물들임 0·모드 0, 유효.</summary>
    public static readonly Vector4 EdgeIdentity = new Vector4(1f, 0f, 0f, 1f);

    /// <summary>물들임 근사의 기준 바닥 밝기(셰이더 DFB_REF_FLOOR_GAMMA / _LINEAR).</summary>
    public const float RefFloorGamma = 0.3f;
    public const float RefFloorLinear = 0.0732f;
    /// <summary>완전 물들임에서 남기는 바닥 비율(셰이더 DFB_STAIN_KEEP).</summary>
    public const float StainKeep = 0.5f;

    /// <summary>그대로(221 이전) — 채움 1·소프트 0·판 추가 1, 유효.</summary>
    public static readonly Vector4 Identity = new Vector4(1f, 0f, 1f, 1f);

    /// <summary>셰이더 <c>DecalFloorBlendOuterSoft</c> 의 "0 으로 본다" 경계.</summary>
    public const float MinSoftness = 1e-4f;

    /// <summary>설정값 → 전역 값(0~1 로 자르고 w = 1).</summary>
    public static Vector4 ToGlobal(float fillAlphaScale, float edgeSoftness, float plateFillAlphaScale) =>
        new Vector4(Mathf.Clamp01(fillAlphaScale), Mathf.Clamp01(edgeSoftness), Mathf.Clamp01(plateFillAlphaScale), 1f);

    /// <summary>전역 값 → (채움, 소프트, 판 추가). w &lt; 0.5 면 그대로(1, 0, 1) — 셰이더 <c>DecalFloorBlendResolve</c>.</summary>
    public static Vector3 Resolve(Vector4 global) =>
        global.w < 0.5f
            ? new Vector3(1f, 0f, 1f)
            : new Vector3(Mathf.Clamp01(global.x), Mathf.Clamp01(global.y), Mathf.Clamp01(global.z));

    /// <summary>안쪽 채움 배율 — 가장자리 가중치 1 = 1(그대로), 0 = 채움 배율.</summary>
    public static float Fill(float edgeWeight, float fillAlphaScale) =>
        Mathf.Lerp(fillAlphaScale, 1f, Mathf.Clamp01(edgeWeight));

    /// <summary>
    /// 바깥 끝 소프트 — <paramref name="outerNorm"/> = 바깥 끝에서 안쪽으로 잰 거리 ÷ 바깥 테두리 폭(0 = 바깥 끝, 1 = 테두리 안쪽 경계).
    /// 소프트 0 이면 1. 소프트 s 면 테두리 폭의 s 만큼이 0 → 1 로 부드럽게(HLSL smoothstep) 올라온다.
    /// </summary>
    public static float OuterSoft(float outerNorm, float softness)
    {
        if (softness <= MinSoftness) return 1f;
        float t = Mathf.Clamp01(outerNorm / softness);
        return t * t * (3f - 2f * t);
    }

    /// <summary>외곽선 설정값 → 전역 값(0~1 로 자르고 w = 1).</summary>
    public static Vector4 ToEdgeGlobal(float outlineAlphaScale, float stain, DecalFloorStainMode mode) =>
        new Vector4(Mathf.Clamp01(outlineAlphaScale), Mathf.Clamp01(stain), (float)mode, 1f);

    /// <summary>외곽선 전역 값 → (알파 배율, 물들임, 모드). w &lt; 0.5 면 그대로(1, 0, 0) — 셰이더 DecalFloorBlendResolveEdge.</summary>
    public static Vector3 ResolveEdge(Vector4 global) =>
        global.w < 0.5f ? new Vector3(1f, 0f, 0f) : new Vector3(Mathf.Clamp01(global.x), Mathf.Clamp01(global.y), global.z);

    /// <summary>선 양쪽 가장자리 소프트 — t = 선 안 위치(0 ~ 1). 셰이더 DecalFloorBlendLineSoft.</summary>
    public static float LineSoft(float t, float softness) => OuterSoft(t, softness) * OuterSoft(1f - t, softness);

    static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

    /// <summary>
    /// 외곽선 색 물들임(셰이더 DecalFloorBlendStain) — 직선 알파 색 <paramref name="c"/>(refFloor 와 같은 색공간).
    /// DBuffer 는 바닥 알베도와 lerp 만 되므로 "기준 바닥 밝기에서 목표 섞기와 같아지는 (색, 알파)" 로 근사하고 물들임만큼 보간한다.
    /// 알파 배율 1·물들임 0 이면 그대로.
    /// </summary>
    public static Color Stain(Color c, Vector3 edge, float refFloor)
    {
        c.a *= edge.x;
        float m = edge.y;
        if (m <= MinSoftness || c.a <= 0f) return c;
        float aS = c.a * (1f - StainKeep);
        Vector3 rgb = new Vector3(c.r, c.g, c.b), dS;
        if (edge.z < 0.5f)
        {
            float l = Luma(c), target = Mathf.Lerp(l, refFloor, 0.5f);
            dS = l > 1e-3f ? rgb * (target / l) : new Vector3(target, target, target);
        }
        else
        {
            dS = (rgb - Vector3.one * (1f - aS)) * (refFloor / Mathf.Max(aS, 1e-4f));
        }
        dS = new Vector3(Mathf.Clamp01(dS.x), Mathf.Clamp01(dS.y), Mathf.Clamp01(dS.z));
        Vector3 pmRgb = Vector3.Lerp(rgb * c.a, dS * aS, m);
        float pmA = Mathf.Lerp(c.a, aS, m);
        Vector3 o = pmRgb / Mathf.Max(pmA, 1e-5f);
        return new Color(o.x, o.y, o.z, pmA);
    }

    /// <summary>판(투명 쿼드)의 안쪽 채움 최종 배율 = 채움 × 판 추가 배율.</summary>
    public static float PlateFill(Vector4 global)
    {
        Vector3 r = Resolve(global);
        return r.x * r.z;
    }
}

/// <summary>외곽선 물들임 근사 방식(할 일 221 후속) — 셰이더 전역 값 z.</summary>
public enum DecalFloorStainMode
{
    /// <summary>밝기 보존(근사) — 색조는 두고 밝기를 기준 바닥 쪽으로 절반 당긴다. 흰 선도 어두운 바닥에서 읽힌다.</summary>
    LuminanceKeep = 0,
    /// <summary>곱하기(근사) — 기준 바닥에서 바닥 × 색. 흰 선은 기준 바닥에서 거의 사라진다.</summary>
    Multiply = 1,
}
