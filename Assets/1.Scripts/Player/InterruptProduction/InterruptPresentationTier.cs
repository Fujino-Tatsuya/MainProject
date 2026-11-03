using System;
using BeaverLobby.Player.Dash;
using UnityEngine;

/// <summary>간파 사건 종류(할 일 192) — 근접(우클릭 그대로) / 대쉬(대시에 실린 간파, <see cref="PlayerInterruptSkillBase.ArmDashAttach"/>).</summary>
public enum InterruptVariant : byte
{
    Normal = 0,
    Dash = 1,
}

/// <summary>
/// 간파 연출 단계(할 일 192) — 실패 / 간파(근접) / 대쉬 간파 / 제압 간파. 제압은 근접·대쉬 무관(Q6).
/// 디렉터가 <see cref="InterruptPresentationTierRules.Select"/> 로 고르고, 단계별 설정(<see cref="InterruptTierProfile"/>)을 읽는다.
/// </summary>
public enum InterruptPresentationTier : byte
{
    Fail = 0,
    Normal = 1,
    Dash = 2,
    Suppress = 3,
}

/// <summary>
/// 성공 섬광 종류(할 일 192 → 194 버전). V0 = 기존 적중 섬광 ②~⑥ / 제압 간파 애니메이션 섬광 시퀀스(179) /
/// V1 = 적중 라이트 연출(194 — 포인트 라이트·암순응·블룸·불꽃·파편·빛줄기, 모든 피어) / V2 = V1 + 적중 화이트아웃 몇 프레임(오너 화면).
/// 값은 직렬화되므로 순서를 바꾸지 않는다.
/// </summary>
public enum InterruptTierFlash : byte
{
    /// <summary>V0 — 기존 적중 섬광 ②~⑥(오너 화면) + 섬광 순간 월드 섬광·암순응(132).</summary>
    HitFlash = 0,
    /// <summary>제압 간파 애니메이션 섬광 시퀀스(179, 모든 피어) — A6 칸이 꺼졌거나 컷 데이터가 없으면 <see cref="HitFlash"/> 로 돌아간다.</summary>
    SuppressSequence = 1,
    /// <summary>V1 — 적중 라이트 연출(할 일 194). 연출 데이터·프리팹이 없으면 <see cref="HitFlash"/> 로 돌아간다.</summary>
    HitLight = 2,
    /// <summary>V2 — V1 + 적중 화이트아웃 몇 프레임(할 일 194, 오너 화면). 연출 데이터·프리팹이 없으면 <see cref="HitFlash"/> 로 돌아간다.</summary>
    HitLightWhiteout = 3,
}

/// <summary>
/// 적중 라이트 연출(V1·V2)의 암순응 방식(할 일 194 Q9). 가 = 화면 패스, 나 = 씬 조명 낮춤. 값은 직렬화되므로 순서를 바꾸지 않는다.
/// </summary>
public enum InterruptDarkAdaptMode : byte
{
    /// <summary>가(기본) — 기존 암순응 전체 화면 패스(어두운 픽셀만 낮춤, 빛·이펙트는 남음). 본인 화면 강하게·팀원 화면 약하게.</summary>
    ScreenPass = 0,
    /// <summary>나 — Directional Light·환경광을 잠깐 낮춘다(모든 화면 같은 세기). 23호 차징의 태양광 끄기와 배율로 겹친다.</summary>
    SceneLight = 1,
}

/// <summary>이번 성공에서 실제로 트는 섬광(할 일 194) — 단계 설정 + 칸·데이터 유무로 <see cref="InterruptPresentationTierRules.SelectSuccessLook"/> 가 고른다.</summary>
public enum InterruptSuccessLook : byte
{
    /// <summary>V0 — 기존 적중 섬광 ②~⑥.</summary>
    HitFlash = 0,
    /// <summary>제압 간파 섬광 시퀀스(179).</summary>
    SuppressSequence = 1,
    /// <summary>V1 — 적중 라이트 연출.</summary>
    HitLight = 2,
    /// <summary>V2 — 적중 라이트 연출 + 화이트아웃.</summary>
    HitLightWhiteout = 3,
}

/// <summary>
/// 단계 하나의 설정 묶음(할 일 192 §3) — 디렉터 칸. 줌 세기는 지금처럼 슬로우 목적(강·약 시도·성공)이 고른다(Q2 — 슬로우 그대로).
/// </summary>
[Serializable]
public sealed class InterruptTierProfile
{
    [Tooltip("이 단계로 예측된 강 시도에서 오너 암전(+빛 모임)을 켠다(Q1 — 기본은 제압 간파만)")]
    public bool ownerDim;
    [Tooltip("성공 섬광 종류 — V0 적중 섬광 ②~⑥ / 제압 간파 섬광 시퀀스 / V1 적중 라이트 연출 / V2 V1 + 화이트아웃(할 일 194)")]
    public InterruptTierFlash flash = InterruptTierFlash.HitFlash;
    [Tooltip("V1·V2 의 암순응 방식(할 일 194 Q9) — 가 = 화면 패스(어두운 픽셀만) / 나 = 태양광·환경광 낮춤. V0·시퀀스에는 쓰지 않는다")]
    public InterruptDarkAdaptMode darkAdapt = InterruptDarkAdaptMode.ScreenPass;

    public InterruptTierProfile() { }

    public InterruptTierProfile(bool ownerDim, InterruptTierFlash flash,
        InterruptDarkAdaptMode darkAdapt = InterruptDarkAdaptMode.ScreenPass)
    {
        this.ownerDim = ownerDim;
        this.flash = flash;
        this.darkAdapt = darkAdapt;
    }
}

/// <summary>간파 연출 단계 규칙(할 일 192) — 순수 함수. EditMode 테스트 대상.</summary>
public static class InterruptPresentationTierRules
{
    /// <summary>단계 선택표 — 실패면 실패, 성공이면 제압(근접·대쉬 무관, Q6) &gt; 대쉬 &gt; 일반.</summary>
    public static InterruptPresentationTier Select(bool succeeded, InterruptVariant variant, bool suppressInterrupt)
    {
        if (!succeeded)
            return InterruptPresentationTier.Fail;
        if (suppressInterrupt)
            return InterruptPresentationTier.Suppress;
        return variant == InterruptVariant.Dash ? InterruptPresentationTier.Dash : InterruptPresentationTier.Normal;
    }

    /// <summary>슬로우 이벤트·신고의 대쉬 칸 → 사건 종류.</summary>
    public static InterruptVariant VariantOf(bool dash) => dash ? InterruptVariant.Dash : InterruptVariant.Normal;

    /// <summary>
    /// [오너] 입력 순간 시도 암전(+빛 모임)을 켜는가(Q1) — 강 예측이고 예측된 성공 단계의 설정이 암전 켬일 때만.
    /// 약 예측은 아무것도 안 켠다(Q3 — 약한 줌만, 줌은 서버 약 시도 슬로우가 처리). 대상 없음은 시도로 치지 않는다.
    /// </summary>
    public static bool AttemptDims(InterruptAttemptForecast forecast, InterruptTierProfile predictedTier) =>
        forecast == InterruptAttemptForecast.Strong && predictedTier != null && predictedTier.ownerDim;

    /// <summary>
    /// 이번 성공이 제압 간파 섬광 시퀀스인가 — 성공 슬로우이고 단계 설정이 시퀀스이고 A6 칸이 켜져 있고 컷 데이터가 있을 때만(아니면 ②~⑥).
    /// </summary>
    public static bool PlaysSuppressSequence(SlowMotionPurpose purpose, InterruptTierProfile tier, bool suppressFlashOn, bool hasSequence) =>
        purpose == SlowMotionPurpose.Success && tier != null && tier.flash == InterruptTierFlash.SuppressSequence &&
        suppressFlashOn && hasSequence;

    /// <summary>
    /// [오너] 서버 성공 슬로우가 왔을 때 제압 섬광 시퀀스를 지금 시작하는가 — 오너 판정 cue 경로(191)가 판정 순간 이미 시퀀스를 틀었을 수 있다.
    /// 시퀀스여야 하는데 그 화면이 아직 안 틀었으면(팀원 화면·cue 경로 아님·예측은 제압 아님 → 서버는 제압) 지금 튼다. 이미 틀었으면 다시 틀지 않는다.
    /// </summary>
    public static bool StartsSuppressSequenceOnServerSuccess(bool playsSequence, bool ownerCueHandled, bool ownerPlayedSequence) =>
        playsSequence && !(ownerCueHandled && ownerPlayedSequence);

    /// <summary>
    /// 이번 성공에서 트는 섬광(할 일 194 — 단계 × 버전 선택표). 설정이 없으면 V0.
    /// 시퀀스 = A6 칸이 켜져 있고 컷 데이터가 있을 때만, V1·V2 = 적중 라이트 연출(데이터·프리팹)이 있을 때만 — 아니면 V0 로 돌아간다.
    /// </summary>
    public static InterruptSuccessLook SelectSuccessLook(InterruptTierProfile tier, bool suppressFlashOn, bool hasSequence, bool hasHitLight)
    {
        if (tier == null)
            return InterruptSuccessLook.HitFlash;

        switch (tier.flash)
        {
            case InterruptTierFlash.SuppressSequence:
                return suppressFlashOn && hasSequence ? InterruptSuccessLook.SuppressSequence : InterruptSuccessLook.HitFlash;
            case InterruptTierFlash.HitLight:
                return hasHitLight ? InterruptSuccessLook.HitLight : InterruptSuccessLook.HitFlash;
            case InterruptTierFlash.HitLightWhiteout:
                return hasHitLight ? InterruptSuccessLook.HitLightWhiteout : InterruptSuccessLook.HitFlash;
            default:
                return InterruptSuccessLook.HitFlash;
        }
    }

    /// <summary>적중 라이트 연출(V1·V2)인가 — ②~⑥ 진행기·섬광 순간 예약 대신 성공 순간 바로 튼다.</summary>
    public static bool UsesHitLight(InterruptSuccessLook look) =>
        look == InterruptSuccessLook.HitLight || look == InterruptSuccessLook.HitLightWhiteout;

    /// <summary>V2 화이트아웃 프레임 수 — V2 이고 이 화면이 발동자 본인일 때만(화면 효과는 오너만, ②~⑥ 와 같다). 그 밖은 0.</summary>
    public static int WhiteoutFrames(InterruptSuccessLook look, bool viewerIsCaster, int frames) =>
        look == InterruptSuccessLook.HitLightWhiteout && viewerIsCaster ? Mathf.Max(0, frames) : 0;
}

/// <summary>
/// [오너] 간파 대상이 이번 간파 성공으로 제압에 들어갈지 미리 알려 준다(할 일 192 — 시도 순간 오너 암전을 켜려면 판정 전에 알아야 한다).
/// 23호 = 복제된 남은 제압 칸이 이번 간파 한 번으로 0 이 되는가. 예측일 뿐 — 실제 제압 여부는 서버가 성공 슬로우 표식으로 보낸다.
/// </summary>
public interface IInterruptSuppressForecast
{
    bool WouldSuppressOnInterrupt { get; }
}
