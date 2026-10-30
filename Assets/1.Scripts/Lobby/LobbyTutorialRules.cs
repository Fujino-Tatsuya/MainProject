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
    /// 출격 확정·로딩 = GameStarting, 출격 집결 카운트다운 중 = CountdownActive. 그 밖 어느 단계든 교체 가능
    /// (훈련 명단 잠금 없음 — 할 일 157 Q1, 커밋 ③. 기록은 clientId 기준이라 교체로 지워지지 않는다 — PLAN §10.2).
    /// </summary>
    public static LobbyCharacterChangeReject CharacterChangeBlock(bool launching, bool sortieCountdownActive)
    {
        if (launching)
            return LobbyCharacterChangeReject.GameStarting;
        if (sortieCountdownActive)
            return LobbyCharacterChangeReject.CountdownActive;
        return LobbyCharacterChangeReject.None;
    }

    /// <summary>
    /// 대사 단계 길이(초, 할 일 157 Q5) — 행 <paramref name="rowSeconds"/> &gt; 0 이면 그 값, 아니면 문구 표 <paramref name="tableSeconds"/> &gt; 0 이면 그 값,
    /// 아니면 글자 수 규칙 max(<paramref name="minSeconds"/>, <paramref name="baseSeconds"/> + 글자 수 × <paramref name="perCharSeconds"/>).
    /// 어느 쪽이든 끝 여유 <paramref name="endPaddingSeconds"/> 를 더한다. 글자 수 = 공백 제외(<see cref="CountDialogueChars"/>). 음수 입력은 0.
    /// </summary>
    public static double DialogueSeconds(float rowSeconds, float tableSeconds, string text,
        float baseSeconds, float perCharSeconds, float minSeconds, float endPaddingSeconds)
    {
        double body;
        if (rowSeconds > 0f)
            body = rowSeconds;
        else if (tableSeconds > 0f)
            body = tableSeconds;
        else
            body = System.Math.Max(System.Math.Max(0f, minSeconds),
                System.Math.Max(0f, baseSeconds) + CountDialogueChars(text) * (double)System.Math.Max(0f, perCharSeconds));

        return body + System.Math.Max(0f, endPaddingSeconds);
    }

    /// <summary>대사 글자 수 — 공백·줄바꿈 제외. null = 0.</summary>
    public static int CountDialogueChars(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var count = 0;
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c))
                count++;
        }

        return count;
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

    /// <summary>
    /// 확정 텔레포트가 실패한 명단 사람(플레이어 오브젝트 없음·순간이동 거부)을 다시 옮겨 볼 마지막 행 — 확정 행(<paramref name="confirmIndex"/>) 뒤 첫 집결 행
    /// (L20 → T20, T50 → L30). 문이 막혀(157 Q4 B) 걸어서는 훈련장에 못 들어가므로 그 집결까지는 재시도한다. 뒤에 집결이 없으면 마지막 행, 표가 비면 -1.
    /// </summary>
    public static int TeleportRetryLastIndex(IReadOnlyList<LobbyTutorialStep> steps, int confirmIndex)
    {
        if (steps == null || steps.Count == 0)
            return -1;

        var next = FirstIndex(steps, confirmIndex + 1, s => s.kind == LobbyTutorialStepKind.Gather);
        return next >= 0 ? next : steps.Count - 1;
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

/// <summary><see cref="LobbyTutorialDialogueClock.Tick"/> 결과.</summary>
public enum LobbyTutorialDialogueAction : byte
{
    None = 0,
    /// <summary>새 대사 단계(revision) — 호출 쪽이 길이를 정해 스냅샷 timerStart/timerEnd 를 싣는다.</summary>
    Start = 1,
    /// <summary>끝 시각 도달 — 호출 쪽이 다음 행으로 전이를 요청하고, 받아들여졌으면 <see cref="LobbyTutorialDialogueClock.MarkEnded"/>.</summary>
    End = 2,
}

/// <summary>
/// [서버] 대사 단계 스케줄러(할 일 157 ③) — 순수 상태(EditMode 테스트 대상). 대사 행 revision 마다 시작 1회 → 끝 시각에 전이 1회.
/// 같은 revision 재진입·반복 없음. 전이 요청이 거부되면(참여자 0 등) <see cref="MarkEnded"/> 를 부르지 않아 다음 틱에 다시 End 를 낸다
/// (모델이 revision 으로 중복 전이를 막으므로 전이는 최대 1회).
/// </summary>
public sealed class LobbyTutorialDialogueClock
{
    int _revision;
    bool _started;
    bool _ended;

    public bool HasEnded => _ended;

    public LobbyTutorialDialogueAction Tick(int revision, double now, double endServerTime)
    {
        if (!_started || revision != _revision)
        {
            _revision = revision;
            _started = true;
            _ended = false;
            return LobbyTutorialDialogueAction.Start;
        }

        return !_ended && now >= endServerTime ? LobbyTutorialDialogueAction.End : LobbyTutorialDialogueAction.None;
    }

    /// <summary>이번 revision 의 끝 전이가 받아들여졌다 — 같은 revision 동안 다시 End 를 내지 않는다.</summary>
    public void MarkEnded(int revision)
    {
        if (_started && revision == _revision)
            _ended = true;
    }
}
