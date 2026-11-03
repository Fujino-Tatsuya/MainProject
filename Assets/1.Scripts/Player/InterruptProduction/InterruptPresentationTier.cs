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
/// 성공 섬광 종류(할 일 192). 지금은 둘 — 기존 적중 섬광 ②~⑥ / 제압 간파 애니메이션 섬광 시퀀스(179).
/// Q4(일반 간파 포인트라이트·환경광 끄기)·Q5(대쉬 간파 암순응·블룸)가 확정되면 여기에 종류를 더하고 단계 설정이 그것을 가리키게 한다.
/// </summary>
public enum InterruptTierFlash : byte
{
    /// <summary>기존 적중 섬광 ②~⑥(오너 화면) + 섬광 순간 월드 섬광·암순응(132).</summary>
    HitFlash = 0,
    /// <summary>제압 간파 애니메이션 섬광 시퀀스(179, 모든 피어) — A6 칸이 꺼졌거나 컷 데이터가 없으면 <see cref="HitFlash"/> 로 돌아간다.</summary>
    SuppressSequence = 1,
}

/// <summary>
/// 단계 하나의 설정 묶음(할 일 192 §3) — 디렉터 칸. 줌 세기는 지금처럼 슬로우 목적(강·약 시도·성공)이 고른다(Q2 — 슬로우 그대로).
/// </summary>
[Serializable]
public sealed class InterruptTierProfile
{
    [Tooltip("이 단계로 예측된 강 시도에서 오너 암전(+빛 모임)을 켠다(Q1 — 기본은 제압 간파만)")]
    public bool ownerDim;
    [Tooltip("성공 섬광 종류 — 적중 섬광 ②~⑥ / 제압 간파 섬광 시퀀스. Q4·Q5 보류: 일반·대쉬는 지금 ②~⑥ 를 가리킨다")]
    public InterruptTierFlash flash = InterruptTierFlash.HitFlash;

    public InterruptTierProfile() { }

    public InterruptTierProfile(bool ownerDim, InterruptTierFlash flash)
    {
        this.ownerDim = ownerDim;
        this.flash = flash;
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
}

/// <summary>
/// [오너] 간파 대상이 이번 간파 성공으로 제압에 들어갈지 미리 알려 준다(할 일 192 — 시도 순간 오너 암전을 켜려면 판정 전에 알아야 한다).
/// 23호 = 복제된 남은 제압 칸이 이번 간파 한 번으로 0 이 되는가. 예측일 뿐 — 실제 제압 여부는 서버가 성공 슬로우 표식으로 보낸다.
/// </summary>
public interface IInterruptSuppressForecast
{
    bool WouldSuppressOnInterrupt { get; }
}
