using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 오너 판정 cue 재생(할 일 191 4단계) — 순수 로직. 디렉터(<see cref="InterruptProductionDirector"/>)가 오너 상태 기계·적중 섬광 진행기를 넘겨 들고,
/// <see cref="IInterruptOwnerCueSink"/> 결과 cue 를 여기로 넘긴다(시도 cue 는 기존 <see cref="InterruptProductionDirector.LocalOwnerAttempt"/>).
/// <list type="bullet">
/// <item>판정 성공 = 서버 성공 슬로우 Started(Success) 의 본인 화면 경로와 같은 일(성공 단계 진입 → 강이었으면 섬광 오버레이 → 섬광 순간 예약)을
///       오너 판정 순간 로컬 발동 번호로 한다. 뒤이어 오는 내 서버 Success 는 <see cref="ConsumeServerStarted"/> 가 한 번 삼킨다(두 번 터지지 않게).</item>
/// <item>판정 실패·CC 끊김·판정 버림 = 결론 전 시도를 걷는다(기존 걷기 0.2초). 서버 거부 = 이미 튼 성공 섬광까지 즉시 끈다.</item>
/// <item>cue 경로가 몬 시도는 서버 시도 슬로우 목적으로 오너 연출을 바꾸지 않는다(D4 — 결론은 오너 판정, 서버 예측 빗나감 걷기 없음).</item>
/// <item>승인 목록(D10): 스위치가 켜져 있으면 나 말고 함께 성공한 캐릭터를 이번 암전에 남긴다(오너·대상은 시도 때 이미 남김).</item>
/// </list>
/// </summary>
public sealed class InterruptOwnerJudgeCuePlayer
{
    // 로컬 발동 번호 — 서버 슬로우 번호(1부터 증가)와 겹치지 않게 위쪽 절반을 쓴다.
    const uint LocalTriggerBase = 0x80000000u;

    readonly System.Func<InterruptOwnerPresentation> _presentation;
    readonly System.Func<InterruptHitFlashSequencer> _flash;
    readonly System.Action _stopHitFlash;
    uint _localTriggerSeq;

    /// <param name="stopHitFlash">서버 거부 때 오너 섬광 효과·월드 섬광을 즉시 끈다(디렉터 = StopHitFlash).</param>
    public InterruptOwnerJudgeCuePlayer(System.Func<InterruptOwnerPresentation> presentation,
        System.Func<InterruptHitFlashSequencer> flash, System.Action stopHitFlash)
    {
        _presentation = presentation;
        _flash = flash;
        _stopHitFlash = stopHitFlash;
    }

    /// <summary>오너 시도를 cue 경로(오너 판정)가 몰고 있다 — 내 서버 시도 슬로우 목적은 오너 연출에 반영하지 않는다.</summary>
    public bool DrivesAttempt { get; private set; }

    /// <summary>오너 판정 성공 섬광을 이미 틀었고, 내 서버 성공 슬로우 Started 를 아직 안 받았다.</summary>
    public bool SuccessPending { get; private set; }

    /// <summary>마지막 로컬 성공의 발동 번호(0 = 없음).</summary>
    public uint LocalSuccessTriggerId { get; private set; }

    /// <summary>[오너] 시도 cue 를 받았다(받아들였든 아니든 — 대상 없음이어도 이 시전은 cue 경로).</summary>
    public void Attempted()
    {
        DrivesAttempt = true;
        SuccessPending = false;
    }

    /// <summary>
    /// [오너] 판정 성공 — 서버 결과를 기다리지 않고 성공 단계로. <paramref name="now"/> = 실시간, <paramref name="effectNow"/>·<paramref name="frame"/> = 섬광 진행기 시계·렌더 프레임.
    /// 강 시도였으면 섬광 오버레이 ②~⑥, 약·대상 없음이면 섬광 순간만(월드 섬광·self 암순응) — 서버 성공과 같은 규칙.
    /// <paramref name="look"/> = 이번 성공의 섬광(할 일 192·194 — 예측 단계로 고른다). V0 만 ②~⑥·섬광 순간을 예약한다.
    /// 적중 라이트 연출(V1·V2·V3)은 예약하지 않는다(그 시작과 암전 걷기는 디렉터가 돌려받은 발동 번호로 한다).
    /// <paramref name="variant"/> = 근접/대쉬(할 일 194 — 섬광 순간 사건의 단계가 대쉬로 계산되게). 로컬 발동 번호를 돌려준다.
    /// </summary>
    public uint JudgeSucceeded(ulong localClientId, Vector3 flashPoint, Vector3 eventPosition, float now, float effectNow, int frame,
        InterruptSuccessLook look = InterruptSuccessLook.HitFlash, InterruptVariant variant = InterruptVariant.Normal)
    {
        InterruptOwnerPresentation presentation = _presentation();
        uint triggerId = LocalTriggerBase | (++_localTriggerSeq & ~LocalTriggerBase);

        presentation.ServerStarted(triggerId, SlowMotionPurpose.Success, true, now);
        LocalSuccessTriggerId = triggerId;
        LocalSuccessLook = look;
        SuccessPending = true;
        if (look != InterruptSuccessLook.HitFlash)
            return triggerId;

        InterruptHitFlashSequencer flash = _flash();
        bool ownerOverlay = InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.Success, true, presentation.SuccessDimmed);
        if (ownerOverlay)
            flash.BeginOwnerOverlay(flashPoint, presentation.AttackDirection, effectNow, frame);
        flash.ScheduleFlash(new InterruptHitFlashMoment(
            InterruptProductionEvent.LocalSuccess(triggerId, localClientId, eventPosition, false, variant), true, ownerOverlay), frame);
        return triggerId;
    }

    /// <summary>마지막 로컬 성공이 고른 섬광(할 일 194).</summary>
    public InterruptSuccessLook LocalSuccessLook { get; private set; }

    /// <summary>[오너] 즉시 종료 cue. 서버 거부면 성공 섬광까지 끄고, 나머지는 결론 전 시도를 걷는다.</summary>
    public void Ended(InterruptOwnerEndReason reason, float now)
    {
        InterruptOwnerPresentation presentation = _presentation();
        if (reason == InterruptOwnerEndReason.ServerRejected)
        {
            SuccessPending = false;
            if (presentation.State == InterruptOwnerPresentationState.Success && presentation.SuccessTriggerId == LocalSuccessTriggerId)
            {
                presentation.ForcedReset();
                _stopHitFlash?.Invoke();
                return;
            }
        }

        presentation.LocalEnded(now);
    }

    /// <summary>
    /// [오너] 내 서버 슬로우 Started — cue 경로가 이미 처리했으면 true(디렉터는 본인 화면 오너 연출·섬광 예약을 건너뛴다).
    /// 성공: 로컬 성공이 기다리고 있으면 한 번만 삼킨다. 시도(강·약): cue 경로가 몬 시도면 늘 삼킨다(서버 예측으로 걷지 않는다).
    /// </summary>
    public bool ConsumeServerStarted(SlowMotionPurpose purpose)
    {
        if (purpose == SlowMotionPurpose.Success)
        {
            if (!SuccessPending)
                return false;

            SuccessPending = false;
            return true;
        }

        return DrivesAttempt && (purpose == SlowMotionPurpose.Attempt || purpose == SlowMotionPurpose.AttemptWeak);
    }

    /// <summary>
    /// [오너] 승인 목록 — D10 스위치(<paramref name="keepCoSucceeded"/>)가 켜져 있으면 나 말고 목록의 시전자를 <paramref name="result"/> 에 담는다.
    /// 내가 첫 승인자가 아니면 내 서버 성공 슬로우는 오지 않는다(슬로우는 첫 승인만) — 삼킬 대기를 푼다.
    /// </summary>
    public void Approved(IReadOnlyList<ulong> approvedActorIds, ulong localActorId, bool keepCoSucceeded, List<ulong> result)
    {
        result.Clear();
        if (approvedActorIds == null || approvedActorIds.Count == 0)
            return;

        if (approvedActorIds[0] != localActorId)
            SuccessPending = false;

        SelectCoSucceeded(approvedActorIds, localActorId, keepCoSucceeded, result);
    }

    /// <summary>D10 — 암전에 함께 남길 시전자(나 제외 승인 목록). 스위치가 꺼져 있으면 없음.</summary>
    public static void SelectCoSucceeded(IReadOnlyList<ulong> approvedActorIds, ulong localActorId, bool keepCoSucceeded, List<ulong> result)
    {
        result.Clear();
        if (!keepCoSucceeded || approvedActorIds == null)
            return;

        for (int i = 0; i < approvedActorIds.Count; i++)
        {
            ulong id = approvedActorIds[i];
            if (id != localActorId && !result.Contains(id))
                result.Add(id);
        }
    }

    /// <summary>강제 리셋·꺼짐 — 대기를 모두 푼다.</summary>
    public void Reset()
    {
        DrivesAttempt = false;
        SuccessPending = false;
        LocalSuccessLook = InterruptSuccessLook.HitFlash;
    }
}
