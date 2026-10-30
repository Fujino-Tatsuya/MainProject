using System;
using System.Collections.Generic;

/// <summary>158 이 읽는 단계 한 행의 읽기 전용 보기(할 일 157 계약 §1). <see cref="IsValid"/> false = 진행기 없음.</summary>
public readonly struct LobbyTutorialStepView
{
    static readonly string[] Empty = Array.Empty<string>();

    readonly LobbyTutorialStep _step;

    public LobbyTutorialStepView(int index, LobbyTutorialStep step)
    {
        Index = index;
        _step = step;
    }

    public bool IsValid => _step != null;
    public int Index { get; }
    public string StateId => _step?.stateId ?? string.Empty;
    public LobbyTutorialStepKind Kind => _step?.kind ?? LobbyTutorialStepKind.Loading;
    public LobbyTutorialActivity Activity => _step?.activity ?? LobbyTutorialActivity.None;
    public string TitleKey => _step?.titleKey ?? string.Empty;
    public string ObjectiveKey => _step?.objectiveKey ?? string.Empty;
    public IReadOnlyList<string> BodyKeys => _step?.bodyKeys ?? Empty;
    public IReadOnlyList<string> ChecklistKeys => _step?.checklistKeys ?? Empty;
    public LobbyTutorialProgressMode ProgressMode => _step?.progressMode ?? LobbyTutorialProgressMode.None;
    public string ProgressKey => _step?.progressKey ?? string.Empty;
    public string CountdownStateId => _step?.countdownStateId ?? string.Empty;
    public string CountdownObjectiveKey => _step?.countdownObjectiveKey ?? string.Empty;
    public string CountdownProgressKey => _step?.countdownProgressKey ?? string.Empty;
    public LobbyGatherPoint GatherPointId => _step != null && _step.kind == LobbyTutorialStepKind.Gather ? _step.gatherPoint : LobbyGatherPoint.None;
    public string PathId => _step?.pathId ?? string.Empty;
    public string TargetWallId => _step?.targetWallId ?? string.Empty;
    public string DialogueId => _step?.dialogueId ?? string.Empty;
    public string SpeakerKey => _step?.speakerKey ?? string.Empty;
    public string BackgroundId => _step?.backgroundId ?? string.Empty;
    public bool LocksInput => _step != null && _step.locksInput;
}

/// <summary>대사 단계 보기 — 서버 시각으로 시작·끝(계약 §1).</summary>
public readonly struct LobbyTutorialDialogueView
{
    public LobbyTutorialDialogueView(string dialogueId, string speakerKey, string backgroundId, double startServerTime, double endServerTime, int revision)
    {
        DialogueId = dialogueId ?? string.Empty;
        SpeakerKey = speakerKey ?? string.Empty;
        BackgroundId = backgroundId ?? string.Empty;
        StartServerTime = startServerTime;
        EndServerTime = endServerTime;
        Revision = revision;
    }

    public bool Active => !string.IsNullOrEmpty(DialogueId);
    public string DialogueId { get; }
    public string SpeakerKey { get; }
    public string BackgroundId { get; }
    public double StartServerTime { get; }
    public double EndServerTime { get; }
    public int Revision { get; }
}

/// <summary>
/// 진행기 쪽 실시간 값(구역 n/N·체크 마스크·명단 여부·훈련 상대) — 커밋 ②에서 <see cref="LobbyTrainingDirector"/> 가 구현해 <see cref="LobbyTutorialState.SetLiveSource"/> 로 건다.
/// 모든 피어에서 복제값으로 답해야 한다.
/// </summary>
public interface ILobbyTutorialLiveSource
{
    PartyGatherZone GatherZone(LobbyGatherPoint point);
    /// <summary>로컬 플레이어가 명단 고정 뒤 접속한 늦은 참가자(훈련 단계 목표 패널 숨김).</summary>
    bool IsLocalWaitingLateJoiner { get; }
    /// <summary>T10 — 로컬 6항목 bitmask(<see cref="LobbyWeaponCheckRules.Has"/>).</summary>
    bool TryGetLocalChecklistMask(out int mask);
    /// <summary>T10 — 6항목 완료 인원/명단 인원.</summary>
    bool TryGetPartyComplete(out int done, out int total);
    TrainingSparringBot TrainingTarget { get; }
}

/// <summary>
/// [클라·전 피어] 로비 튜토리얼 상태 파사드 — 할 일 157 PLAN §3, 계약 <c>Docs/tech/handoff-lobby-tutorial-contract.md</c>. 158(표시)은 이것만 읽는다.
/// 생산자(진행기, 커밋 ②)가 스냅샷이 바뀔 때마다 <see cref="Publish"/> 를 부르고, 진행 줄 값이 바뀌면 <see cref="NotifyProgressChanged"/> 를 부른다.
/// 커밋 ① 시점엔 아무도 Publish 하지 않으므로 <see cref="IsAvailable"/> = false 다(기존 동작 무변경).
/// </summary>
public static class LobbyTutorialState
{
    static LobbyTutorialStepTable _table;
    static LobbyTutorialSnapshot _snapshot;
    static ILobbyTutorialLiveSource _live;
    static LobbyTutorialStepView _current;
    static LobbyTutorialDialogueView _dialogue;

    /// <summary>스냅샷 revision 이 바뀜(회차 첫 Publish 포함). prev 는 회차 첫 번째면 IsValid=false.</summary>
    public static event Action<LobbyTutorialStepView, LobbyTutorialStepView, int> StepChanged;
    public static event Action<LobbyTutorialDialogueView> DialogueStarted;
    /// <summary>(dialogueId, 끝난 단계의 revision). 연속 대사면 같은 Publish 안에서 Ended 직후 Started.</summary>
    public static event Action<string, int> DialogueEnded;
    public static event Action ProgressChanged;
    /// <summary>지정 벽 표식을 끈다 — 벽 키가 있던 단계를 떠날 때.</summary>
    public static event Action<string> TargetWallCleared;

    public static bool IsAvailable => _table != null && _snapshot.IsStarted && _current.IsValid;
    public static LobbyTutorialStepTable Table => _table;
    public static LobbyTutorialSnapshot Snapshot => _snapshot;
    public static int RunId => _snapshot.RunId;
    public static int Revision => _snapshot.Revision;
    public static LobbyTutorialStepView CurrentStep => _current;

    /// <summary>활성 집결 구역 카운트다운 중이고 행에 카운트다운 표시 상태가 있으면 그 ID(T21), 아니면 StateId.</summary>
    public static string DisplayStateId => IsCountdownDisplay ? _current.CountdownStateId : _current.StateId;

    public static string TitleKey => _current.TitleKey;
    public static string ObjectiveKey => IsCountdownDisplay ? _current.CountdownObjectiveKey : _current.ObjectiveKey;
    public static IReadOnlyList<string> BodyKeys => _current.BodyKeys;

    public static LobbyTutorialProgressMode ProgressMode =>
        IsCountdownDisplay ? LobbyTutorialProgressMode.Countdown : _current.ProgressMode;

    public static string ProgressKey => IsCountdownDisplay ? _current.CountdownProgressKey : _current.ProgressKey;

    /// <summary>false = 진행기 없음·대사·로딩·출격 확정 뒤·명단 밖 늦은 참가자(L10 확정 뒤 단계).</summary>
    public static bool ObjectivePanelVisible
    {
        get
        {
            if (!IsAvailable || _snapshot.LaunchCommitted)
                return false;
            if (_current.Kind == LobbyTutorialStepKind.Dialogue || _current.Kind == LobbyTutorialStepKind.Loading)
                return false;
            return _live == null || !_live.IsLocalWaitingLateJoiner;
        }
    }

    public static LobbyGatherPoint ActiveGatherPointId =>
        IsAvailable && !_snapshot.LaunchCommitted ? _current.GatherPointId : LobbyGatherPoint.None;

    public static PartyGatherZone ActiveGatherZone
    {
        get
        {
            var point = ActiveGatherPointId;
            return point == LobbyGatherPoint.None || _live == null ? null : _live.GatherZone(point);
        }
    }

    public static string ActivePathId => IsAvailable && !_snapshot.LaunchCommitted ? _current.PathId : string.Empty;
    public static string ActiveTargetWallId => IsAvailable && !_snapshot.LaunchCommitted ? _current.TargetWallId : string.Empty;

    /// <summary>카운트다운 표시 상태일 때만 남은 초, 아니면 0(유지 0.3초는 표시하지 않는다).</summary>
    public static double CountdownRemainingSeconds => IsCountdownDisplay ? ActiveGatherZone.CountdownRemainingSeconds : 0d;

    public static LobbyTutorialDialogueView Dialogue => _dialogue;
    public static bool IsDialogueActive => _dialogue.Active;
    public static bool IsInputLockedForBriefing => IsAvailable && _current.LocksInput;
    public static bool LaunchCommitted => _snapshot.LaunchCommitted;
    public static TrainingSparringBot TrainingTarget => _live?.TrainingTarget;

    /// <summary>진행 줄 값 — Gather = 활성 구역 모인 수/참여자, PartyComplete = 완료 인원/명단, Countdown = 남은 정수 초(올림)/0. None·값 없음이면 false.</summary>
    public static bool TryGetProgress(out int n, out int total)
    {
        n = 0;
        total = 0;
        if (!IsAvailable)
            return false;

        switch (ProgressMode)
        {
            case LobbyTutorialProgressMode.Gather:
            {
                var zone = ActiveGatherZone;
                if (zone == null)
                    return false;
                n = zone.GatheredCount;
                total = zone.ParticipantCount;
                return true;
            }
            case LobbyTutorialProgressMode.PartyComplete:
                return _live != null && _live.TryGetPartyComplete(out n, out total);
            case LobbyTutorialProgressMode.Countdown:
                n = (int)Math.Ceiling(CountdownRemainingSeconds);
                return true;
            default:
                return false;
        }
    }

    /// <summary>T10 개인 체크 — 내 bitmask 와 항목 키. 체크 항목이 있는 행에서만 true.</summary>
    public static bool TryGetLocalChecklist(out int mask, out IReadOnlyList<string> itemKeys)
    {
        mask = 0;
        itemKeys = _current.ChecklistKeys;
        if (!IsAvailable || itemKeys.Count == 0 || _live == null)
            return false;
        return _live.TryGetLocalChecklistMask(out mask);
    }

    // ── 생산자(진행기) 쪽 ──

    public static void SetLiveSource(ILobbyTutorialLiveSource source) => _live = source;

    /// <summary>
    /// 스냅샷 반영. 같은 (runId, revision) 이면 시각·출격 값만 갱신(이벤트 없음). revision·회차가 바뀌면 대사 Ended → StepChanged → 대사 Started → 벽 해제 순으로 알린다.
    /// </summary>
    public static void Publish(LobbyTutorialStepTable table, in LobbyTutorialSnapshot snapshot)
    {
        if (table == null || !snapshot.IsStarted)
        {
            Clear();
            return;
        }

        var sameStep = _table == table && _snapshot.RunId == snapshot.RunId && _snapshot.Revision == snapshot.Revision;
        var prev = _current;
        var prevDialogue = _dialogue;
        var prevRevision = _snapshot.Revision;

        _table = table;
        _snapshot = snapshot;
        _current = table.TryGet(snapshot.StepIndex, out var step) ? new LobbyTutorialStepView(snapshot.StepIndex, step) : default;
        _dialogue = BuildDialogue(_current, snapshot);

        if (sameStep)
            return;

        if (prevDialogue.Active)
            DialogueEnded?.Invoke(prevDialogue.DialogueId, prevRevision);
        StepChanged?.Invoke(prev, _current, snapshot.Revision);
        if (_dialogue.Active)
            DialogueStarted?.Invoke(_dialogue);
        if (prev.IsValid && !string.IsNullOrEmpty(prev.TargetWallId) && prev.TargetWallId != _current.TargetWallId)
            TargetWallCleared?.Invoke(prev.TargetWallId);
    }

    /// <summary>생산자가 진행 줄 값(구역 n/N·체크 마스크·카운트다운 정수) 변경을 알린다.</summary>
    public static void NotifyProgressChanged() => ProgressChanged?.Invoke();

    /// <summary>진행기 디스폰·씬 이탈. 대사 중이면 Ended 를 알린다. 구독자는 남긴다.</summary>
    public static void Clear()
    {
        var prevDialogue = _dialogue;
        var prevRevision = _snapshot.Revision;
        _table = null;
        _snapshot = default;
        _current = default;
        _dialogue = default;
        _live = null;
        if (prevDialogue.Active)
            DialogueEnded?.Invoke(prevDialogue.DialogueId, prevRevision);
    }

    /// <summary>테스트 전용 — 상태와 구독자 모두 지운다.</summary>
    public static void ResetForTests()
    {
        Clear();
        StepChanged = null;
        DialogueStarted = null;
        DialogueEnded = null;
        ProgressChanged = null;
        TargetWallCleared = null;
    }

    static bool IsCountdownDisplay
    {
        get
        {
            if (!IsAvailable || string.IsNullOrEmpty(_current.CountdownStateId))
                return false;
            var zone = ActiveGatherZone;
            return zone != null && zone.IsCountdownActive;
        }
    }

    static LobbyTutorialDialogueView BuildDialogue(LobbyTutorialStepView step, in LobbyTutorialSnapshot snapshot)
    {
        if (!step.IsValid || step.Kind != LobbyTutorialStepKind.Dialogue)
            return default;
        return new LobbyTutorialDialogueView(step.DialogueId, step.SpeakerKey, step.BackgroundId,
            snapshot.TimerStartServerTime, snapshot.TimerEndServerTime, snapshot.Revision);
    }
}
