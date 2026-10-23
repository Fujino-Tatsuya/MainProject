using System;
using System.Collections.Generic;

/// <summary>
/// 로비 목표 패널 id(할 일 86 T6c) — 문구는 <c>TutorialGuideTexts</c> SO 행(ko 폴백) + Strings 키 <c>Tutorial.Guide.&lt;id&gt;</c>(Export 가 SO 행에서 모은다).
/// 행 추가 = 메뉴 <c>Tools/UI/로비 무전 문구 추가</c>(무전 행과 함께).
/// </summary>
public static class LobbyObjectiveIds
{
    public const string Briefing = "LOBBY_OBJ_BRIEFING";   // 브리핑 진행 중
    public const string Sortie = "LOBBY_OBJ_SORTIE";       // 브리핑 끝 — 출격 지점에 집결

    public static readonly string[] All = { Briefing, Sortie };

    public static bool IsLobbyObjective(string id) => !string.IsNullOrEmpty(id) && Array.IndexOf(All, id) >= 0;
}

/// <summary>브리핑 대사 한 줄 — id + 표시 시간(초). 0 이하면 자막 큐 기본값(<see cref="LobbyBriefingRules.DefaultLineSeconds"/>).</summary>
public readonly struct LobbyBriefingLine
{
    public LobbyBriefingLine(string id, float seconds)
    {
        Id = id;
        Seconds = seconds > 0f ? seconds : LobbyBriefingRules.DefaultLineSeconds;
    }

    public string Id { get; }
    public float Seconds { get; }
}

/// <summary>브리핑 표시 규칙(순수) — 목표 패널·카메라. 전 피어가 복제된 진행기 단계로 같은 판정을 한다.</summary>
public static class LobbyBriefingRules
{
    /// <summary>표시 시간이 비었을 때 — <see cref="TutorialSubtitleQueue.DefaultDurationSeconds"/> 와 같은 값.</summary>
    public const float DefaultLineSeconds = 6f;

    /// <summary>브리핑 대사(명세 v0.2 §14 순서).</summary>
    public static readonly string[] LineIds =
    {
        LobbyRadioIds.Brief01, LobbyRadioIds.Brief02, LobbyRadioIds.Brief03, LobbyRadioIds.Brief04,
    };

    /// <summary>브리핑 진행 중(조작 제한·카메라·'작전 설명 중') — Brief/InProgress.</summary>
    public static bool IsBriefing(LobbyTrainingStage stage, LobbyTrainingPhase phase) =>
        stage == LobbyTrainingStage.Brief && phase == LobbyTrainingPhase.InProgress;

    /// <summary>목표 패널 id(없으면 null). 브리핑 중 = '작전 설명 중', 브리핑 완료 = '출격 지점에 집결'. 그 밖 단계는 비운다.</summary>
    public static string ObjectiveFor(LobbyTrainingStage stage, LobbyTrainingPhase phase)
    {
        if (stage != LobbyTrainingStage.Brief)
            return null;

        switch (phase)
        {
            case LobbyTrainingPhase.InProgress: return LobbyObjectiveIds.Briefing;
            case LobbyTrainingPhase.Completed: return LobbyObjectiveIds.Sortie;
            default: return null;
        }
    }

    /// <summary>브리핑 전체 시간 = 시작 지연 + 대사 표시 시간 합 + 끝 여유.</summary>
    public static float TotalSeconds(IReadOnlyList<LobbyBriefingLine> lines, float leadSeconds, float tailSeconds)
    {
        var total = Math.Max(0f, leadSeconds) + Math.Max(0f, tailSeconds);
        if (lines == null)
            return total;

        for (var i = 0; i < lines.Count; i++)
            total += lines[i].Seconds;
        return total;
    }
}

/// <summary>브리핑 진행 상태. 한 진행기(회차)에 한 번 — Completed·Aborted 뒤엔 다시 시작하지 않는다.</summary>
public enum LobbyBriefingState : byte
{
    Idle = 0,
    Running = 1,
    Completed = 2,
    Aborted = 3,
}

/// <summary>
/// [서버] 브리핑 진행 모델(순수, EditMode 테스트 대상) — 할 일 86 T6c, 명세 v0.2 §4·§10.
/// <list type="bullet">
/// <item>시간은 서버가 잰다: 시작 지연 뒤 대사를 순서대로 하나씩 내보내고(앞 줄 표시 시간이 지나면 다음 줄), 마지막 줄 + 끝 여유가 지나면 완료 1회.
/// 클라 자막 종료 보고에 의존하지 않는다.</item>
/// <item>중복 시작 무시(Running·Completed·Aborted 에서 <see cref="Start"/> = false). 완료 신호는 <see cref="Tick"/> 이 정확히 한 번 true.</item>
/// <item>중단(<see cref="Abort"/>) = 진행 중이었을 때만 true — 조작 제한·카메라 해제 신호. 중단 뒤 남은 대사·완료는 나오지 않는다.</item>
/// <item><see cref="HoldsLock"/> = 조작 제한을 걸고 있어야 하는가(Running 일 때만).</item>
/// </list>
/// </summary>
public sealed class LobbyBriefingModel
{
    readonly List<LobbyBriefingLine> _lines = new List<LobbyBriefingLine>();
    float _lead;
    float _tail;
    int _nextLine;
    float _nextAt;

    public LobbyBriefingState State { get; private set; } = LobbyBriefingState.Idle;

    public bool IsRunning => State == LobbyBriefingState.Running;

    /// <summary>조작 제한(입력 잠금·카메라)을 걸고 있어야 한다.</summary>
    public bool HoldsLock => State == LobbyBriefingState.Running;

    /// <summary>시작 뒤 지난 시간(초).</summary>
    public float Elapsed { get; private set; }

    /// <summary>브리핑 전체 시간(초) — <see cref="LobbyBriefingRules.TotalSeconds"/>.</summary>
    public float TotalSeconds { get; private set; }

    /// <summary>지금까지 내보낸 대사 수.</summary>
    public int SentCount => _nextLine;

    public IReadOnlyList<LobbyBriefingLine> Lines => _lines;

    /// <summary>시작. Idle 일 때만 true — 진행 중·끝난 뒤 다시 부르면 무시(false).</summary>
    public bool Start(IReadOnlyList<LobbyBriefingLine> lines, float leadSeconds, float tailSeconds)
    {
        if (State != LobbyBriefingState.Idle)
            return false;

        _lines.Clear();
        if (lines != null)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (!string.IsNullOrEmpty(lines[i].Id))
                    _lines.Add(lines[i]);
            }
        }

        _lead = Math.Max(0f, leadSeconds);
        _tail = Math.Max(0f, tailSeconds);
        _nextLine = 0;
        _nextAt = _lead;
        Elapsed = 0f;
        TotalSeconds = LobbyBriefingRules.TotalSeconds(_lines, _lead, _tail);
        State = LobbyBriefingState.Running;
        return true;
    }

    /// <summary>
    /// 시간 진행. <paramref name="dueLines"/> 에 이번에 내보낼 대사 id 를 순서대로 채운다(한 번에 여러 줄도 가능 — 큰 dt).
    /// 반환 true = 방금 완료(정확히 한 번). 진행 중이 아니면 아무것도 하지 않고 false.
    /// </summary>
    public bool Tick(float deltaSeconds, List<string> dueLines)
    {
        dueLines?.Clear();
        if (State != LobbyBriefingState.Running)
            return false;

        Elapsed += Math.Max(0f, deltaSeconds);
        while (_nextLine < _lines.Count && Elapsed >= _nextAt)
        {
            dueLines?.Add(_lines[_nextLine].Id);
            _nextAt += _lines[_nextLine].Seconds;
            _nextLine++;
        }

        if (_nextLine < _lines.Count || Elapsed < TotalSeconds)
            return false;

        State = LobbyBriefingState.Completed;
        return true;
    }

    /// <summary>중단(단계 이탈·회차 초기화·디스폰). 진행 중이었으면 true — 조작 제한·카메라를 풀어야 한다.</summary>
    public bool Abort()
    {
        if (State != LobbyBriefingState.Running)
            return false;

        State = LobbyBriefingState.Aborted;
        return true;
    }
}

/// <summary>
/// [서버] 브리핑이 건 입력 잠금 장부(순수) — "내가 건 것만 푼다". 이미 다른 계통(보스 등장 연출 등)이 잠근 사람은 장부에 넣지 않아
/// 브리핑이 끝나도 그 잠금을 풀지 않는다. 접속 끊김은 <see cref="Forget"/>(오브젝트와 함께 잠금도 사라진다).
/// </summary>
public sealed class LobbyBriefingLockLedger
{
    readonly List<ulong> _held = new List<ulong>();

    public int Count => _held.Count;

    public bool Holds(ulong clientId) => _held.Contains(clientId);

    /// <summary>
    /// 잠글지 판정. 이미 내 장부에 있거나 다른 계통이 잠가 둔 사람(<paramref name="lockedByOther"/>)이면 false.
    /// true 면 호출자가 잠그고 <see cref="Record"/> 한다.
    /// </summary>
    public bool ShouldLock(ulong clientId, bool lockedByOther) => !_held.Contains(clientId) && !lockedByOther;

    public void Record(ulong clientId)
    {
        if (!_held.Contains(clientId))
            _held.Add(clientId);
    }

    /// <summary>장부에서만 뺀다(접속 끊김 — 플레이어 오브젝트와 함께 잠금도 사라진다). 있었으면 true.</summary>
    public bool Forget(ulong clientId) => _held.Remove(clientId);

    /// <summary>풀 대상 전부를 <paramref name="into"/> 에 옮기고 장부를 비운다.</summary>
    public void TakeAll(List<ulong> into)
    {
        into.Clear();
        into.AddRange(_held);
        _held.Clear();
    }
}
