using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 튜토리얼 존4 흐름 + 보스 진입 안내(S10, §3.7 Z4-0~Z4-6 · §3.9 B-1·B-2) — <b>서버 전용</b>. 규칙은 순수 <see cref="TutorialZone4Model"/>,
/// 이 컴포넌트는 디렉터·다리 패널·보스 타이머·패드 신호를 모델에 넣고 결과(타이머 HUD·G8·기상·카운터·단계·자막)를 실행한다.
/// 표시(목표 패널·강조·도착 정리)는 모든 피어의 <see cref="TutorialZone4Guide"/> 가 복제 상태로 그린다. 상세: Docs/tech/tutorial-zone4.md.
///
/// 붙는 곳: 서버에서 디렉터가 튜토리얼을 켠 순간 디렉터 GameObject 에 런타임으로 붙는다(<see cref="TutorialZone3Flow"/> 와 같은 방식 — 씬 무수정).
///
/// 흐름:
///  · G7 열림 ∧ 존4 파티 첫 진입(순서 무관) → 단계 Zone4Lore · 타이머 HUD 표시 · 자막 TUT_Z4_LORE → TUT_Z4_TIMER_HINT(전원) · 존4 휴면 유지.
///  · 설정 자막 길이(<see cref="TutorialZone4Model.DefaultLoreSeconds"/>) 뒤 = G8(<see cref="TutorialDirector.StartBossTimerServer"/>) →
///    단계 Zone4Combat · TUT_Z4_START → TUT_Z4_PANEL_INTRO · 3초 뒤 존4 일반 몬스터 기상(중간보스 제외).
///  · 패널(<see cref="ZoneBridgeGateManager.ServerPanelActivated"/>)마다 Zone4Panels +1(첫 1번 TUT_Z4_PANEL_FIRST) ·
///    다리 개통 완료(<see cref="ZoneBridgeGateManager.ServerBridgeOpened"/>) → TUT_Z4_BRIDGE · 중간보스 기상 · 첫 간파 창 → TUT_Z4_COUNTER_REMIND.
///  · 존4 클리어(장부 — 중간보스 처치, G9 는 장부가 연다) → TUT_Z4_CLEAR → TUT_Z4_GO_BOSS. 타이머 만료 → TUT_TIMER_EXPIRED 1번.
///  · 패드 카운트다운 시작 → TUT_B_PAD_COUNT · 보스방 도착(<see cref="TutorialDirector.BossRoomArrived"/>) 1초 뒤 → TUT_B_BOSS_FRONT_TIP(D19).
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialZone4Flow : MonoBehaviour
{
    [Header("시간 (서버 실제 시간 초)")]
    [Tooltip("설정 자막(Z4-0) → G8 까지. TUT_Z4_LORE + TUT_Z4_TIMER_HINT 문구 표시 시간 합(5+5) — 문구 시간을 바꾸면 같이 맞출 것.")]
    [SerializeField, Min(0f)] private float loreSeconds = TutorialZone4Model.DefaultLoreSeconds;
    [Tooltip("G8 → 존4 일반 몬스터 기상(Z4-2).")]
    [SerializeField, Min(0f)] private float wakeDelaySeconds = TutorialZone4Model.DefaultWakeDelaySeconds;
    [Tooltip("패드 카운트다운 자막(TUT_B_PAD_COUNT) 최소 간격 — 발판을 들락거릴 때 도배 방지.")]
    [SerializeField, Min(0f)] private float padCountCooldownSeconds = TutorialZone4Model.DefaultPadCountCooldownSeconds;
    [Tooltip("보스방 도착 → 23호 정면 팁. 각 클라가 Finished 복제를 받고 안내를 정리한 뒤에 오게 둔 여유.")]
    [SerializeField, Min(0f)] private float bossTipDelaySeconds = TutorialZone4Model.DefaultBossTipDelaySeconds;

    private TutorialDirector _director;
    private TutorialZone4Model _model;
    private MonsterCounterWindow _midbossWindow;
    private bool _midbossWindowWasOpen;
    private bool _subscribed;

    private readonly List<MonsterBase> _buffer = new List<MonsterBase>();

    private static double Now => Time.realtimeSinceStartupAsDouble;

    // ─── 부착(서버) ──────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InstallBootstrap()
    {
        TutorialDirector.StateChanged -= EnsureOnServer;
        TutorialDirector.StateChanged += EnsureOnServer;
    }

    private static void EnsureOnServer()
    {
        TutorialDirector d = TutorialDirector.Instance;
        if (d == null || !d.IsSpawned || !d.IsServer || !d.IsTutorialActive) return;
        if (d.GetComponent<TutorialZone4Flow>() != null) return;
        d.gameObject.AddComponent<TutorialZone4Flow>();
    }

    // ─── 생명주기 ────────────────────────────────────────────────────────

    private void OnEnable()
    {
        _director = GetComponent<TutorialDirector>();
        NetworkManager nm = NetworkManager.Singleton;
        if (_director == null || nm == null || !nm.IsServer)
        {
            enabled = false;
            return;
        }

        _model = new TutorialZone4Model(loreSeconds, wakeDelaySeconds, padCountCooldownSeconds, bossTipDelaySeconds);
        Subscribe();

        // 붙기 전에 이미 지나간 신호(정상 흐름에서는 없음 — 활성 순간 붙는다)를 따라잡는다.
        if (_director.IsGateOpen(TutorialGate.G8_TimerStart))
        {
            if (_model.NotifyTimerStarted(Now, out bool skipped)) StartCombat(skipped);
        }
        else
        {
            bool start = false;
            if (_director.IsGateOpen(TutorialGate.G7_Zone4Entry)) start |= _model.NotifyEntryGateOpen(Now);
            if (_director.HasPartyEnteredServer(TutorialArea.Zone4)) start |= _model.NotifyPartyEntered(Now);
            if (start) StartLore();
        }
    }

    private void OnDisable() => Unsubscribe();

    private void Subscribe()
    {
        if (_subscribed) return;
        TutorialDirector.AreaFirstEntered += HandleAreaFirstEntered;
        TutorialDirector.GateOpened += HandleGateOpened;
        TutorialDirector.ZoneCleared += HandleZoneCleared;
        TutorialDirector.BossRoomArrived += HandleBossRoomArrived;
        ZoneBridgeGateManager.ServerPanelActivated += HandlePanelActivated;
        ZoneBridgeGateManager.ServerBridgeOpened += HandleBridgeOpened;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        TutorialDirector.AreaFirstEntered -= HandleAreaFirstEntered;
        TutorialDirector.GateOpened -= HandleGateOpened;
        TutorialDirector.ZoneCleared -= HandleZoneCleared;
        TutorialDirector.BossRoomArrived -= HandleBossRoomArrived;
        ZoneBridgeGateManager.ServerPanelActivated -= HandlePanelActivated;
        ZoneBridgeGateManager.ServerBridgeOpened -= HandleBridgeOpened;
        _subscribed = false;
    }

    // ─── 시간·폴링 ───────────────────────────────────────────────────────
    // 타이머 만료·패드 카운트다운은 경석 코드에 이벤트가 없어 복제 상태(State·IsCountdownActive)를 서버에서 읽는다.

    private void Update()
    {
        if (_model == null || _director == null || !_director.IsTutorialActive) return;
        double now = Now;

        if (_model.ShouldStartTimer(now))
        {
            // G8 열림 → HandleGateOpened → StartCombat. 거부(G7 닫힘 — 정상 흐름에서는 없음)면 로그만 남긴다.
            if (!_director.StartBossTimerServer())
                Edit.LogWarning("[Tutorial] 존4: 설정 자막이 끝났는데 G8(타이머 시작)이 열리지 않았다 — 게이트 순서 확인.", this);
        }

        if (_model.ShouldWakeNormals(now))
            _director.WakeZoneServer(TutorialArea.Zone4, TutorialMonsterFilter.NormalOnly);

        PollMidbossWindow();

        BossTimerManager timer = BossTimerManager.Instance;
        bool expired = timer != null && timer.State == BossTimerManager.TimerState.Expired;
        if (expired && _model.NotifyTimerExpired())
            _director.ShowSubtitleServer(TutorialGuideIds.TimerExpired);

        BossTeleportManager teleport = BossTeleportManager.Instance;
        if (teleport != null && _model.NotifyPadCountdown(teleport.IsCountdownActive, expired, now))
            _director.ShowSubtitleServer(TutorialGuideIds.BPadCount);

        if (_model.ShouldShowBossTip(now))
            _director.ShowSubtitleServer(TutorialGuideIds.BBossFrontTip);
    }

    private void PollMidbossWindow()
    {
        if (_midbossWindow == null) return;
        bool open = _midbossWindow.IsOpen;
        if (open && !_midbossWindowWasOpen && _model.NotifyMidbossWindowOpened())
            _director.ShowSubtitleServer(TutorialGuideIds.Z4CounterRemind);
        _midbossWindowWasOpen = open;
        if (_model.CounterRemindShown) _midbossWindow = null;   // 1번이면 끝 — 더 볼 필요 없다
    }

    // ─── 신호 ────────────────────────────────────────────────────────────

    private void HandleAreaFirstEntered(TutorialArea area, ulong clientId)
    {
        if (area == TutorialArea.Zone4 && _model.NotifyPartyEntered(Now)) StartLore();
    }

    private void HandleGateOpened(TutorialGate gate, TutorialGateOpenReason reason)
    {
        if (gate == TutorialGate.G7_Zone4Entry)
        {
            if (_model.NotifyEntryGateOpen(Now)) StartLore();
        }
        else if (gate == TutorialGate.G8_TimerStart)
        {
            // 설정 자막 끝이든 디버그든 G8 이 열리는 모든 경로가 여기로 온다(타이머 시작은 디렉터가 G8 에서 이미 했다).
            if (_model.NotifyTimerStarted(Now, out bool skippedLore)) StartCombat(skippedLore);
        }
    }

    private void HandlePanelActivated(int slotId, int panelIndex, int activated, int panelCount)
    {
        _model.NotifyPanelActivated(out bool first);
        if (first) _director.ShowSubtitleServer(TutorialGuideIds.Z4PanelFirst);
        // 목표(4)에서 멈춘다 — 모든 패널을 켜면 ZoneBridgeGateManager 가 다리를 연다.
        _director.AddCounterServer(TutorialCounter.Zone4Panels);
        Edit.Log($"[Tutorial] 존4 패널 {activated}/{panelCount} (Slot {slotId} 패널 {panelIndex}) — 카운터 " +
                 $"{_director.GetCounter(TutorialCounter.Zone4Panels)}/{_director.GetCounterTarget(TutorialCounter.Zone4Panels)}.", this);
    }

    private void HandleBridgeOpened(int slotId)
    {
        if (!_model.NotifyBridgeOpened()) return;
        Edit.Log($"[Tutorial] 존4 다리 개통(Slot {slotId}) — 중간보스 기상.", this);
        _director.ShowSubtitleServer(TutorialGuideIds.Z4Bridge);
        _director.WakeZoneServer(TutorialArea.Zone4, TutorialMonsterFilter.KeyOnly);

        _director.GetZoneMonstersServer(TutorialArea.Zone4, TutorialMonsterFilter.KeyOnly, _buffer);
        _midbossWindow = _buffer.Count > 0 ? _buffer[0].GetComponent<MonsterCounterWindow>() : null;
        _midbossWindowWasOpen = _midbossWindow != null && _midbossWindow.IsOpen;
        _buffer.Clear();
    }

    private void HandleZoneCleared(TutorialArea zone)
    {
        if (zone != TutorialArea.Zone4 || !_model.NotifyCleared()) return;
        // G9 는 디렉터(존 장부 → RequestGateServer)가 연다 — 여기서는 안내만.
        _midbossWindow = null;
        _director.ShowSubtitleServer(TutorialGuideIds.Z4Clear);
        _director.ShowSubtitleServer(TutorialGuideIds.Z4GoBoss);
    }

    private void HandleBossRoomArrived(IReadOnlyList<ulong> arrived)
    {
        // 각 클라 안내 정리는 TutorialZone4Guide 가 복제 단계(Finished)로 한다 — 팁은 그 뒤에 오게 지연.
        _model.NotifyBossArrived(Now);
        _midbossWindow = null;
    }

    // ─── 실행 ────────────────────────────────────────────────────────────

    private void StartLore()
    {
        Edit.Log($"[Tutorial] 존4 설정 자막 시작 — {_model.LoreSeconds:0.#}초 뒤 G8(타이머 시작).", this);
        _director.SetStepServer(TutorialStep.Zone4Lore);
        _director.ShowTimerHudServer();
        _director.ShowSubtitleServer(TutorialGuideIds.Z4Lore);
        _director.ShowSubtitleServer(TutorialGuideIds.Z4TimerHint);
    }

    private void StartCombat(bool skippedLore)
    {
        Edit.Log($"[Tutorial] 존4 G8 → 전투{(skippedLore ? " (설정 자막 없이 — 디버그 개방)" : "")} · " +
                 $"{_model.WakeDelaySeconds:0.#}초 뒤 일반 몬스터 기상.", this);
        _director.SetStepServer(TutorialStep.Zone4Combat);
        _director.ShowSubtitleServer(TutorialGuideIds.Z4Start);
        _director.ShowSubtitleServer(TutorialGuideIds.Z4PanelIntro);
    }
}
