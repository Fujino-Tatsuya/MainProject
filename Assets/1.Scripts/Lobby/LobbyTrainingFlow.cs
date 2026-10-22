using System;
using System.Collections.Generic;

/// <summary>로비 훈련 단계(할 일 86 PLAN §3.2) — 앞으로만 진행한다. 복제용이라 byte.</summary>
public enum LobbyTrainingStage : byte
{
    Lobby = 0,
    Train01 = 1,
    Train02 = 2,
    Train03A = 3,
    Train03B = 4,
    Train03C = 5,
    Train03D = 6,
    TrainEnd = 7,
    Brief = 8,
    Sortie = 9,
}

/// <summary>
/// 단계 안 하위 상태. Gathering = 시작 위치 전원 집결 대기, InProgress = 진행 중(내용은 외부 — 단계 완료 보고로 끝난다),
/// Completed = 끝났고 다음 전이 조건을 기다린다(Train03 A/B = 영역 안 확인, Brief = 출격 집결).
/// </summary>
public enum LobbyTrainingPhase : byte
{
    Gathering = 0,
    InProgress = 1,
    Completed = 2,
}

/// <summary>로비 훈련 진행기가 켜고 끄는 집결 구역(씬의 <see cref="PartyGatherZone"/> 하나씩).</summary>
public enum LobbyGatherPoint : byte
{
    None = 0,
    Entrance = 1,       // 훈련장 입구 — 훈련 시작(선택)
    Sortie = 2,         // 출격 — 메인 진입(옛 Ready 패드 자리)
    Train01Start = 3,
    Train02Start = 4,
    Train03Area = 5,    // 세 번째 훈련 시작 집결 + A→B·B→C 영역 안 확인
    TrainEnd = 6,       // 종료 집결(훈련실 출구)
    Brief = 7,          // 브리핑 위치
}

/// <summary>
/// [서버] 로비 훈련 단계 모델 — 할 일 86 PLAN §3.2·§7, 명세 v0.2 §3·§5·§10. NGO·MonoBehaviour 의존 없는 순수 상태(EditMode 테스트 대상).
/// <list type="bullet">
/// <item>단계: Lobby → Train01 → Train02 → Train03A → B → C → D → TrainEnd → Brief → Sortie. 앞으로만, 전이마다 1회. 같은 전이 중복·역방향·현재가 아닌 단계 보고는 무시(false).</item>
/// <item>훈련은 선택(§7-2): Lobby 에선 입구·출격 집결이 둘 다 켜지고, 출격 확정이면 훈련 없이 바로 Sortie. 입구 확정 뒤엔 출격 비활성, 브리핑 완료 뒤 다시 활성.</item>
/// <item>입구 확정 ≠ TRAIN_01 시작 — 첫 위치(Train01Start) 전원 집결이 시작 조건(명세 §5). Train02·Train03A·Brief 도 시작 위치 집결 뒤 시작.</item>
/// <item>Train03 A→B·B→C = 완료 뒤 영역 안 확인(카운트다운 없음, <see cref="IsAreaCheckOnly"/>). C→D = 완료 보고 즉시(집결 재검사 없음).</item>
/// <item>명단(§7-2): 기본 = 접속자 전원. 훈련 참여 명단은 입구 확정 순간 접속자로 고정 — 훈련·브리핑 중 접속자는 로비 대기(훈련 내부 집결 인원 아님),
/// 출격 집결 인원에는 포함. 연결 종료자는 모든 명단에서 뺀다.</item>
/// <item>판 끝 로비 복귀 = 새 회차: <see cref="Reset"/> 으로 Lobby 부터(기록·명단 초기화).</item>
/// </list>
/// </summary>
public sealed class LobbyTrainingFlow
{
    readonly List<ulong> _connected = new List<ulong>();
    readonly List<ulong> _trainingRoster = new List<ulong>();

    public LobbyTrainingStage Stage { get; private set; } = LobbyTrainingStage.Lobby;

    public LobbyTrainingPhase Phase { get; private set; } = LobbyTrainingPhase.Gathering;

    /// <summary>접속자 전원(입장 순서).</summary>
    public IReadOnlyList<ulong> Connected => _connected;

    /// <summary>훈련 참여 명단 — 입구 확정 때 고정. 그 전(Lobby)·훈련 없이 출격한 경우 비어 있다.</summary>
    public IReadOnlyList<ulong> TrainingRoster => _trainingRoster;

    /// <summary>입구 집결로 훈련을 시작했다(이번 회차).</summary>
    public bool HasStartedTraining { get; private set; }

    /// <summary>캐릭터 변경 잠금 — 입구 확정 뒤 이번 회차 끝까지(명세 §5·§10). 훈련 없이 출격해도 잠근다(메인 진입).</summary>
    public bool IsCharacterLocked => Stage != LobbyTrainingStage.Lobby;

    /// <summary>훈련 중(입구 확정 ~ 브리핑 완료 전) — 늦은 참가자가 로비에서 기다리는 구간.</summary>
    public bool IsTrainingInProgress => HasStartedTraining
        && Stage != LobbyTrainingStage.Sortie
        && !(Stage == LobbyTrainingStage.Brief && Phase == LobbyTrainingPhase.Completed);

    /// <summary>지금 켜져 있어야 하는 구역이 영역 안 확인 전용인가(유지 시간 0 — Train03 A→B·B→C).</summary>
    public bool IsAreaCheckOnly => Phase == LobbyTrainingPhase.Completed
        && (Stage == LobbyTrainingStage.Train03A || Stage == LobbyTrainingStage.Train03B);

    /// <summary>새 회차 — Lobby 단계부터, 훈련 기록·명단 초기화. 접속자 목록은 유지(같은 방).</summary>
    public void Reset()
    {
        Stage = LobbyTrainingStage.Lobby;
        Phase = LobbyTrainingPhase.Gathering;
        HasStartedTraining = false;
        _trainingRoster.Clear();
    }

    /// <summary>접속. Lobby 단계면 다음 입구 확정 때 훈련 명단에 들어간다. 훈련 중이면 로비 대기(훈련 명단 밖). 이미 있으면 false.</summary>
    public bool AddClient(ulong clientId)
    {
        if (_connected.Contains(clientId))
            return false;

        _connected.Add(clientId);
        return true;
    }

    /// <summary>연결 종료 확정 — 접속자·훈련 명단 모두에서 뺀다. 없으면 false.</summary>
    public bool RemoveClient(ulong clientId)
    {
        var removed = _connected.Remove(clientId);
        removed |= _trainingRoster.Remove(clientId);
        return removed;
    }

    /// <summary>훈련·브리핑 중 접속해 로비에서 기다리는 사람(접속자인데 훈련 명단 밖).</summary>
    public bool IsWaitingLateJoiner(ulong clientId)
    {
        return HasStartedTraining && _connected.Contains(clientId) && !_trainingRoster.Contains(clientId);
    }

    /// <summary>이 구역의 참여자가 접속자 전원인가(입구·출격). 아니면 훈련 명단.</summary>
    public static bool UsesConnectedClients(LobbyGatherPoint point)
    {
        return point == LobbyGatherPoint.Entrance || point == LobbyGatherPoint.Sortie;
    }

    /// <summary>이 구역 참여자 명단.</summary>
    public IReadOnlyList<ulong> ParticipantsFor(LobbyGatherPoint point)
    {
        return UsesConnectedClients(point) ? _connected : _trainingRoster;
    }

    /// <summary>지금 켜져 있어야 하는 구역인가.</summary>
    public bool IsPointActive(LobbyGatherPoint point)
    {
        switch (Stage)
        {
            case LobbyTrainingStage.Lobby:
                return point == LobbyGatherPoint.Entrance || point == LobbyGatherPoint.Sortie;
            case LobbyTrainingStage.Train01:
                return point == LobbyGatherPoint.Train01Start && Phase == LobbyTrainingPhase.Gathering;
            case LobbyTrainingStage.Train02:
                return point == LobbyGatherPoint.Train02Start && Phase == LobbyTrainingPhase.Gathering;
            case LobbyTrainingStage.Train03A:
            case LobbyTrainingStage.Train03B:
                return point == LobbyGatherPoint.Train03Area
                    && (Phase == LobbyTrainingPhase.Gathering || Phase == LobbyTrainingPhase.Completed);
            case LobbyTrainingStage.TrainEnd:
                return point == LobbyGatherPoint.TrainEnd && Phase == LobbyTrainingPhase.Gathering;
            case LobbyTrainingStage.Brief:
                return Phase == LobbyTrainingPhase.Gathering ? point == LobbyGatherPoint.Brief
                    : Phase == LobbyTrainingPhase.Completed && point == LobbyGatherPoint.Sortie;
            default:
                return false;
        }
    }

    /// <summary>
    /// 구역 집결 확정. 지금 그 구역이 의미 있는 상태일 때만 전이하고 true. 비활성 구역·중복 확정은 무시(false).
    /// </summary>
    public bool OnGatherConfirmed(LobbyGatherPoint point)
    {
        if (point == LobbyGatherPoint.None || !IsPointActive(point))
            return false;

        switch (point)
        {
            case LobbyGatherPoint.Entrance:
                HasStartedTraining = true;
                _trainingRoster.Clear();
                _trainingRoster.AddRange(_connected);
                Set(LobbyTrainingStage.Train01, LobbyTrainingPhase.Gathering);
                return true;

            case LobbyGatherPoint.Sortie:
                Set(LobbyTrainingStage.Sortie, LobbyTrainingPhase.InProgress);
                return true;

            case LobbyGatherPoint.Train01Start:
            case LobbyGatherPoint.Train02Start:
            case LobbyGatherPoint.Brief:
                Set(Stage, LobbyTrainingPhase.InProgress);
                return true;

            case LobbyGatherPoint.Train03Area:
                if (Phase == LobbyTrainingPhase.Gathering)
                    Set(Stage, LobbyTrainingPhase.InProgress);
                else
                    Set(Stage == LobbyTrainingStage.Train03A ? LobbyTrainingStage.Train03B : LobbyTrainingStage.Train03C,
                        LobbyTrainingPhase.InProgress);
                return true;

            case LobbyGatherPoint.TrainEnd:
                Set(LobbyTrainingStage.Brief, LobbyTrainingPhase.Gathering);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// 단계 완료 보고(훈련 내용 T3~T6 이 부른다). <paramref name="stage"/> 가 현재 단계이고 진행 중일 때만 전이하고 true —
    /// 다른 단계·집결 대기 중·이미 완료·중복 보고는 무시(false). Lobby·TrainEnd·Sortie 는 보고로 끝나지 않는다(집결로 끝난다).
    /// </summary>
    public bool ReportStageCompleted(LobbyTrainingStage stage)
    {
        if (stage != Stage || Phase != LobbyTrainingPhase.InProgress)
            return false;

        switch (stage)
        {
            case LobbyTrainingStage.Train01:
                Set(LobbyTrainingStage.Train02, LobbyTrainingPhase.Gathering);
                return true;
            case LobbyTrainingStage.Train02:
                Set(LobbyTrainingStage.Train03A, LobbyTrainingPhase.Gathering);
                return true;
            case LobbyTrainingStage.Train03A:
            case LobbyTrainingStage.Train03B:
            case LobbyTrainingStage.Brief:
                Set(stage, LobbyTrainingPhase.Completed);
                return true;
            case LobbyTrainingStage.Train03C:
                Set(LobbyTrainingStage.Train03D, LobbyTrainingPhase.InProgress);   // 제압 시작 순간 — 집결 재검사 없음
                return true;
            case LobbyTrainingStage.Train03D:
                Set(LobbyTrainingStage.TrainEnd, LobbyTrainingPhase.Gathering);
                return true;
            default:
                return false;
        }
    }

    void Set(LobbyTrainingStage stage, LobbyTrainingPhase phase)
    {
        if (stage < Stage)
            throw new InvalidOperationException($"로비 훈련 단계 역행 {Stage} → {stage}");

        Stage = stage;
        Phase = phase;
    }
}
