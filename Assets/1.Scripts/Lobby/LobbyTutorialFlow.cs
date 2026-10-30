using System;
using System.Collections.Generic;

/// <summary><see cref="LobbyTutorialFlow.TryAdvance"/> 결과.</summary>
public enum LobbyTutorialAdvanceResult : byte
{
    Advanced = 0,
    /// <summary>이번 프레임에 이미 전이했다 — 요청을 보관했다가 다음 프레임 <see cref="LobbyTutorialFlow.Tick"/> 에서 적용.</summary>
    Deferred = 1,
    StaleRun = 2,
    StaleRevision = 3,
    /// <summary>목표 행이 현재 이하(역행·중복).</summary>
    Backward = 4,
    /// <summary>표 범위 밖·마지막 행.</summary>
    OutOfRange = 5,
    /// <summary>참여자 0 — 전이 금지(명세 §7 N=0).</summary>
    NoParticipants = 6,
    /// <summary>출격 확정 뒤 — 로딩 행 말고는 전이하지 않는다(확정 뒤 집결·대사·완료 보고 무시, 할 일 157 ⑩).</summary>
    LaunchLocked = 7,
}

/// <summary>
/// [서버] 로비 튜토리얼 단계 모델 — 할 일 157 PLAN §2. NGO·MonoBehaviour 의존 없는 순수 상태(EditMode 테스트 대상).
/// 서버 진행기 <see cref="LobbyTrainingDirector"/> 가 쓴다(커밋 ②부터 — 옛 10단계 모델 대체).
/// <list type="bullet">
/// <item>단계 = 단계표 행 번호. 앞으로만, 전이마다 <see cref="Revision"/> +1. 요청은 (runId, revision) 이 지금과 같을 때만 — 이전 회차·이전 단계 콜백·중복 보고를 거른다.</item>
/// <item>한 프레임 한 전이: 같은 프레임 두 번째 요청은 보관했다가 다음 프레임에 적용. 내용 컴포넌트는 <see cref="AcceptsEvent"/> 로 진입 프레임 이후 사건만 인정한다
/// (T31 성공이 같은 프레임에 T32 를 완료시키지 않게, 명세 §6).</item>
/// <item>참여자: 명단 고정 전(L00·L10) = 스폰 완료 접속자(<see cref="AddClient"/> 는 PlayerObject 가 선 뒤 부른다), <see cref="LockRoster"/> 뒤 = 고정 명단.
/// 참여자 0 이면 전이하지 않는다.</item>
/// <item>모든 기록(명단·라커 방문)은 clientId 기준 — 캐릭터 교체(PlayerObject 디스폰·재스폰)로 지워지지 않는다(PLAN §10.2). 연결 종료만 지운다.</item>
/// <item>라커 방문: LockerVisit 단계 동안 참여자만, 1인 1회 누적(나갔다 와도 유지). 회차 = 모델 인스턴스 — 새 회차는 새 인스턴스.
/// 완료 = 스폰 완료 참여자 전원 방문(<see cref="AllSpawnedParticipantsVisitedLocker"/>, 커밋 ③).</item>
/// <item>출격 확정: <see cref="TryCommitLaunch"/> 회차마다 1회. 확정 뒤엔 로딩 행으로만 전이한다(<see cref="LobbyTutorialAdvanceResult.LaunchLocked"/>).</item>
/// <item>재접속(할 일 157 ⑩, Q-T2 A): 연결 종료 때 <see cref="RemoveClient"/> 에 사용자 키(playerId)를 넘기면 그 사람의 명단 소속·라커 방문을 회차 안에 보관하고,
/// 같은 키로 새 clientId 가 접속하면 <see cref="TryRebind"/> 가 새 clientId 로 옮긴다(훈련 처음부터 재실행·다른 사람 기록 초기화 없음). 키가 없거나 보관 기록이 없으면 늦은 참가자.</item>
/// <item>튜토리얼 클리어 플래그(할 일 112, 로비 Alt+1 — <see cref="SetTrainingCleared"/>): 켠 사람은 명단 고정 전 참여자·고정 명단에서 빠지고(늦은 참가자처럼 로비 대기),
/// 출격 집결(접속자 전원)에는 남는다. 명단 고정 뒤 켜서 명단이 비면 진행기가 출격 집결로 건너뛴다(<see cref="TryAdvanceTo"/> ignoreParticipants).
/// 끄면 플래그만 지운다 — 이미 고정된 명단엔 다시 넣지 않는다. 플래그 장부는 진행기(서버 세션 정적)가 회차를 넘겨 잇는다.</item>
/// </list>
/// </summary>
public sealed class LobbyTutorialFlow
{
    public const int NoFrame = int.MinValue;

    readonly LobbyTutorialStepKind[] _kinds;
    readonly List<ulong> _connected = new List<ulong>();
    readonly List<ulong> _roster = new List<ulong>();
    readonly List<ulong> _lockerVisited = new List<ulong>();
    readonly List<ulong> _cleared = new List<ulong>();
    readonly List<ulong> _openParticipants = new List<ulong>();
    readonly Dictionary<string, DepartedRecord> _departed = new Dictionary<string, DepartedRecord>(StringComparer.Ordinal);

    int _lastTransitionFrame = NoFrame;
    bool _hasPending;
    int _pendingRevision;
    int _pendingTarget;
    double _pendingServerTime;
    bool _pendingIgnoreParticipants;

    LobbyTutorialSnapshot _snapshot;

    public LobbyTutorialFlow(IReadOnlyList<LobbyTutorialStepKind> kinds, int runId, double startServerTime = 0d)
    {
        if (kinds == null || kinds.Count == 0)
            throw new ArgumentException("단계표가 비었다.", nameof(kinds));
        if (kinds.Count > LobbyTutorialStepTable.MaxSteps)
            throw new ArgumentException($"단계 {kinds.Count}개 — 최대 {LobbyTutorialStepTable.MaxSteps}.", nameof(kinds));
        if (runId == 0)
            throw new ArgumentException("runId 0 은 '시작 전' 예약값.", nameof(runId));

        _kinds = new LobbyTutorialStepKind[kinds.Count];
        for (var i = 0; i < _kinds.Length; i++)
            _kinds[i] = kinds[i];

        _snapshot = new LobbyTutorialSnapshot
        {
            RunId = runId,
            Revision = 1,
            StepIndex = 0,
            EnteredAtServerTime = startServerTime,
        };
    }

    public LobbyTutorialSnapshot Snapshot => _snapshot;
    public int RunId => _snapshot.RunId;
    public int Revision => _snapshot.Revision;
    public int StepIndex => _snapshot.StepIndex;
    public int StepCount => _kinds.Length;
    public LobbyTutorialStepKind CurrentKind => _kinds[_snapshot.StepIndex];
    public bool IsLastStep => _snapshot.StepIndex == _kinds.Length - 1;

    /// <summary>현재 단계에 들어온 프레임. 시작 행은 <see cref="NoFrame"/>(어느 프레임 사건이든 인정).</summary>
    public int EnteredFrame { get; private set; } = NoFrame;

    public bool HasPendingAdvance => _hasPending;

    public IReadOnlyList<ulong> Connected => _connected;
    public IReadOnlyList<ulong> Roster => _roster;
    public bool IsRosterLocked { get; private set; }

    /// <summary>지금 판정 대상 — 명단 고정 전 = 접속자 중 클리어 플래그 끈 사람, 뒤 = 고정 명단.</summary>
    public IReadOnlyList<ulong> Participants => IsRosterLocked ? _roster : OpenParticipants();

    /// <summary>튜토리얼 클리어 플래그가 켜진 접속자(켠 순서).</summary>
    public IReadOnlyList<ulong> TrainingCleared => _cleared;

    public bool IsTrainingCleared(ulong clientId) => _cleared.Contains(clientId);

    public IReadOnlyList<ulong> LockerVisited => _lockerVisited;
    public bool LaunchCommitted => _snapshot.LaunchCommitted;

    public bool IsParticipant(ulong clientId) => Contains(Participants, clientId);

    /// <summary>명단 고정 뒤 접속해 로비에서 기다리는 사람(명단 밖 접속자).</summary>
    public bool IsWaitingLateJoiner(ulong clientId) => IsRosterLocked && _connected.Contains(clientId) && !_roster.Contains(clientId);

    // ── 참여자 ──

    /// <summary>스폰 완료 접속. 이미 있으면 false. 명단 고정 뒤면 늦은 참가자(명단 밖).</summary>
    public bool AddClient(ulong clientId)
    {
        if (_connected.Contains(clientId))
            return false;

        _connected.Add(clientId);
        return true;
    }

    /// <summary>
    /// 연결 종료 확정 — 접속자·명단·라커 방문 모두에서 뺀다(감소한 N 으로 호출 쪽 다음 폴링이 재평가). 없으면 false.
    /// <paramref name="playerKey"/>(재접속 식별 playerId)가 있으면 명단 소속·라커 방문을 보관해 <see cref="TryRebind"/> 로 되살릴 수 있게 한다.
    /// </summary>
    public bool RemoveClient(ulong clientId, string playerKey = null)
    {
        if (!string.IsNullOrEmpty(playerKey) && _connected.Contains(clientId))
            _departed[playerKey] = new DepartedRecord(clientId, _roster.Contains(clientId), _lockerVisited.Contains(clientId));

        var removed = _connected.Remove(clientId);
        removed |= _roster.Remove(clientId);
        _lockerVisited.Remove(clientId);
        _cleared.Remove(clientId);
        return removed;
    }

    /// <summary>
    /// 같은 사용자 재접속(새 clientId) — <paramref name="playerKey"/> 로 보관한 기록(명단 소속·라커 방문)을 <paramref name="clientId"/> 로 옮긴다.
    /// 새 clientId 가 접속자(<see cref="AddClient"/> 뒤)이고 보관 기록이 있을 때만 true(<paramref name="previousClientId"/> = 옛 clientId).
    /// 명단은 고정 뒤 소속이었을 때만 다시 넣는다(뒤에 붙임). 기록은 한 번 쓰면 지운다. false = 늦은 참가자 처리(변화 없음).
    /// </summary>
    public bool TryRebind(ulong clientId, string playerKey, out ulong previousClientId)
    {
        previousClientId = 0;
        if (string.IsNullOrEmpty(playerKey) || !_connected.Contains(clientId) || !_departed.TryGetValue(playerKey, out var record))
            return false;

        _departed.Remove(playerKey);
        previousClientId = record.ClientId;
        if (record.InRoster && IsRosterLocked && !_roster.Contains(clientId))
            _roster.Add(clientId);
        if (record.VisitedLocker && !_lockerVisited.Contains(clientId))
            _lockerVisited.Add(clientId);
        return true;
    }

    /// <summary>재접속 대기 기록이 있는가(테스트·로그용).</summary>
    public bool HasDepartedRecord(string playerKey) => !string.IsNullOrEmpty(playerKey) && _departed.ContainsKey(playerKey);

    /// <summary>훈련 명단 고정(L10 확정) — 지금 접속자 중 클리어 플래그 끈 사람으로. 이미 고정했으면 false.</summary>
    public bool LockRoster()
    {
        if (IsRosterLocked)
            return false;

        _roster.Clear();
        _roster.AddRange(OpenParticipants());
        IsRosterLocked = true;
        return true;
    }

    /// <summary>
    /// 튜토리얼 클리어 플래그를 켜고 끈다(접속자만). 바뀌었으면 true, 같은 값·접속자 아님이면 false.
    /// 켜면 고정 명단에서 바로 뺀다(<paramref name="leftRoster"/>). 명단이 비었는지(출격 집결 건너뛰기)는 호출 쪽이 본다.
    /// 끄면 플래그만 지운다 — 이미 고정된 명단엔 다시 넣지 않는다(명단 고정 전이면 다음 고정부터 포함).
    /// </summary>
    public bool SetTrainingCleared(ulong clientId, bool cleared, out bool leftRoster)
    {
        leftRoster = false;
        if (!_connected.Contains(clientId) || IsTrainingCleared(clientId) == cleared)
            return false;

        if (!cleared)
        {
            _cleared.Remove(clientId);
            return true;
        }

        _cleared.Add(clientId);
        leftRoster = _roster.Remove(clientId);
        return true;
    }

    // ── 라커 방문 ──

    /// <summary>
    /// 라커룸 내부 진입 기록 — LockerVisit 단계·참여자·처음일 때만 true. 진입 프레임에 이미 안에 있던 사람도 같은 호출로 센다(최초 스냅샷).
    /// </summary>
    public bool MarkLockerVisited(ulong clientId)
    {
        if (CurrentKind != LobbyTutorialStepKind.LockerVisit || !IsParticipant(clientId) || _lockerVisited.Contains(clientId))
            return false;

        _lockerVisited.Add(clientId);
        return true;
    }

    public bool HasVisitedLocker(ulong clientId) => _lockerVisited.Contains(clientId);

    /// <summary>참여자 전원 방문했는가. 참여자 0 이면 false.</summary>
    public bool AllParticipantsVisitedLocker()
    {
        var participants = Participants;
        if (participants.Count == 0)
            return false;

        foreach (var clientId in participants)
        {
            if (!_lockerVisited.Contains(clientId))
                return false;
        }

        return true;
    }

    /// <summary>
    /// 스폰 완료(PlayerObject 있음) 참여자 전원이 방문했는가(할 일 157 ③ — L00 참여자 = 스폰 완료 접속자).
    /// 로딩 중(아직 스폰 전) 접속자는 세지 않는다. 스폰 완료 참여자가 0 이면 false(N=0 전이 금지).
    /// </summary>
    public bool AllSpawnedParticipantsVisitedLocker(Predicate<ulong> isSpawned)
    {
        if (isSpawned == null)
            return AllParticipantsVisitedLocker();

        var counted = 0;
        foreach (var clientId in Participants)
        {
            if (!isSpawned(clientId))
                continue;

            counted++;
            if (!_lockerVisited.Contains(clientId))
                return false;
        }

        return counted > 0;
    }

    // ── 사건 검증 ──

    /// <summary>이 사건을 현재 단계가 받아도 되는가 — 같은 회차·같은 revision·진입 프레임 뒤(같은 프레임 연쇄 완료 금지).</summary>
    public bool AcceptsEvent(int runId, int revision, int eventFrame)
    {
        return runId == _snapshot.RunId && revision == _snapshot.Revision && (EnteredFrame == NoFrame || eventFrame > EnteredFrame);
    }

    // ── 전이 ──

    /// <summary>다음 행으로.</summary>
    public LobbyTutorialAdvanceResult TryAdvance(int runId, int revision, int frame, double serverTime) =>
        TryAdvanceTo(runId, revision, _snapshot.StepIndex + 1, frame, serverTime);

    /// <summary>
    /// <paramref name="targetIndex"/> 행으로(앞으로만 — 건너뛰기 등). (runId, revision) 이 지금과 다르면 거부.
    /// 이번 프레임에 이미 전이했으면 보관(<see cref="LobbyTutorialAdvanceResult.Deferred"/>) — 보관 중 새 요청은 덮어쓴다.
    /// <paramref name="ignoreParticipants"/> = 참여자 대신 접속자 수만 본다 — 대상 단계가 접속자 전원 단계(출격 집결·즉시 출격)일 때.
    /// </summary>
    public LobbyTutorialAdvanceResult TryAdvanceTo(int runId, int revision, int targetIndex, int frame, double serverTime,
        bool ignoreParticipants = false)
    {
        if (runId != _snapshot.RunId)
            return LobbyTutorialAdvanceResult.StaleRun;
        if (revision != _snapshot.Revision)
            return LobbyTutorialAdvanceResult.StaleRevision;
        if (targetIndex <= _snapshot.StepIndex)
            return LobbyTutorialAdvanceResult.Backward;
        if (targetIndex >= _kinds.Length)
            return LobbyTutorialAdvanceResult.OutOfRange;
        if (_snapshot.LaunchCommitted && _kinds[targetIndex] != LobbyTutorialStepKind.Loading)
            return LobbyTutorialAdvanceResult.LaunchLocked;
        if ((ignoreParticipants ? _connected.Count : Participants.Count) == 0)
            return LobbyTutorialAdvanceResult.NoParticipants;

        if (frame == _lastTransitionFrame)
        {
            _hasPending = true;
            _pendingRevision = revision;
            _pendingTarget = targetIndex;
            _pendingServerTime = serverTime;
            _pendingIgnoreParticipants = ignoreParticipants;
            return LobbyTutorialAdvanceResult.Deferred;
        }

        Enter(targetIndex, frame, serverTime);
        return LobbyTutorialAdvanceResult.Advanced;
    }

    /// <summary>매 프레임 — 보관한 전이를 적용한다. 적용했으면 true. 그사이 revision 이 바뀌었으면 버린다.</summary>
    public bool Tick(int frame)
    {
        if (!_hasPending || frame == _lastTransitionFrame)
            return false;

        _hasPending = false;
        return TryAdvanceTo(_snapshot.RunId, _pendingRevision, _pendingTarget, frame, _pendingServerTime, _pendingIgnoreParticipants)
            == LobbyTutorialAdvanceResult.Advanced;
    }

    /// <summary>대사 단계 시각(서버) — 현재 revision 일 때만. 스냅샷에 실린다.</summary>
    public bool SetTimer(int revision, double startServerTime, double endServerTime)
    {
        if (revision != _snapshot.Revision || endServerTime < startServerTime)
            return false;

        _snapshot.TimerStartServerTime = startServerTime;
        _snapshot.TimerEndServerTime = endServerTime;
        return true;
    }

    /// <summary>출격 확정 래치 — 회차마다 처음 한 번만 true.</summary>
    public bool TryCommitLaunch()
    {
        if (_snapshot.LaunchCommitted)
            return false;

        _snapshot.LaunchCommitted = true;
        return true;
    }

    void Enter(int targetIndex, int frame, double serverTime)
    {
        _snapshot.StepIndex = (byte)targetIndex;
        _snapshot.Revision = _snapshot.Revision >= int.MaxValue - 1 ? 1 : _snapshot.Revision + 1;
        _snapshot.EnteredAtServerTime = serverTime;
        _snapshot.TimerStartServerTime = 0d;
        _snapshot.TimerEndServerTime = 0d;
        _lastTransitionFrame = frame;
        EnteredFrame = frame;
        _hasPending = false;
    }

    List<ulong> OpenParticipants()
    {
        _openParticipants.Clear();
        foreach (var clientId in _connected)
        {
            if (!_cleared.Contains(clientId))
                _openParticipants.Add(clientId);
        }

        return _openParticipants;
    }

    readonly struct DepartedRecord
    {
        public readonly ulong ClientId;
        public readonly bool InRoster;
        public readonly bool VisitedLocker;

        public DepartedRecord(ulong clientId, bool inRoster, bool visitedLocker)
        {
            ClientId = clientId;
            InRoster = inRoster;
            VisitedLocker = visitedLocker;
        }
    }

    static bool Contains(IReadOnlyList<ulong> list, ulong clientId)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == clientId)
                return true;
        }

        return false;
    }
}
