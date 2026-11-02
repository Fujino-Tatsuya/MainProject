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
        return LocalOwnerAttempt(cue.Skill, cue.Forecast, cue.Owner, cue.Target, cue.AttackDirection);
    }

    void IInterruptOwnerCueSink.OwnerJudgeSucceeded(in InterruptOwnerCue cue)
    {
        ulong viewer = _networkManager != null ? _networkManager.LocalClientId : 0;
        OwnerJudgeCues.JudgeSucceeded(viewer, FlashPoint(cue.FlashPosition), cue.FlashPosition,
            Time.unscaledTime, InterruptEffectTime.Now(HitFlash.Clock), Time.frameCount);
    }

    void IInterruptOwnerCueSink.OwnerEnded(InterruptOwnerEndReason reason) =>
        OwnerJudgeCues.Ended(reason, Time.unscaledTime);

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

    private GameObject FindSpawnedObject(ulong networkObjectId)
    {
        NetworkSpawnManager spawnManager = _networkManager != null ? _networkManager.SpawnManager : null;
        return spawnManager != null && spawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject networkObject) &&
               networkObject != null
            ? networkObject.gameObject
            : null;
    }
}
