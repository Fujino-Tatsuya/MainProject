using System;
using System.Collections.Generic;

/// <summary>
/// 로비 무전 대사 id(명세 v0.2 §14 표의 대사 ID 그대로). 문구·지속시간은 <c>TutorialGuideTexts</c> SO 행(ko 폴백) +
/// Strings 키 <c>Tutorial.Guide.&lt;id&gt;</c>(Export 가 SO 행에서 모은다). 행 추가 = 메뉴 <c>Tools/UI/로비 무전 문구 추가</c>.
/// </summary>
public static class LobbyRadioIds
{
    public const string LobbyTrainRoute = "LOBBY_TRAIN_ROUTE";
    public const string Train01Start = "TRAIN_01_START";
    public const string Train01End = "TRAIN_01_END";
    public const string Train02Start = "TRAIN_02_START";
    public const string Train02End = "TRAIN_02_END";
    public const string Train03Start = "TRAIN_03_START";
    public const string Train03Counter = "TRAIN_03_COUNTER";   // 단계 안 사건 — LobbyRadio.ServerPlay(레인 B T5c)
    public const string Train03Cc = "TRAIN_03_CC";
    public const string Train03Reset = "TRAIN_03_RESET";
    public const string Train03WallGuide = "TRAIN_03_WALL_GUIDE";
    public const string Train03Wall = "TRAIN_03_WALL";         // 단계 안 사건 — LobbyRadio.ServerPlay(레인 B T5c)
    public const string Train03Suppress = "TRAIN_03_SUPPRESS";
    public const string Train03Combat = "TRAIN_03_COMBAT";     // 단계 안 사건 — LobbyRadio.ServerPlay(레인 B T5c)
    public const string TrainEnd = "TRAIN_END";
    public const string BriefRoute = "BRIEF_ROUTE";
    public const string Brief01 = "BRIEF_01";                  // 브리핑 — 재생은 T6c
    public const string Brief02 = "BRIEF_02";
    public const string Brief03 = "BRIEF_03";
    public const string Brief04 = "BRIEF_04";

    /// <summary>로비 무전 id 전부(표 순서).</summary>
    public static readonly string[] All =
    {
        LobbyTrainRoute, Train01Start, Train01End, Train02Start, Train02End,
        Train03Start, Train03Counter, Train03Cc, Train03Reset, Train03WallGuide, Train03Wall, Train03Suppress, Train03Combat,
        TrainEnd, BriefRoute, Brief01, Brief02, Brief03, Brief04,
    };

    public static bool IsLobbyRadio(string id) => !string.IsNullOrEmpty(id) && Array.IndexOf(All, id) >= 0;
}

/// <summary>
/// 단계 진입 대사 표(Docs/tech/lobby-training.md "로비 무전" 절). 앞으로만 진행하는 단계라 (단계, 하위 상태) 진입 = 전이 하나와 1:1 이다.
/// 한 진입에 여러 줄이면 배열 순서대로 큐에 들어간다(B→C = 초기화 안내 → 벽 안내).
/// </summary>
public static class LobbyRadioSchedule
{
    static readonly string[] None = Array.Empty<string>();

    public static IReadOnlyList<string> LinesOnEnter(LobbyTrainingStage stage, LobbyTrainingPhase phase)
    {
        switch (stage)
        {
            case LobbyTrainingStage.Train01:
                // Gathering = 입구 집결 확정 직후(Lobby → Train01) · InProgress = 첫 위치 집결 확정(무기 점검 시작)
                return phase == LobbyTrainingPhase.Gathering ? new[] { LobbyRadioIds.LobbyTrainRoute }
                    : phase == LobbyTrainingPhase.InProgress ? new[] { LobbyRadioIds.Train01Start } : None;
            case LobbyTrainingStage.Train02:
                // Gathering = Train01 전원 완료 보고 직후 · InProgress = 두 번째 위치 집결 확정(몹 스폰)
                return phase == LobbyTrainingPhase.Gathering ? new[] { LobbyRadioIds.Train01End }
                    : phase == LobbyTrainingPhase.InProgress ? new[] { LobbyRadioIds.Train02Start } : None;
            case LobbyTrainingStage.Train03A:
                // Gathering = Train02 완료 보고 직후 · InProgress = 세 번째 위치 집결 확정(A 시작)
                return phase == LobbyTrainingPhase.Gathering ? new[] { LobbyRadioIds.Train02End }
                    : phase == LobbyTrainingPhase.InProgress ? new[] { LobbyRadioIds.Train03Start } : None;
            case LobbyTrainingStage.Train03B:
                return phase == LobbyTrainingPhase.InProgress ? new[] { LobbyRadioIds.Train03Cc } : None;
            case LobbyTrainingStage.Train03C:
                // B 완료 뒤 영역 안 확인 = B→C 명시 초기화 순간(명세 §9.4) — 초기화 안내 → C 안내.
                return phase == LobbyTrainingPhase.InProgress
                    ? new[] { LobbyRadioIds.Train03Reset, LobbyRadioIds.Train03WallGuide } : None;
            case LobbyTrainingStage.Train03D:
                return phase == LobbyTrainingPhase.InProgress ? new[] { LobbyRadioIds.Train03Suppress } : None;
            case LobbyTrainingStage.TrainEnd:
                return phase == LobbyTrainingPhase.Gathering ? new[] { LobbyRadioIds.TrainEnd } : None;
            case LobbyTrainingStage.Brief:
                // 진입 대사 없음 — 종료 텔레포트로 이미 브리핑 구역 안이라 이동 안내(BRIEF_ROUTE) 불필요(할 일 111, 키·문구는 남김).
                // 브리핑 본문(BRIEF_01~04)은 T6c.
                return None;
            default:
                return None;
        }
    }
}

/// <summary>
/// [서버] 로비 무전 재생 결정(순수, EditMode 테스트 대상). 진행기 상태가 바뀔 때마다 <see cref="OnFlowChanged"/> 를 부른다.
/// <list type="bullet">
/// <item>처음 본 상태는 기준만 잡고 대사 없음 — 진행기 스폰 직후(Lobby)·늦게 붙은 구독이 지난 대사를 재생하지 않는다.</item>
/// <item>단계(<see cref="LobbyTrainingStage"/>)가 바뀌면 <see cref="StageSerial"/> 을 올린다 — 클라는 더 큰 번호를 받으면 이전 단계의 미재생 대사를 버린다.
/// 하위 상태만 바뀐 건 같은 단계라 버리지 않는다.</item>
/// <item>같은 id 는 회차당 1회(단계 진입 대사 + <see cref="TryPlay"/> 의 oncePerRound). 새 회차 = <see cref="Reset"/>(판 끝 로비 복귀 = 새 진행기라 새 모델).</item>
/// </list>
/// </summary>
public sealed class LobbyRadioModel
{
    readonly HashSet<string> _played = new HashSet<string>(StringComparer.Ordinal);
    bool _hasState;
    LobbyTrainingStage _stage;
    LobbyTrainingPhase _phase;

    /// <summary>단계 번호 — 단계가 바뀔 때마다 1씩. 대사 RPC 에 같이 싣는다.</summary>
    public int StageSerial { get; private set; }

    public bool HasPlayed(string id) => !string.IsNullOrEmpty(id) && _played.Contains(id);

    /// <summary>
    /// 진행기 상태 반영. <paramref name="lines"/> 에 이번에 틀 대사(이미 튼 id 제외)를 순서대로 채운다.
    /// 반환 true = 단계가 바뀌었다(<see cref="StageSerial"/> 증가 — 미재생 폐기를 보낸다).
    /// </summary>
    public bool OnFlowChanged(LobbyTrainingStage stage, LobbyTrainingPhase phase, List<string> lines)
    {
        lines?.Clear();
        if (!_hasState)
        {
            _hasState = true;
            _stage = stage;
            _phase = phase;
            return false;
        }

        if (stage == _stage && phase == _phase)
            return false;

        var stageChanged = stage != _stage;
        if (stageChanged)
            StageSerial++;

        _stage = stage;
        _phase = phase;

        foreach (var id in LobbyRadioSchedule.LinesOnEnter(stage, phase))
        {
            if (_played.Add(id))
                lines?.Add(id);
        }

        return stageChanged;
    }

    /// <summary>단계 안 사건 대사. oncePerRound 면 이번 회차에 이미 튼 id 는 false. 빈 id 는 false.</summary>
    public bool TryPlay(string id, bool oncePerRound)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        if (oncePerRound)
            return _played.Add(id);

        _played.Add(id);
        return true;
    }

    /// <summary>새 회차 — 튼 기록·기준 상태를 비운다. 단계 번호는 계속 올려 클라 대기 줄을 버리게 한다.</summary>
    public void Reset()
    {
        _played.Clear();
        _hasState = false;
        StageSerial++;
    }
}

/// <summary>
/// [클라] 로비 무전 수신 필터(순수). 서버 단계 번호보다 오래된 대사는 버리고, 더 큰 번호가 오면 대기 줄을 먼저 비우라고 알려 준다.
/// 늦게 붙은 클라는 0 에서 시작해 다음 대사부터만 받는다(지난 대사 재생 없음 — 서버가 다시 보내지 않는다).
/// </summary>
public sealed class LobbyRadioReceiver
{
    public int StageSerial { get; private set; }

    /// <summary>단계 바뀜 신호. 반환 true = 이전 단계의 미재생 대사를 버린다.</summary>
    public bool OnStageAdvanced(int serial)
    {
        if (serial <= StageSerial)
            return false;

        StageSerial = serial;
        return true;
    }

    /// <summary>대사 수신. 반환 false = 지난 단계 대사라 버린다. discardPendingFirst = 큐에 넣기 전에 대기 줄을 비운다.</summary>
    public bool AcceptLine(int serial, out bool discardPendingFirst)
    {
        discardPendingFirst = false;
        if (serial < StageSerial)
            return false;

        discardPendingFirst = OnStageAdvanced(serial);
        return true;
    }
}
