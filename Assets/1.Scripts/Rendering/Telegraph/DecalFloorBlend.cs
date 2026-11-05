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

    /// <summary>판(투명 쿼드)의 안쪽 채움 최종 배율 = 채움 × 판 추가 배율.</summary>
    public static float PlateFill(Vector4 global)
    {
        Vector3 r = Resolve(global);
        return r.x * r.z;
    }
}
