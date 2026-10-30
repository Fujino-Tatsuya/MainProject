using System.Collections.Generic;

/// <summary>
/// 로비 목표 패널 id(할 일 86 T6c) — 문구는 <c>TutorialGuideTexts</c> SO 행(ko 폴백) + Strings 키 <c>Tutorial.Guide.&lt;id&gt;</c>(Export 가 SO 행에서 모은다).
/// 할 일 157 ⑨부터 아무도 띄우지 않는다(목표는 단계표 키 — 158·과도기 어댑터). 키·문구 행은 보존한다(명세 §3.3 '브리핑 진행 중' 표시 안 함).
/// </summary>
public static class LobbyObjectiveIds
{
    public const string Briefing = "LOBBY_OBJ_BRIEFING";   // 브리핑 진행 중 — 표시 안 함(키 보존)
    public const string Sortie = "LOBBY_OBJ_SORTIE";       // 브리핑 끝 — 출격 지점에 집결(옛 표시, 지금은 단계표 LOBBY_OBJ_SORTIE_GATHER)

    public static readonly string[] All = { Briefing, Sortie };

    public static bool IsLobbyObjective(string id) => !string.IsNullOrEmpty(id) && System.Array.IndexOf(All, id) >= 0;
}

/// <summary>브리핑 판정(순수) — 전 피어가 복제된 진행기 단계로 같은 판정을 한다(입력 잠금·카메라).</summary>
public static class LobbyBriefingRules
{
    /// <summary>
    /// 브리핑 묶음(입력 잠금·카메라 임시 추적) — 입력 잠금 대사 행(L31~L34) 어디든. 할 일 157 ⑨: 대사 4행은 진행기 공용 대사 스케줄러가 서버 시각으로
    /// 한 행씩 넘기고, 잠금은 묶음 첫 행 진입 때 걸어 묶음을 떠날 때(L40·회차 초기화) 푼다 — 행이 바뀌어도 풀었다 다시 걸지 않는다.
    /// </summary>
    public static bool IsBriefing(in LobbyTutorialStepView step) =>
        step.IsValid && step.Kind == LobbyTutorialStepKind.Dialogue && step.LocksInput;
}

/// <summary><see cref="LobbyBriefingLockSession.Update"/> 결과.</summary>
public enum LobbyBriefingLockAction : byte
{
    None = 0,
    /// <summary>묶음에 들어왔다 — 접속자 전원 잠금(기존 무전 종료).</summary>
    Begin = 1,
    /// <summary>묶음을 떠났다(정상 L40·단계 이탈·디스폰) — 장부의 잠금을 전부 푼다.</summary>
    Release = 2,
}

/// <summary>
/// [서버] 브리핑 잠금 구간(순수, EditMode 테스트 대상) — 할 일 157 ⑨, Q7 A. 단계가 바뀔 때마다 <see cref="Update"/>(지금 행이 브리핑인가)를 부르면
/// 묶음 진입 1회 Begin, 묶음 안 행 이동(L31→L34)은 None, 묶음 밖으로 나가면 1회 Release. <see cref="End"/> = 디스폰·연결 종료 등 강제 종료(잡고 있었으면 true).
/// 회차(진행기)당 여러 번 들어와도(회차 초기화 뒤 재진입 등) 매번 짝이 맞는다.
/// </summary>
public sealed class LobbyBriefingLockSession
{
    /// <summary>잠금을 잡고 있다(L31~L34 안).</summary>
    public bool IsHolding { get; private set; }

    public LobbyBriefingLockAction Update(bool briefing)
    {
        if (briefing == IsHolding)
            return LobbyBriefingLockAction.None;

        IsHolding = briefing;
        return briefing ? LobbyBriefingLockAction.Begin : LobbyBriefingLockAction.Release;
    }

    /// <summary>강제 종료(디스폰 등). 잡고 있었으면 true — 호출 쪽이 장부의 잠금을 푼다.</summary>
    public bool End()
    {
        if (!IsHolding)
            return false;

        IsHolding = false;
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
