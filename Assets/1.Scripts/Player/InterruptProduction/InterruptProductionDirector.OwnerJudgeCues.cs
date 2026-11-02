using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 할 일 191 4단계 — 오너 판정 cue 수신(InterruptManager → 디렉터). 디렉터 쪽 191 변경은 이 파일에 모은다
// (같은 시기 다른 레인이 디렉터 본문을 고치므로 본문은 HandleStarted 의 본인 화면 건너뛰기 세 줄만 손댔다).
public sealed partial class InterruptProductionDirector : IInterruptOwnerCueSink
{
    [Header("동시 성공 — 개인(할 일 191 D10)")]
    [Tooltip("같은 창을 함께 성공한(동시 성공 0.05초 안) 다른 플레이어 캐릭터도 내 암전에서 밝게 남긴다. 오너·보스는 늘 남는다. 구경하는 팀원 화면은 무관")]
    [DataTableIgnore, SerializeField] private bool keepCoSucceededBright = true;

    private InterruptOwnerJudgeCuePlayer _ownerJudgeCues;
    private readonly List<ulong> _coSucceededBuffer = new List<ulong>();

    private InterruptOwnerJudgeCuePlayer OwnerJudgeCues =>
        _ownerJudgeCues ??= new InterruptOwnerJudgeCuePlayer(() => OwnerPresentation, () => HitFlash, StopHitFlash);

    /// <summary>D10 칸 — 런타임 값(저장하지 않음, 디버그·테스트).</summary>
    public bool KeepCoSucceededBright
    {
        get => keepCoSucceededBright;
        set => keepCoSucceededBright = value;
    }

    // 내 서버 슬로우 Started 를 cue 경로가 이미 처리했는가(HandleStarted 본인 화면 — 오너 연출·섬광 예약 건너뜀).
    private bool OwnerJudgeCueHandles(SlowMotionPurpose purpose) =>
        _ownerJudgeCues != null && _ownerJudgeCues.ConsumeServerStarted(purpose);

    bool IInterruptOwnerCueSink.OwnerAttempt(in InterruptOwnerCue cue)
    {
        OwnerJudgeCues.Attempted();
        return LocalOwnerAttempt(cue.Skill, cue.Forecast, cue.Owner, cue.Target, cue.AttackDirection, cue.ViaDash);
    }

    // 판정 성공(할 일 192) — 시도 순간 예측한 단계가 제압 간파면 ②~⑥ 대신 제압 섬광 시퀀스를 지금 튼다(암전은 시퀀스 첫 프레임에 걷힌다).
    // 서버 표식은 성공 슬로우로 뒤에 온다 — 예측이 빗나가 서버가 제압이면 그때 시퀀스로 바꾼다(HandleStarted).
    void IInterruptOwnerCueSink.OwnerJudgeSucceeded(in InterruptOwnerCue cue)
    {
        ulong viewer = _networkManager != null ? _networkManager.LocalClientId : 0;
        bool suppressSequence = PlaysSuppressFlash(SlowMotionPurpose.Success, _ownerPredictedTier);
        uint triggerId = OwnerJudgeCues.JudgeSucceeded(viewer, FlashPoint(cue.FlashPosition), cue.FlashPosition,
            Time.unscaledTime, InterruptEffectTime.Now(HitFlash.Clock), Time.frameCount, suppressSequence);
        LastOwnerTier = _ownerPredictedTier;
        if (suppressSequence)
            BeginSuppressFlash(InterruptProductionEvent.LocalSuccess(triggerId, viewer, cue.FlashPosition, true,
                InterruptPresentationTierRules.VariantOf(cue.ViaDash)), true);
    }

    void IInterruptOwnerCueSink.OwnerEnded(InterruptOwnerEndReason reason)
    {
        // 서버 거부는 이미 튼 제압 섬광 시퀀스까지 끈다(②~⑥ 와 같은 규칙). 판정 실패·끊김은 결론 전 시도만 걷는다(Q3 — 그 순간 종료).
        if (reason == InterruptOwnerEndReason.ServerRejected && _ownerJudgeCues != null && _ownerJudgeCues.LocalSuccessPlayedSuppress)
            _suppressFlash?.Stop();
        OwnerJudgeCues.Ended(reason, Time.unscaledTime);
        LastOwnerTier = InterruptPresentationTier.Fail;
    }

    void IInterruptOwnerCueSink.OwnerApproved(IReadOnlyList<ulong> approvedActorIds, ulong localActorId)
    {
        OwnerJudgeCues.Approved(approvedActorIds, localActorId, keepCoSucceededBright, _coSucceededBuffer);
        for (int i = 0; i < _coSucceededBuffer.Count; i++)
        {
            GameObject actor = FindSpawnedObject(_coSucceededBuffer[i]);
            if (actor != null)
                KeepInOwnerDim(actor);
        }

        _coSucceededBuffer.Clear();
    }

    // ---- 디버그 단계 재현(할 일 192 — DebugInterruptLab 버튼). 이 화면에서만, 서버·슬로우 없이 오너 연출만 판정 cue 경로 그대로 돌린다 ----

    /// <summary>
    /// [디버그] 단계 재현 시도 — 실패 = 약 예측(약한 줌 자리, 이 재현은 슬로우가 없어 줌 없음), 그 밖 = 그 단계로 예측한 강 시도(제압이면 암전·빛 모임).
    /// 새 시도로 받아들였으면 true. 뒤이어 <see cref="DebugPreviewResolve"/> 를 부른다.
    /// </summary>
    public bool DebugPreviewAttempt(InterruptPresentationTier tier, PlayerInterruptSkillBase skill, GameObject owner, GameObject target,
        Vector3 attackDirection)
    {
        InterruptOwnerPresentation presentation = OwnerPresentation;
        var forecast = tier == InterruptPresentationTier.Fail
            ? BeaverLobby.Player.Dash.InterruptAttemptForecast.Weak
            : BeaverLobby.Player.Dash.InterruptAttemptForecast.Strong;
        if (!presentation.Accepts(forecast))
            return false;

        OwnerJudgeCues.Attempted();
        _ownerPredictedTier = tier == InterruptPresentationTier.Fail ? InterruptPresentationTier.Normal : tier;
        LastOwnerTier = tier;
        _ownerDimOutput?.SetSubjects(owner, target);
        return presentation.Predict(forecast, skill, Time.unscaledTime, attackDirection,
            InterruptPresentationTierRules.AttemptDims(forecast, TierProfile(tier)));
    }

    /// <summary>
    /// [디버그] 단계 재현 결론 — 실패 단계면 판정 실패 cue(그 순간 종료), 아니면 판정 성공 cue(단계 섬광). 서버 성공 슬로우는 오지 않으므로 삼킬 대기를 푼다.
    /// </summary>
    public void DebugPreviewResolve(InterruptPresentationTier tier, Vector3 flashPosition)
    {
        IInterruptOwnerCueSink sink = this;
        if (tier == InterruptPresentationTier.Fail)
        {
            sink.OwnerEnded(InterruptOwnerEndReason.JudgeFailed);
            return;
        }

        sink.OwnerJudgeSucceeded(new InterruptOwnerCue
        {
            FlashPosition = flashPosition,
            ViaDash = tier == InterruptPresentationTier.Dash,
        });
        LastSuccessTier = tier;
        OwnerJudgeCues.Reset();
    }

    private GameObject FindSpawnedObject(ulong networkObjectId)
    {
        NetworkSpawnManager spawnManager = _networkManager != null ? _networkManager.SpawnManager : null;
        return spawnManager != null && spawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject networkObject) &&
               networkObject != null
            ? networkObject.gameObject
            : null;
    }
}
