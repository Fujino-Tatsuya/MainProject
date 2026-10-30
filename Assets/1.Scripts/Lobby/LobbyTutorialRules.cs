using System.Collections.Generic;

/// <summary>
/// 로비 튜토리얼 진행기 규칙(할 일 157 커밋 ②) — 단계표 행으로 판정하는 순수 함수(NGO·MonoBehaviour 의존 없음, EditMode 테스트 대상).
/// 옛 10단계 모델(LobbyTrainingFlow)의 정적 규칙(캐릭터 잠금·접속 차단·확정 텔레포트·멈춤 탈출 지점·체력 하한)을 행 기준으로 옮겼다.
/// </summary>
public static class LobbyTutorialRules
{
    /// <summary>훈련 상대(TRAIN_03) 단계 — T31~T40.</summary>
    public static bool IsSparringActivity(LobbyTutorialActivity activity) =>
        activity >= LobbyTutorialActivity.Counter && activity <= LobbyTutorialActivity.SuppressKill;

    /// <summary>
    /// 캐릭터 변경을 막는 진행기 쪽 사유(없으면 None) — 서버 RPC 검증과 클라 패널 잠금이 같이 쓴다.
    /// 출격 확정·로딩 = GameStarting, 출격 집결 카운트다운 중 = CountdownActive, 고정 명단 안 = TrainingLocked.
    /// (명단 잠금은 커밋 ③에서 없앤다 — 할 일 157 Q1. ②는 옛 동작 유지 — 잠금 시점만 L10 확정으로 당겨졌다.)
    /// </summary>
    public static LobbyCharacterChangeReject CharacterChangeBlock(bool launching, bool inLockedRoster, bool sortieCountdownActive)
    {
        if (launching)
            return LobbyCharacterChangeReject.GameStarting;
        if (sortieCountdownActive)
            return LobbyCharacterChangeReject.CountdownActive;
        if (inLockedRoster)
            return LobbyCharacterChangeReject.TrainingLocked;
        return LobbyCharacterChangeReject.None;
    }

    /// <summary>새 접속을 막아야 하는가 — 출격 집결 카운트다운 중이거나 출격 확정·로딩 뒤. 훈련·브리핑 중은 받는다(로비 대기).</summary>
    public static bool BlocksNewJoins(bool launching, bool sortieCountdownActive) => launching || sortieCountdownActive;

    /// <summary>이 구역의 참여자가 접속자 전원인가(출격). 아니면 진행기 참여자(명단 고정 전 = 접속자, 뒤 = 명단).</summary>
    public static bool UsesConnectedClients(LobbyGatherPoint point) => point == LobbyGatherPoint.Sortie;

    /// <summary>
    /// 집결 확정 직후 훈련 명단 전원을 옮길 구역(할 일 111 유지 — 157 Q4 B, L20 = 157 §10.3 안 A). 없으면 None.
    /// 입구(L20) → 훈련장 안 시작 위치(Train01Start), 훈련 종료(T50) → 로비 중앙(Brief, L30 이 바로 센다).
    /// </summary>
    public static LobbyGatherPoint TeleportTargetOnConfirm(LobbyGatherPoint confirmed)
    {
        switch (confirmed)
        {
            case LobbyGatherPoint.Entrance: return LobbyGatherPoint.Train01Start;
            case LobbyGatherPoint.TrainEnd: return LobbyGatherPoint.Brief;
            default: return LobbyGatherPoint.None;
        }
    }

    /// <summary>체력 하한 — 고정 명단 안 + 행 보정 Training 이면 1, 아니면 0. 늦은 참가자·클리어 플래그는 0.</summary>
    public static int HealthFloorFor(LobbyTutorialStep step, bool inLockedRoster) =>
        inLockedRoster && step != null && step.trainingProfile == LobbyTutorialTrainingProfile.Training
            ? LobbyTrainingSupportRules.TrainingHealthFloor
            : 0;

    /// <summary>
    /// 멈춤 탈출(로비 Alt+2)로 옮길 집결 구역 — 고정 명단 안일 때만. 집결 행이면 그 지점, 아니면 바로 앞 집결 행의 지점
    /// (확정 텔레포트가 있으면 그 도착지 — L21·T10 = Train01Start). 출격 집결·그 뒤·명단 밖은 None(= 스폰 지점).
    /// </summary>
    public static LobbyGatherPoint RecoveryPointFor(IReadOnlyList<LobbyTutorialStep> steps, int index, bool inLockedRoster)
    {
        if (!inLockedRoster || steps == null || index < 0 || index >= steps.Count)
            return LobbyGatherPoint.None;

        for (var i = index; i >= 0; i--)
        {
            var step = steps[i];
            if (step == null || step.kind != LobbyTutorialStepKind.Gather)
                continue;

            var point = step.gatherPoint;
            if (point == LobbyGatherPoint.Sortie)
                return LobbyGatherPoint.None;
            if (i == index)
                return point;

            var teleported = TeleportTargetOnConfirm(point);
            return teleported != LobbyGatherPoint.None ? teleported : point;
        }

        return LobbyGatherPoint.None;
    }

    /// <summary>명단을 고정하는 행 — 첫 집결 행(L10). 없으면 -1.</summary>
    public static int RosterLockIndex(IReadOnlyList<LobbyTutorialStep> steps) => FirstIndex(steps, 0, s => s.kind == LobbyTutorialStepKind.Gather);

    /// <summary>이 지점의 첫 집결 행(<paramref name="from"/> 부터). 없으면 -1.</summary>
    public static int IndexOfGather(IReadOnlyList<LobbyTutorialStep> steps, LobbyGatherPoint point, int from = 0) =>
        FirstIndex(steps, from, s => s.kind == LobbyTutorialStepKind.Gather && s.gatherPoint == point);

    /// <summary>이 종류의 첫 행(<paramref name="from"/> 부터). 없으면 -1.</summary>
    public static int IndexOfKind(IReadOnlyList<LobbyTutorialStep> steps, LobbyTutorialStepKind kind, int from = 0) =>
        FirstIndex(steps, from, s => s.kind == kind);

    /// <summary>입력 잠금 묶음(브리핑 L31~L34) 바로 뒤 행 — <paramref name="index"/> 가 묶음 안이 아니면 index+1.</summary>
    public static int IndexAfterLockedBlock(IReadOnlyList<LobbyTutorialStep> steps, int index)
    {
        if (steps == null)
            return index + 1;

        var next = index + 1;
        while (next < steps.Count && steps[next] != null && steps[next].locksInput)
            next++;
        return next;
    }

    static int FirstIndex(IReadOnlyList<LobbyTutorialStep> steps, int from, System.Func<LobbyTutorialStep, bool> match)
    {
        if (steps == null)
            return -1;

        for (var i = from < 0 ? 0 : from; i < steps.Count; i++)
        {
            if (steps[i] != null && match(steps[i]))
                return i;
        }

        return -1;
    }
}
