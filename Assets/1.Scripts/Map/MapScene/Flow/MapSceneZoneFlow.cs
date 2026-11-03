using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 맵씬 존1~3 진행(할 일 107) — 존 교육 흐름(존1 체크리스트·존2 안내·존3 스피너 연습)을 Legacy 로 뺀 자리의 <b>최소 진행</b>.
/// 구역은 전투·클리어 조건만 남긴다(할 일 86 PLAN §3.7 · 로비 훈련 명세 §11 — 기본 공격·스킬·간파 횟수 검사 제거, 교육은 로비 훈련이 대신).
/// 게이트 순서·배리어·존 장부·복도 특성 라운드는 그대로다 — 교육 흐름이 열던 게이트(G1·G5)와 존 기상만 여기서 대신한다.
///
/// 붙는 곳: 디렉터가 튜토리얼을 켠 것이 그 피어에서 보이는 순간 디렉터 GameObject 에 런타임으로 붙는다(모든 피어 — 씬·프리팹 저장 없음).
///
/// 서버(디렉터 Commit 안 — 게이트·진입 이벤트 — 에서 다시 Commit 하지 않게 실행은 다음 Update 로 미룬다):
///  · 1구역 도착 무전 <c>TUT_MAIN_Z01_ENTER</c> 은 <b>로딩이 걷히고 포드 착지 연출이 끝난 뒤</b>
///    (조작 복귀 — <see cref="MapPodArrival.IsArrivalDoneLocal"/> ∧ <see cref="GameManager.IsMainGameReady"/>) + 무전 데이터 지연. G1 순간에 틀었더니 착지 연출 중에 끝나 안 보였다(경석 11-02).
///    G1(존1 → 복도1)은 <b>그 무전의 마지막 조각이 끝난 뒤</b> 연다 → 단계 Corridor1(경석 11-02 — 활성 순간 열면 대사를 안 듣고 지나간다).
///  · 존2 파티 첫 진입 → 단계 Zone2. 2구역 소개 연출이 켜진 판이면 기상은 <see cref="MapSceneZone2IntroFlow"/> 몫(G2 가 열리는 순간 = 연출 복귀 끝),
///    끈 판이면 <see cref="Zone2WakeDelaySeconds"/> 뒤 존2 기상(예전 그대로). G3 는 존 장부(전멸)가 연다.
///  · G4 열림 ∧ 존3 파티 첫 진입 → 존3 핵심 몬스터(스피너봇 — 없으면 전부) 기상 · 단계 Zone3Fight · G5 요청. G6 은 존 장부(처치)가 연다.
///  · 무전: 존2·존3 클리어(장부) → <c>TUT_MAIN_Z02_CLEAR</c>·<c>TUT_MAIN_Z03_CLEAR</c>. 존3 핵심 몬스터가 살아 있는 플레이어 누군가와
///    <see cref="MapSceneRadioData.Zone3SightDistance"/> 안이 된 첫 순간 <c>TUT_MAIN_Z03_ENEMY</c>(공격 준비·판정 창과 무관 — 명세 §4.4). 같은 무전은 판마다 한 번(디렉터).
/// 모든 피어(로컬):
///  · 단계가 존2/존3 으로 바뀌면 복도 출구 목표 "다음 구역으로"(<see cref="MapSceneTraitCorridorFlow.NextId"/>)가 남아 있을 때 지운다(존 목표 줄은 없다).
/// </summary>
[DisallowMultipleComponent]
public sealed class MapSceneZoneFlow : MonoBehaviour
{
    /// <summary>존2 첫 진입 → 기상(서버 실제 시간 초). 이전 존2 흐름(TutorialZone2Rules.WakeDelaySeconds)과 같다.</summary>
    public const double Zone2WakeDelaySeconds = 4d;

    private MapSceneDirector _director;
    private NetworkManager _nm;
    private bool _isServer;

    // 서버
    private bool _pendingG1 = true;
    private bool _pendingCorridor1Step;
    private double _zone2EnteredAt = -1d;
    private bool _zone2StepDone;
    private bool _zone2Woken;
    private bool _zone3Started;
    private bool _pendingZone3;
    private bool _zone3EnemySeen;
    private double _zone1RadioPendingSince = -1d;   // G1 을 연 시각 — 착지 연출이 끝나길 기다리는 중
    /// <summary>착지 신호가 끝내 안 오면(연출 고장) 이만큼 뒤엔 그냥 낸다(초).</summary>
    private const double Zone1RadioGiveUpSeconds = 30d;
    private bool _g1WaitingRadio;   // 도착 무전을 틀었고 그 끝을 기다려 G1 을 연다
    /// <summary>무전 길이를 못 재면(문구·글꼴 없음) 무전 시작부터 이만큼 뒤엔 그냥 G1 을 연다(초).</summary>
    private const double G1RadioGiveUpSeconds = 15d;
    private float _nextSightCheckAt;
    private readonly List<MonsterBase> _sightBuffer = new List<MonsterBase>();
    private const float SightCheckInterval = 0.2f;

    // ─── 부착(모든 피어) ───────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InstallBootstrap()
    {
        MapSceneDirector.StateChanged -= Ensure;
        MapSceneDirector.StateChanged += Ensure;
    }

    private static void Ensure()
    {
        MapSceneDirector d = MapSceneDirector.Instance;
        if (d == null || !d.IsSpawned || !d.IsTutorialActive) return;
        if (d.GetComponent<MapSceneZoneFlow>() != null) return;
        d.gameObject.AddComponent<MapSceneZoneFlow>();
    }

    // ─── 생명주기 ────────────────────────────────────────────────────────

    private void OnEnable()
    {
        _director = GetComponent<MapSceneDirector>();
        _nm = NetworkManager.Singleton;
        if (_director == null || _nm == null || !_nm.IsListening)
        {
            enabled = false;
            return;
        }

        _isServer = _nm.IsServer;
        if (_isServer)
        {
            MapSceneDirector.AreaFirstEntered += HandleAreaFirstEnteredServer;
            MapSceneDirector.GateOpened += HandleGateOpenedServer;
            MapSceneDirector.ZoneCleared += HandleZoneClearedServer;

            // 붙기 전에 이미 지나간 신호(정상 흐름에서는 없음 — 활성 순간 붙는다)를 따라잡는다.
            if (_director.HasPartyEnteredServer(MapSceneArea.Zone2)) _zone2EnteredAt = _nm.ServerTime.Time;
            TryStartZone3();
        }
        MapSceneDirector.StepChanged += HandleStepChanged;
        Edit.Log($"[Tutorial] 맵씬 존 진행 켜짐 (서버:{_isServer}) — 존1~3 교육 없음(G1 = 도착 무전 뒤 ·존2 기상 · 존3 기상+G5).", this);
    }

    private void OnDisable()
    {
        MapSceneDirector.AreaFirstEntered -= HandleAreaFirstEnteredServer;
        MapSceneDirector.GateOpened -= HandleGateOpenedServer;
        MapSceneDirector.ZoneCleared -= HandleZoneClearedServer;
        MapSceneDirector.StepChanged -= HandleStepChanged;
    }

    private void Update()
    {
        if (!_isServer || _director == null || !_director.IsTutorialActive || _director.IsFinished) return;

        if (_pendingG1)
        {
            _pendingG1 = false;
            _zone1RadioPendingSince = _nm.ServerTime.Time;   // 도착 무전은 착지 연출이 끝난 뒤(아래) — G1 은 그 무전이 끝난 뒤
        }

        if (_zone1RadioPendingSince >= 0d) TickZone1Radio();
        if (_g1WaitingRadio) TickG1AfterRadio();

        if (_pendingCorridor1Step)
        {
            _pendingCorridor1Step = false;
            _director.SetStepServer(MapSceneStep.Corridor1);
        }

        if (_zone2EnteredAt >= 0d)
        {
            if (!_zone2StepDone)
            {
                _zone2StepDone = true;
                _director.SetStepServer(MapSceneStep.Zone2);
            }
            if (!_zone2Woken && !MapSceneZone2IntroFlow.OwnsZone2Wake && _nm.ServerTime.Time - _zone2EnteredAt >= Zone2WakeDelaySeconds)
            {
                _zone2Woken = true;
                int n = _director.WakeZoneServer(MapSceneArea.Zone2);
                Edit.Log($"[Tutorial] 존2 기상 {n}마리 — 첫 진입 {Zone2WakeDelaySeconds:0}초 뒤.", this);
            }
        }

        if (_pendingZone3)
        {
            _pendingZone3 = false;
            StartZone3();
        }

        if (!_zone3EnemySeen && Time.unscaledTime >= _nextSightCheckAt)
        {
            _nextSightCheckAt = Time.unscaledTime + SightCheckInterval;
            TickZone3Sight();
        }
    }

    // ─── 서버 ────────────────────────────────────────────────────────────

    private void HandleAreaFirstEnteredServer(MapSceneArea area, ulong clientId)
    {
        if (area == MapSceneArea.Zone2 && _zone2EnteredAt < 0d)
        {
            _zone2EnteredAt = _nm.ServerTime.Time;
            Edit.Log($"[Tutorial] 존2 첫 진입(client {clientId}) — {Zone2WakeDelaySeconds:0}초 뒤 기상.", this);
        }
        else if (area == MapSceneArea.Zone3) TryStartZone3();
    }

    private void HandleGateOpenedServer(MapSceneGate gate, MapSceneGateOpenReason reason)
    {
        if (gate == MapSceneGate.G1_Corridor1Entry) _pendingCorridor1Step = true;
        else if (gate == MapSceneGate.G4_Zone3Entry) TryStartZone3();
    }

    // 1구역 도착 무전 — 정상 플레이가 가능한 시점(명세 §4.1): 로딩이 걷혔고 포드 착지 연출이 끝났다(조작 복귀). 이동을 막지 않는다.
    private void TickZone1Radio()
    {
        bool ready = MapPodArrival.IsArrivalDoneLocal && (GameManager.Instance == null || GameManager.Instance.IsMainGameReady);
        bool giveUp = _nm.ServerTime.Time - _zone1RadioPendingSince >= Zone1RadioGiveUpSeconds;
        if (!ready && !giveUp) return;
        _zone1RadioPendingSince = -1d;
        _g1WaitingRadio = _director.PlayRadioServer(TutorialGuideIds.MainZ1Enter);
        if (!ready) Edit.LogWarning($"[Tutorial] 착지 연출 끝 신호가 {Zone1RadioGiveUpSeconds:0}초 동안 안 왔다 — 1구역 도착 무전을 그냥 낸다.", this);
        if (!_g1WaitingRadio) OpenG1("도착 무전을 못 틀었다(이미 튼 판)");
    }

    // G1(존1 → 복도1)은 1구역 도착 무전의 마지막 조각이 꺼진 뒤 연다(경석 11-02 Play — 무전 중에 열려 대사를 안 듣고 지나갔다).
    // 길이 = 호스트 표시와 같은 조각 나누기 × 데이터 시간(MapSceneRadioData) — 하드코딩 없음. 늦게 붙은·재접속 클라는 게이트 복제 값을 따른다.
    private void TickG1AfterRadio()
    {
        if (_director.IsGateOpen(MapSceneGate.G1_Corridor1Entry))
        {
            _g1WaitingRadio = false;   // 구간 스킵·디버그가 먼저 열었다
            return;
        }
        double start = _director.Radio.StartServerTime;
        double now = _nm.ServerTime.Time;
        var radio = GetComponent<MapSceneRadioPresentation>();
        if (radio != null && radio.TryGetLineSeconds(TutorialGuideIds.MainZ1Enter, out float seconds))
        {
            if (now >= start + seconds) OpenG1($"도착 무전 끝({seconds:0.##}초)");
        }
        else if (now - start >= G1RadioGiveUpSeconds)
            OpenG1($"도착 무전 길이를 {G1RadioGiveUpSeconds:0}초 동안 못 쟀다 — 그냥 연다");
    }

    private void OpenG1(string why)
    {
        _g1WaitingRadio = false;
        if (!_director.IsGateOpen(MapSceneGate.G1_Corridor1Entry) && _director.TryOpenGateServer(MapSceneGate.G1_Corridor1Entry))
            Edit.Log($"[Tutorial] 존1 검사 없음 — G1 열림: {why}.", this);
    }

    private void HandleZoneClearedServer(MapSceneArea zone)
    {
        // 장부 클리어 = 기존 전투 완료 판정(G3·G6 을 여는 것과 같은 사건). 무전은 이동을 막지 않는다.
        if (zone == MapSceneArea.Zone2) _director.PlayRadioServer(TutorialGuideIds.MainZ2Clear);
        else if (zone == MapSceneArea.Zone3) _director.PlayRadioServer(TutorialGuideIds.MainZ3Clear);
    }

    // 3구역 상대 첫 인식 — 존3 핵심 몬스터(스피너봇)가 살아 있는 플레이어 누군가와 화면 거리 안. 핵심이 없으면 이 무전은 없다.
    // 존3 클리어 뒤에는 의미가 없어 더 보지 않는다(명세 §8.1 — 의미 없어진 대사 폐기).
    private void TickZone3Sight()
    {
        if (_director.IsGateOpen(MapSceneGate.G6_Corridor3Entry) || _director.IsZoneClearedServer(MapSceneArea.Zone3))
        {
            _zone3EnemySeen = true;
            return;
        }
        if (!_director.IsGateOpen(MapSceneGate.G4_Zone3Entry)) return;   // 존3 입구가 닫혀 있으면 벽 너머 — 거리만으로 "봤다" 하지 않는다

        _director.GetZoneMonstersServer(MapSceneArea.Zone3, MapSceneMonsterFilter.KeyOnly, _sightBuffer);
        if (_sightBuffer.Count == 0) return;
        float range = MapSceneRadioData.Load().Zone3SightDistance;
        foreach (KeyValuePair<ulong, NetworkClient> pair in _nm.ConnectedClients)
        {
            NetworkObject po = pair.Value.PlayerObject;
            if (po == null || !po.IsSpawned) continue;
            PlayerLifeCycleController life = po.GetComponent<PlayerLifeCycleController>();
            if (life != null && life.State != PlayerLifeState.Alive) continue;
            foreach (MonsterBase m in _sightBuffer)
            {
                if (m == null || !MapSceneZone3SightRules.InSight(po.transform.position, m.transform.position, range)) continue;
                _zone3EnemySeen = true;
                _director.PlayRadioServer(TutorialGuideIds.MainZ3Enemy);
                Edit.Log($"[Tutorial] 존3 상대 인식 — client {pair.Key} ↔ {m.name} ≤ {range:0.#}m.", this);
                return;
            }
        }
    }

    // 존3 시작 조건은 이전 존3 흐름과 같다 — 입구(G4) 열림 ∧ 파티 첫 진입.
    private void TryStartZone3()
    {
        if (_zone3Started || !_director.IsGateOpen(MapSceneGate.G4_Zone3Entry) || !_director.HasPartyEnteredServer(MapSceneArea.Zone3)) return;
        _zone3Started = true;
        _pendingZone3 = true;
    }

    private void StartZone3()
    {
        // 이전 존3 흐름은 핵심 몬스터(스피너봇)만 깨웠다. 핵심이 없으면 장부가 전멸로 클리어를 판정하므로 전부 깨운다.
        bool hasKey = _director.TryGetZoneMonsterCountServer(MapSceneArea.Zone3, MapSceneMonsterFilter.KeyOnly, out int keyTotal, out _) && keyTotal > 0;
        int n = _director.WakeZoneServer(MapSceneArea.Zone3, hasKey ? MapSceneMonsterFilter.KeyOnly : MapSceneMonsterFilter.All);
        _director.SetStepServer(MapSceneStep.Zone3Fight);
        _director.RequestGateServer(MapSceneGate.G5_SpinnerRealMode);   // 연습 없음 — G6(처치)이 이 뒤에 열리게 순서만 맞춘다
        Edit.Log($"[Tutorial] 존3 시작 — {(hasKey ? "핵심" : "전체")} {n}마리 기상 · G5(연습 없음).", this);
    }

    // ─── 로컬 ────────────────────────────────────────────────────────────

    private void HandleStepChanged(MapSceneStep from, MapSceneStep to)
    {
        if (to != MapSceneStep.Zone2 && to != MapSceneStep.Zone3Practice && to != MapSceneStep.Zone3Fight) return;
        TutorialGuideUI ui = TutorialGuideUI.Instance;
        if (ui != null && ui.CaptureState().ObjectiveId == MapSceneTraitCorridorFlow.NextId) ui.SetObjective(null);
    }
}
