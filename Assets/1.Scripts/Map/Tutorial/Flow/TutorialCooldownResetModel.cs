using System.Collections.Generic;

// 복도1 첫 진입 쿨타임 초기화(v8 C1-0)와 존3 간파 성공 우클릭 쿨 초기화(v8 Z3-4)의 순수 규칙 — Unity·NGO 비의존(EditMode 테스트 대상).
// 원본: Docs/design/tutorial-scenario.md §3.2(C1-0·쿨타임 초기화 범위·추가 구현 검토)·§3.5 · PLAN-tutorial.md V2·V4.
// 배선(서버 — 쿨 장부 공개 API 호출)은 TutorialCorridor1CooldownFlow · TutorialZone3Flow, 공용 실행은 TutorialCooldownReset.

/// <summary>
/// 복도1 첫 진입 쿨타임 초기화 — 플레이어별 1회 + 진행 중 행동 종료까지 <b>초기화 예약</b>.
/// <list type="bullet">
/// <item>조건: G1(존1 클리어) 열림 ∧ 그 플레이어가 복도1 구간에 있음(서버 구간 판정 — 살아 있는 사람만) ∧ 아직 안 받음.
///   첫 진입한 1명 때문에 동료를 미리 초기화하지 않는다. 왕복·부활 재진입은 반복하지 않는다.</item>
/// <item>예약: 초기화 순간 그 사람이 '끝날 때 쿨이 걸리는 행동'(실행 중 스킬 · 어쌔신 변신 · 어쌔신 E 강화 대기 · 거너 Q 집중) 중이면
///   그 행동이 모두 끝나는 순간 한 번 더 초기화한다. 자원을 채우거나 변신을 강제로 끝내지 않는다.</item>
/// </list>
/// </summary>
public sealed class TutorialCooldownResetModel
{
    private readonly HashSet<ulong> _done = new HashSet<ulong>();
    private readonly HashSet<ulong> _reserved = new HashSet<ulong>();

    /// <summary>이 플레이어가 이미 받았는가(첫 초기화 기준).</summary>
    public bool HasReset(ulong clientId) => _done.Contains(clientId);

    /// <summary>예약(진행 중 행동 종료 대기)이 걸려 있는가.</summary>
    public bool IsReserved(ulong clientId) => _reserved.Contains(clientId);

    /// <summary>
    /// 지금 첫 초기화를 할 차례인가. true 면 호출자가 즉시 초기화하고, <paramref name="busy"/>(진행 중 행동)였다면 예약도 건다.
    /// <paramref name="alreadyRecorded"/> = 복제 기록(디렉터 체크)으로 이미 받은 사람 — 흐름 컴포넌트가 다시 붙어도 반복하지 않는다.
    /// </summary>
    public bool TryBegin(ulong clientId, bool g1Open, bool inCorridor1, bool busy, bool alreadyRecorded = false)
    {
        if (alreadyRecorded) _done.Add(clientId);
        if (!g1Open || !inCorridor1 || _done.Contains(clientId)) return false;

        _done.Add(clientId);
        if (busy) _reserved.Add(clientId);
        return true;
    }

    /// <summary>
    /// 예약 진행 — 예약이 있고 진행 중 행동이 모두 끝났으면 true(한 번 더 초기화할 때). 예약은 그때 지운다.
    /// 행동이 아직이면(또는 예약이 없으면) false.
    /// </summary>
    public bool TickReservation(ulong clientId, bool busy)
    {
        if (busy || !_reserved.Contains(clientId)) return false;
        _reserved.Remove(clientId);
        return true;
    }

    /// <summary>이탈 — 예약만 지운다(받은 기록은 남김, 재접속 없음).</summary>
    public void Forget(ulong clientId) => _reserved.Remove(clientId);
}

/// <summary>쿨타임 초기화 대상(v8 §3.2 범위) — 스킬 슬롯·대시. 자원·체력·패시브 주기는 범위 밖.</summary>
public static class TutorialCooldownResetScope
{
    /// <summary>복도1: Q·E·R·우클릭(기본·대체 스킬 모두) + 대시.</summary>
    public static bool Corridor1Includes(PlayerSkillSlot slot) =>
        slot == PlayerSkillSlot.Main || slot == PlayerSkillSlot.Sub
        || slot == PlayerSkillSlot.Ultimate || slot == PlayerSkillSlot.Interrupt;

    /// <summary>존3 간파 성공: 우클릭(기본·대체)만. 대시는 아니다.</summary>
    public static bool Zone3CounterIncludes(PlayerSkillSlot slot) => slot == PlayerSkillSlot.Interrupt;
}
