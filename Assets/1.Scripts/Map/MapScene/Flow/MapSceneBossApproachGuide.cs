using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 존4 진행 + 보스 진입 안내(S10, §3.7 Z4-0~Z4-6 · §3.9 B-1·B-2) — <b>서버 전용</b>. 규칙은 순수 <see cref="MapSceneBossApproachModel"/>,
/// 이 컴포넌트는 디렉터·다리 개통·보스 타이머·패드 신호를 모델에 넣고 결과(타이머 HUD·G8·기상·단계·자막)를 실행한다.
/// 표시(보스방 목표·미니맵 강조·도착 정리)는 모든 피어의 <see cref="MapSceneBossApproachView"/> 가 복제 상태로 그린다. 상세: Docs/tech/tutorial-zone4.md.
/// 할 일 107: 존4 <b>교육</b> 부분(장치 설명·첫 패널·패널 n/4·중간보스 간파 리마인드)은 Legacy(<c>TutorialZone4EducationFlow</c>)로 뺐다.
///
/// 붙는 곳: 서버에서 디렉터가 튜토리얼을 켠 순간 디렉터 GameObject 에 런타임으로 붙는다(씬 무수정).
///
/// 흐름:
///  · G7 열림 ∧ 존4 파티 첫 진입(순서 무관) → 단계 Zone4Lore · 타이머 HUD 표시 · 자막 TUT_Z4_LORE → TUT_Z4_TIMER_HINT(전원) · 존4 휴면 유지.
///  · 설정 자막 길이(<see cref="MapSceneBossApproachModel.DefaultLoreSeconds"/>) 뒤 = G8(<see cref="MapSceneDirector.StartBossTimerServer"/>) →
///    단계 Zone4Combat · 3초 뒤 존4 일반 몬스터 기상(중간보스 제외).
///  · 다리 개통 완료(<see cref="ZoneBridgeGateManager.ServerBridgeOpened"/>) → TUT_Z4_BRIDGE · 중간보스 기상.
///  · 존4 클리어(장부 — 중간보스 처치, G9 는 장부가 연다) → TUT_Z4_CLEAR → TUT_Z4_GO_BOSS. 타이머 만료 → TUT_TIMER_EXPIRED 1번.
///  · 패드 카운트다운 시작 → TUT_B_PAD_COUNT · 보스방 도착(<see cref="MapSceneDirector.BossRoomArrived"/>) 1초 뒤 → TUT_B_BOSS_FRONT_TIP(D19).
/// </summary>
[DisallowMultipleComponent]
public sealed class MapSceneBossApproachGuide : MonoBehaviour
{
    [Header("시간 (서버 실제 시간 초)")]
    [Tooltip("설정 자막(Z4-0) → G8 까지. TUT_Z4_LORE + TUT_Z4_TIMER_HINT 문구 표시 시간 합(5+5) — 문구 시간을 바꾸면 같이 맞출 것.")]
    [SerializeField, Min(0f)] private float loreSeconds = MapSceneBossApproachModel.DefaultLoreSeconds;
    [Tooltip("G8 → 존4 일반 몬스터 기상(Z4-2).")]
    [SerializeField, Min(0f)] private float wakeDelaySeconds = MapSceneBossApproachModel.DefaultWakeDelaySeconds;
    [Tooltip("패드 카운트다운 자막(TUT_B_PAD_COUNT) 최소 간격 — 발판을 들락거릴 때 도배 방지.")]
    [SerializeField, Min(0f)] private float padCountCooldownSeconds = MapSceneBossApproachModel.DefaultPadCountCooldownSeconds;
    [Tooltip("보스방 도착 → 23호 정면 팁. 각 클라가 Finished 복제를 받고 안내를 정리한 뒤에 오게 둔 여유.")]
    [SerializeField, Min(0f)] private float bossTipDelaySeconds = MapSceneBossApproachModel.DefaultBossTipDelaySeconds;

    private MapSceneDirector _director;
    private MapSceneBossApproachModel _model;
    private bool _subscribed;

    private bool _introMode;                 // 4구역 소개 연출 켜짐 — 설정 대기·기상 지연 0, 설정 자막은 복귀 무전 뒤
    private float _loreSubtitleDelay;        // 복귀 무전 길이(데이터) — 설정 자막을 그 뒤로 미룬다
    private double _loreSubtitlesAt = -1d;

    private static double Now => Time.realtimeSinceStartupAsDouble;

    // ─── 부착(서버) ──────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InstallBootstrap()
    {
        MapSceneDirector.StateChanged -= EnsureOnServer;
        MapSceneDirector.StateChanged += EnsureOnServer;
    }

    private static void EnsureOnServer()
    {
        MapSceneDirector d = MapSceneDirector.Instance;
        if (d == null || !d.IsSpawned || !d.IsServer || !d.IsTutorialActive) return;
        if (d.GetComponent<MapSceneBossApproachGuide>() != null) return;
        d.gameObject.AddComponent<MapSceneBossApproachGuide>();
    }

    // ─── 생명주기 ────────────────────────────────────────────────────────

    private void OnEnable()
    {
        _director = GetComponent<MapSceneDirector>();
        NetworkManager nm = NetworkManager.Singleton;
        if (_director == null || nm == null || !nm.IsServer)
        {
            enabled = false;
            return;
        }

        // 4구역 소개 연출이 켜진 판(PLAN-zone4-intro-cinematic, 경석 10-26): G7 = 연출 복귀가 끝난 순간 → 그 즉시 타이머(G8)·일반 몬스터 기상.
        // 설정 자막(LORE·TIMER_HINT)은 타이머를 기다리게 하지 않고 복귀 무전("어서 움직여.") 뒤에 띄운다(_loreSubtitlesAt).
        MapSceneZone4IntroData intro = MapSceneZone4IntroData.Load();
        _introMode = intro.IntroEnabled;
        _loreSubtitleDelay = intro.ReturnLineDelaySeconds + intro.ReturnLineChunkSeconds + intro.ReturnLineGapSeconds;
        _model = _introMode
            ? new MapSceneBossApproachModel(0f, 0f, padCountCooldownSeconds, bossTipDelaySeconds, startOnGateOpen: true)
            : new MapSceneBossApproachModel(loreSeconds, wakeDelaySeconds, padCountCooldownSeconds, bossTipDelaySeconds);
        Subscribe();

        // 붙기 전에 이미 지나간 신호(정상 흐름에서는 없음 — 활성 순간 붙는다)를 따라잡는다.
        if (_director.IsGateOpen(MapSceneGate.G8_TimerStart))
        {
            if (_model.NotifyTimerStarted(Now, out bool skipped)) StartCombat(skipped);
        }
        else
        {
            bool start = false;
            if (_director.IsGateOpen(MapSceneGate.G7_Zone4Entry)) start |= _model.NotifyEntryGateOpen(Now);
            if (_director.HasPartyEnteredServer(MapSceneArea.Zone4)) start |= _model.NotifyPartyEntered(Now);
            if (start) StartLore();
        }
    }

    private void OnDisable() => Unsubscribe();

    private void Subscribe()
    {
        if (_subscribed) return;
        MapSceneDirector.AreaFirstEntered += HandleAreaFirstEntered;
        MapSceneDirector.GateOpened += HandleGateOpened;
        MapSceneDirector.ZoneCleared += HandleZoneCleared;
        MapSceneDirector.BossRoomArrived += HandleBossRoomArrived;
        ZoneBridgeGateManager.ServerBridgeOpened += HandleBridgeOpened;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        MapSceneDirector.AreaFirstEntered -= HandleAreaFirstEntered;
        MapSceneDirector.GateOpened -= HandleGateOpened;
        MapSceneDirector.ZoneCleared -= HandleZoneCleared;
        MapSceneDirector.BossRoomArrived -= HandleBossRoomArrived;
        ZoneBridgeGateManager.ServerBridgeOpened -= HandleBridgeOpened;
        _subscribed = false;
    }

    // ─── 시간·폴링 ───────────────────────────────────────────────────────
    // 타이머 만료·패드 카운트다운은 경석 코드에 이벤트가 없어 복제 상태(State·IsCountdownActive)를 서버에서 읽는다.

    private void Update()
    {
        if (_model == null || _director == null || !_director.IsTutorialActive) return;
        double now = Now;

        // 보스방 도착(Finished) 뒤엔 23호 팁만 남긴다 — 설정 자막→G8·기상 시계·만료/패드 자막은 돌리지 않는다(S12 정리, 휴면 몬스터는 휴면 그대로).
        if (_director.IsFinished)
        {
            // 보스 등장 시네마틱(웰즈 독백)과 겹치지 않게 — 연출이 끝난 뒤에 낸다(팁 예약은 그때까지 남아 있다).
            BossEncounterDirector encounter = BossEncounterDirector.Instance;
            if (encounter != null && encounter.IsSpawned && encounter.IsIntroCinematicActive) return;
            if (_model.ShouldShowBossTip(now))
                _director.ShowSubtitleServer(TutorialGuideIds.BBossFrontTip);
            return;
        }

        // 전멸 재도전 되감기로 Finished 를 벗어났다 — 도착 래치를 풀어 패드 자막·재도착 처리를 다시 돌린다.
        if (_model.NotifyBossRetry())
            Edit.Log("[Tutorial] 존4: 보스 재도전 — 도착 래치 해제.", this);

        if (_loreSubtitlesAt >= 0d && now >= _loreSubtitlesAt)
        {
            _loreSubtitlesAt = -1d;
            _director.ShowSubtitleServer(TutorialGuideIds.Z4Lore);
            _director.ShowSubtitleServer(TutorialGuideIds.Z4TimerHint);
        }

        if (_model.ShouldStartTimer(now))
        {
            // G8 열림 → HandleGateOpened → StartCombat. 거부(G7 닫힘 — 정상 흐름에서는 없음)면 로그만 남긴다.
            if (!_director.StartBossTimerServer())
                Edit.LogWarning("[Tutorial] 존4: 설정 자막이 끝났는데 G8(타이머 시작)이 열리지 않았다 — 게이트 순서 확인.", this);
        }

        if (_model.ShouldWakeNormals(now))
            _director.WakeZoneServer(MapSceneArea.Zone4, MapSceneMonsterFilter.NormalOnly);

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

    // ─── 신호 ────────────────────────────────────────────────────────────

    private void HandleAreaFirstEntered(MapSceneArea area, ulong clientId)
    {
        if (area == MapSceneArea.Zone4 && _model.NotifyPartyEntered(Now)) StartLore();
    }

    private void HandleGateOpened(MapSceneGate gate, MapSceneGateOpenReason reason)
    {
        if (gate == MapSceneGate.G7_Zone4Entry)
        {
            if (_model.NotifyEntryGateOpen(Now)) StartLore();
        }
        else if (gate == MapSceneGate.G8_TimerStart)
        {
            // 설정 자막 끝이든 디버그든 G8 이 열리는 모든 경로가 여기로 온다(타이머 시작은 디렉터가 G8 에서 이미 했다).
            if (_model.NotifyTimerStarted(Now, out bool skippedLore)) StartCombat(skippedLore);
        }
    }

    private void HandleBridgeOpened(int slotId)
    {
        if (!_model.NotifyBridgeOpened()) return;
        Edit.Log($"[Tutorial] 존4 다리 개통(Slot {slotId}) — 중간보스 기상.", this);
        _director.ShowSubtitleServer(TutorialGuideIds.Z4Bridge);
        _director.WakeZoneServer(MapSceneArea.Zone4, MapSceneMonsterFilter.KeyOnly);
    }

    private void HandleZoneCleared(MapSceneArea zone)
    {
        if (zone != MapSceneArea.Zone4 || !_model.NotifyCleared()) return;
        // G9 는 디렉터(존 장부 → RequestGateServer)가 연다 — 여기서는 안내만.
        _director.ShowSubtitleServer(TutorialGuideIds.Z4Clear);
        _director.ShowSubtitleServer(TutorialGuideIds.Z4GoBoss);
    }

    private void HandleBossRoomArrived(IReadOnlyList<ulong> arrived)
    {
        // 각 클라 안내 정리는 MapSceneBossApproachView 가 복제 단계(Finished)로 한다 — 팁은 그 뒤에 오게 지연.
        _model.NotifyBossArrived(Now);
    }

    // ─── 실행 ────────────────────────────────────────────────────────────

    private void StartLore()
    {
        Edit.Log($"[Tutorial] 존4 설정 자막 시작 — {_model.LoreSeconds:0.#}초 뒤 G8(타이머 시작).", this);
        _director.SetStepServer(MapSceneStep.Zone4Lore);
        _director.ShowTimerHudServer();
        if (_introMode)
        {
            _loreSubtitlesAt = Now + Mathf.Max(0f, _loreSubtitleDelay);   // 복귀 무전("어서 움직여.")과 겹치지 않게
            return;
        }
        _director.ShowSubtitleServer(TutorialGuideIds.Z4Lore);
        _director.ShowSubtitleServer(TutorialGuideIds.Z4TimerHint);
    }

    private void StartCombat(bool skippedLore)
    {
        Edit.Log($"[Tutorial] 존4 G8 → 전투{(skippedLore ? " (설정 자막 없이 — 디버그 개방)" : "")} · " +
                 $"{_model.WakeDelaySeconds:0.#}초 뒤 일반 몬스터 기상.", this);
        _director.SetStepServer(MapSceneStep.Zone4Combat);
        // TUT_Z4_START·TUT_Z4_PANEL_INTRO(장치 설명)는 교육 부분이라 Legacy(TutorialZone4EducationFlow)로 뺐다(할 일 107).
    }
}
