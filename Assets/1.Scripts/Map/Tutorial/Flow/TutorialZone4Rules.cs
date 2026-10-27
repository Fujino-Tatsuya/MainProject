using System;

// 튜토리얼 존4(설정 → 타이머 → 패널·다리·중간보스) + 보스 진입 안내 흐름의 순수 규칙(Unity·NGO 비의존 — EditMode 테스트 대상).
// 원본: Docs/design/tutorial-scenario.md §3.7(Z4-0~Z4-6)·§3.9(B-1·B-2)·§4 G7~G9·D4·D6·D19 · PLAN-tutorial.md S10 · Docs/tech/tutorial-zone4.md.
// 배선(서버)은 TutorialZone4Flow, 표시(모든 피어)는 TutorialZone4Guide.

/// <summary>존4 진행 단계(서버 흐름 기준).</summary>
public enum TutorialZone4Phase : byte
{
    /// <summary>G7 열림 + 파티 첫 진입 대기 — 존4 휴면, 타이머 Idle.</summary>
    Waiting,
    /// <summary>설정 자막(Z4-0) — 타이머 HUD 표시, 타이머 정지, 존4 휴면 유지.</summary>
    Lore,
    /// <summary>G8 뒤 — 타이머 진행, 패널·다리·중간보스.</summary>
    Combat,
    /// <summary>중간보스 처치(존4 클리어 — G9 는 장부가 연다).</summary>
    Cleared,
}

/// <summary>
/// 존4 + 보스 진입 안내 상태 기계. 시각은 호출자가 넘기는 서버 실제 시간(초)이다(슬로우모션 무관 — §2.1).
/// 같은 신호 반복은 무시한다(전부 1회성, 패드 카운트다운 자막만 재시작마다 — 간격 제한).
/// </summary>
public sealed class TutorialZone4Model
{
    /// <summary>
    /// 설정 자막(Z4-0) 길이 = G8 까지 기다릴 시간. <c>TUT_Z4_LORE</c>(5초) + <c>TUT_Z4_TIMER_HINT</c>(5초) 문구 표시 시간 합 —
    /// 시나리오 §3.7 "설정 자막이 끝나면(약 8~10초)". 문구 행 표시 시간을 바꾸면 이 값(인스펙터)도 맞출 것.
    /// </summary>
    public const float DefaultLoreSeconds = 10f;
    /// <summary>G8 → 존4 일반 몬스터 기상(Z4-2 "3초 뒤").</summary>
    public const float DefaultWakeDelaySeconds = 3f;
    /// <summary>패드 카운트다운 자막을 다시 띄우기까지 최소 간격 — 발판을 들락거릴 때 도배 방지.</summary>
    public const float DefaultPadCountCooldownSeconds = 8f;
    /// <summary>보스방 도착 → 23호 정면 팁(D19)까지 — 각 클라 안내 정리(Finished 복제)가 먼저 끝나게.</summary>
    public const float DefaultBossTipDelaySeconds = 1f;

    private bool _entryGateOpen;
    private bool _partyEntered;
    private double _timerAt = -1d;
    private double _wakeAt = -1d;
    private double _bossTipAt = -1d;
    private bool _padActive;
    private double _padShownAt = double.NegativeInfinity;

    /// <param name="startOnGateOpen">
    /// true = 설정 자막을 G7 열림 하나로 시작한다(파티 존4 진입을 기다리지 않는다). 4구역 소개 연출(PLAN-zone4-intro-cinematic)이 켜진 판 —
    /// 연출이 끝나 G7 이 열리는 순간이 곧 타이머 시작이다(경석 10-26: 소개가 끝나고 타이머).
    /// </param>
    public TutorialZone4Model(float loreSeconds = DefaultLoreSeconds, float wakeDelaySeconds = DefaultWakeDelaySeconds,
                              float padCountCooldownSeconds = DefaultPadCountCooldownSeconds,
                              float bossTipDelaySeconds = DefaultBossTipDelaySeconds,
                              bool startOnGateOpen = false)
    {
        StartOnGateOpen = startOnGateOpen;
        LoreSeconds = Math.Max(0f, loreSeconds);
        WakeDelaySeconds = Math.Max(0f, wakeDelaySeconds);
        PadCountCooldownSeconds = Math.Max(0f, padCountCooldownSeconds);
        BossTipDelaySeconds = Math.Max(0f, bossTipDelaySeconds);
    }

    public bool StartOnGateOpen { get; }
    public float LoreSeconds { get; }
    public float WakeDelaySeconds { get; }
    public float PadCountCooldownSeconds { get; }
    public float BossTipDelaySeconds { get; }

    public TutorialZone4Phase Phase { get; private set; } = TutorialZone4Phase.Waiting;
    public bool PanelFirstShown { get; private set; }
    public bool BridgeOpened { get; private set; }
    public bool CounterRemindShown { get; private set; }
    public bool TimerExpiredShown { get; private set; }
    public bool BossArrived { get; private set; }

    // ─── Z4-0 설정 자막 → Z4-1 G8 ───

    /// <summary>G7(존4 입구) 열림. 설정 자막을 지금 시작해야 하면 true.</summary>
    public bool NotifyEntryGateOpen(double now)
    {
        _entryGateOpen = true;
        return TryStartLore(now);
    }

    /// <summary>파티 첫 존4 진입. 설정 자막을 지금 시작해야 하면 true.</summary>
    public bool NotifyPartyEntered(double now)
    {
        _partyEntered = true;
        return TryStartLore(now);
    }

    private bool TryStartLore(double now)
    {
        if (Phase != TutorialZone4Phase.Waiting || !_entryGateOpen || (!_partyEntered && !StartOnGateOpen)) return false;
        Phase = TutorialZone4Phase.Lore;
        _timerAt = now + LoreSeconds;
        return true;
    }

    /// <summary>설정 자막이 끝났다 → 타이머 시작(G8)을 요청할 때면 true(한 번).</summary>
    public bool ShouldStartTimer(double now)
    {
        if (Phase != TutorialZone4Phase.Lore || _timerAt < 0d || now < _timerAt) return false;
        _timerAt = -1d;
        return true;
    }

    /// <summary>
    /// G8 열림(어느 경로든 — 설정 자막 끝·디버그). 전투 단계로 넘어가면 true.
    /// <paramref name="skippedLore"/> = 설정 자막 없이 열렸다(디버그) — 호출자가 HUD·자막을 생략할지 정한다.
    /// </summary>
    public bool NotifyTimerStarted(double now, out bool skippedLore)
    {
        skippedLore = Phase == TutorialZone4Phase.Waiting;
        if (Phase != TutorialZone4Phase.Waiting && Phase != TutorialZone4Phase.Lore) return false;
        Phase = TutorialZone4Phase.Combat;
        _timerAt = -1d;
        _wakeAt = now + WakeDelaySeconds;
        return true;
    }

    /// <summary>G8 + 기상 지연이 지났다 → 존4 일반 몬스터 기상(중간보스 제외) 때면 true(한 번).</summary>
    public bool ShouldWakeNormals(double now)
    {
        if (_wakeAt < 0d || now < _wakeAt) return false;
        _wakeAt = -1d;
        return true;
    }

    // ─── Z4-3 패널 · Z4-4 다리 · Z4-5 중간보스 ───

    /// <summary>패널 하나가 새로 켜졌다(누가 켜도 파티 공유). <paramref name="showFirst"/> = 판 전체 첫 패널(<c>TUT_Z4_PANEL_FIRST</c>).</summary>
    public void NotifyPanelActivated(out bool showFirst)
    {
        showFirst = !PanelFirstShown;
        PanelFirstShown = true;
    }

    /// <summary>다리 개통 완료. 처음이면 true(→ <c>TUT_Z4_BRIDGE</c>·중간보스 기상).</summary>
    public bool NotifyBridgeOpened()
    {
        if (BridgeOpened) return false;
        BridgeOpened = true;
        return true;
    }

    /// <summary>중간보스 간파 창이 열렸다. 다리 개통 뒤·처치 전 첫 1번만 <c>TUT_Z4_COUNTER_REMIND</c>.</summary>
    public bool NotifyMidbossWindowOpened()
    {
        if (!BridgeOpened || CounterRemindShown || Phase == TutorialZone4Phase.Cleared || BossArrived) return false;
        CounterRemindShown = true;
        return true;
    }

    /// <summary>존4 클리어(중간보스 처치 — D4). 처음이면 true.</summary>
    public bool NotifyCleared()
    {
        if (Phase == TutorialZone4Phase.Cleared) return false;
        Phase = TutorialZone4Phase.Cleared;
        _timerAt = -1d;
        return true;
    }

    // ─── 타이머 만료 · 보스 진입 ───

    /// <summary>보스 타이머가 만료 상태다. 도착 전 첫 1번만 <c>TUT_TIMER_EXPIRED</c>.</summary>
    public bool NotifyTimerExpired()
    {
        if (TimerExpiredShown || BossArrived) return false;
        TimerExpiredShown = true;
        return true;
    }

    /// <summary>
    /// 패드 카운트다운 진행 여부(매 폴링). 새로 시작된 패드 카운트다운이면 true(<c>TUT_B_PAD_COUNT</c>) —
    /// 만료 강제 이동(<paramref name="timerExpired"/>)은 같은 카운트다운 표시를 쓰지만 발판과 무관해 제외, 간격 제한 안에 다시 시작해도 제외.
    /// </summary>
    public bool NotifyPadCountdown(bool active, bool timerExpired, double now)
    {
        bool started = active && !_padActive;
        _padActive = active;
        if (!started || timerExpired || BossArrived) return false;
        if (now - _padShownAt < PadCountCooldownSeconds) return false;
        _padShownAt = now;
        return true;
    }

    /// <summary>보스방 도착(판 전체 1번). 처음이면 true — 팁은 <see cref="ShouldShowBossTip"/> 가 지연 뒤 낸다.</summary>
    public bool NotifyBossArrived(double now)
    {
        if (BossArrived) return false;
        BossArrived = true;
        _bossTipAt = now + BossTipDelaySeconds;
        _timerAt = -1d;
        _wakeAt = -1d;
        return true;
    }

    /// <summary>
    /// 전멸 재도전 되감기(단계 Finished → BossEnter). 도착 래치를 풀어 패드 카운트다운 자막·도착 처리·23호 팁이 다시 돈다.
    /// 도착 전이면 false(할 일 없음). 설정 자막·타이머·기상 시계는 되살리지 않는다.
    /// </summary>
    public bool NotifyBossRetry()
    {
        if (!BossArrived) return false;
        BossArrived = false;
        _bossTipAt = -1d;
        return true;
    }

    /// <summary>도착 + 지연이 지났다 → <c>TUT_B_BOSS_FRONT_TIP</c>(D19) 때면 true(한 번).</summary>
    public bool ShouldShowBossTip(double now)
    {
        if (_bossTipAt < 0d || now < _bossTipAt) return false;
        _bossTipAt = -1d;
        return true;
    }
}

/// <summary>존4 강조 대상(동시에 여러 개).</summary>
[Flags]
public enum TutorialZone4Highlight : byte
{
    None = 0,
    /// <summary>타이머 게이지 펄스(Z4-0 — 설정 자막 동안).</summary>
    BossTimer = 1 << 0,
    /// <summary>가장 가까운 계단 + 중앙 섬(중간보스) — 다리 개통 뒤 처치 전(Z4-4).</summary>
    MidbossRoute = 1 << 1,
    /// <summary>미니맵 보스 아이콘 펄스 — G9 뒤 도착 전(Z4-6).</summary>
    MinimapBossIcon = 1 << 2,
}

/// <summary>
/// 존4·보스 진입 목표 패널·강조 — 복제 상태(단계·패널 카운터·G9)만으로 정한다. 늦게 붙은 클라도 <c>StateChanged</c> 한 번으로 같은 화면이 된다.
/// 패널 다 켬 = 다리 개통 시작(기존 ZoneBridgeGateManager 규칙) → 목표는 그때 중간보스로 바뀐다(개통 1.5초는 무시).
/// </summary>
public static class TutorialZone4Display
{
    /// <summary>지금 띄울 존4 목표 id. 존4 전투 전·보스방 도착 뒤면 null.</summary>
    public static string ObjectiveFor(TutorialStep step, int panels, int panelTarget, bool corridor4EntryOpen)
    {
        if (step == TutorialStep.Finished) return null;
        if (corridor4EntryOpen) return TutorialGuideIds.Z4GoBoss;
        if (step < TutorialStep.Zone4Combat) return null;
        return panels < Math.Max(1, panelTarget) ? TutorialGuideIds.Z4PanelGoal : TutorialGuideIds.Z4Midboss;
    }

    public static TutorialZone4Highlight HighlightsFor(TutorialStep step, int panels, int panelTarget, bool corridor4EntryOpen)
    {
        if (step == TutorialStep.Finished) return TutorialZone4Highlight.None;
        if (corridor4EntryOpen) return TutorialZone4Highlight.MinimapBossIcon;
        if (step == TutorialStep.Zone4Lore) return TutorialZone4Highlight.BossTimer;
        if (step >= TutorialStep.Zone4Combat && panels >= Math.Max(1, panelTarget)) return TutorialZone4Highlight.MidbossRoute;
        return TutorialZone4Highlight.None;
    }

    /// <summary>
    /// 존4 목표가 "보스방으로"(G9 뒤 — 플레이어는 복도4)인데 복도 흐름이 고르기·대기·발판 목표를 띄웠으면 존4 가 양보한다(S12).
    /// "다음 구역으로"(낡은 복도3 목표)에는 양보하지 않는다.
    /// </summary>
    public static bool YieldsToCorridor(string zone4ObjectiveId, string currentObjectiveId) =>
        zone4ObjectiveId == TutorialGuideIds.Z4GoBoss
        && currentObjectiveId != TutorialCorridorFlow.NextId
        && TutorialCorridorRules.IsCorridorObjective(currentObjectiveId);

    /// <summary>존4 설정 자막 단계(목표 줄 없음)에 복도3 출구 목표 "다음 구역으로"가 남아 있으면 지운다(S12 — 존 첫 진입 정리).</summary>
    public static bool ShouldClearStaleCorridorObjective(TutorialStep step, string currentObjectiveId) =>
        step == TutorialStep.Zone4Lore && currentObjectiveId == TutorialCorridorFlow.NextId;

    /// <summary>이 목표 id 가 존4 흐름이 띄운 것인가 — 존4 를 벗어날 때 남의 목표는 지우지 않는다.</summary>
    public static bool IsZone4Objective(string id) =>
        id == TutorialGuideIds.Z4PanelGoal || id == TutorialGuideIds.Z4Midboss || id == TutorialGuideIds.Z4GoBoss;
}
