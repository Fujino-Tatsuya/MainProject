using UnityEngine;

/// <summary>빛 모임 재생 시간 기준(할 일 132 단계 4).</summary>
public enum InterruptLightGatherTimeBase
{
    /// <summary>실시간 — 강 슬로우(0.15)·히트스톱 중에도 같은 빠르기로 보인다(기본).</summary>
    Unscaled,
    /// <summary>스케일 시간 — 슬로우를 따라 느려진다.</summary>
    Scaled,
}

/// <summary>빛 모임 한 벌 — 플레이어 소켓(<see cref="InterruptLightGatherSocket"/>)이 구현한다. 테스트는 가짜를 넣는다.</summary>
public interface IInterruptLightGatherPlayer
{
    /// <summary>재생 — 오너 암전에 남길 VFX 루트를 돌려준다(없으면 null).</summary>
    GameObject Play();
    void Stop();
}

/// <summary>
/// 빛 모임(할 일 132 단계 4) 순수 규칙 — 누가 틀지·어디에 붙일지·시간 기준·덮어쓸 텍스처 키워드.
/// 재생 시점: 오너 = 입력 순간 강 예측(<see cref="InterruptOwnerPresentation"/>), 팀원 = 서버 강 시도 슬로우 시작.
/// 끝 = 발동자 애니 Hit(각 피어 자기 애니메이터) — 팀원은 실패 복귀·성공·강제 리셋도 끝으로 본다.
/// </summary>
public static class InterruptLightGatherRules
{
    /// <summary>덮어쓸 텍스처 칸이 차 있을 때 켜는 셰이더 키워드 — 꺼져 있으면 절차 생성.</summary>
    public const string OverrideTextureKeyword = "_OVERRIDE_TEX_ON";
    /// <summary>덮어쓸 텍스처 칸 셰이더 속성 이름.</summary>
    public const string OverrideTextureProperty = "_OverrideTex";

    /// <summary>
    /// [각 피어] 서버 슬로우 시작으로 이 화면이 빛 모임을 트는가 — 강 시도(Attempt)이고 발동자가 이 화면 본인이 아닐 때만.
    /// 본인 화면은 입력 순간 오너 예측이 먼저 켜므로 중복 재생하지 않는다(약 예측·서버 강이어도 늦게 켜지 않는다 — 상태 기계 규칙).
    /// </summary>
    public static bool ShouldPlayForPeer(SlowMotionPurpose purpose, bool viewerIsCaster) =>
        purpose == SlowMotionPurpose.Attempt && !viewerIsCaster;

    /// <summary>붙일 곳 — 캐릭터 Variant 가 연결한 소켓, 비어 있으면(base) 캐릭터 중심.</summary>
    public static Transform ResolveAnchor(Transform socket, Transform center) => socket != null ? socket : center;

    public static bool UsesUnscaledTime(InterruptLightGatherTimeBase timeBase) => timeBase == InterruptLightGatherTimeBase.Unscaled;

    /// <summary>덮어쓸 텍스처를 쓰는가 — 비어 있으면 셰이더 절차 생성.</summary>
    public static bool UsesOverrideTexture(Texture overrideTexture) => overrideTexture != null;

    /// <summary>
    /// 재질의 덮어쓸 텍스처 칸에 맞춰 키워드를 맞춘다(셰이더 GUI 가 칸을 바꿀 때마다 부른다). 바뀐 경우만 true.
    /// 칸이 없는 재질은 건드리지 않는다.
    /// </summary>
    public static bool ApplyOverrideKeyword(Material material)
    {
        if (material == null || !material.HasProperty(OverrideTextureProperty))
            return false;

        bool wanted = UsesOverrideTexture(material.GetTexture(OverrideTextureProperty));
        if (material.IsKeywordEnabled(OverrideTextureKeyword) == wanted)
            return false;

        if (wanted)
            material.EnableKeyword(OverrideTextureKeyword);
        else
            material.DisableKeyword(OverrideTextureKeyword);
        return true;
    }
}
