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
///  · 활성 순간 G1(존1 → 복도1)을 바로 연다 → 단계 Corridor1.
///  · 존2 파티 첫 진입 → 단계 Zone2 → <see cref="Zone2WakeDelaySeconds"/> 뒤 존2 기상(이전 존2 흐름과 같은 지연). G3 는 존 장부(전멸)가 연다.
///  · G4 열림 ∧ 존3 파티 첫 진입 → 존3 핵심 몬스터(스피너봇 — 없으면 전부) 기상 · 단계 Zone3Fight · G5 요청. G6 은 존 장부(처치)가 연다.
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

            // 붙기 전에 이미 지나간 신호(정상 흐름에서는 없음 — 활성 순간 붙는다)를 따라잡는다.
            if (_director.HasPartyEnteredServer(MapSceneArea.Zone2)) _zone2EnteredAt = _nm.ServerTime.Time;
            TryStartZone3();
        }
        MapSceneDirector.StepChanged += HandleStepChanged;
        Edit.Log($"[Tutorial] 맵씬 존 진행 켜짐 (서버:{_isServer}) — 존1~3 교육 없음(G1 즉시 · 존2 기상 · 존3 기상+G5).", this);
    }

    private void OnDisable()
    {
        MapSceneDirector.AreaFirstEntered -= HandleAreaFirstEnteredServer;
        MapSceneDirector.GateOpened -= HandleGateOpenedServer;
        MapSceneDirector.StepChanged -= HandleStepChanged;
    }

    private void Update()
    {
        if (!_isServer || _director == null || !_director.IsTutorialActive || _director.IsFinished) return;

        if (_pendingG1)
        {
            _pendingG1 = false;
            if (!_director.IsGateOpen(MapSceneGate.G1_Corridor1Entry) && _director.TryOpenGateServer(MapSceneGate.G1_Corridor1Entry))
                Edit.Log("[Tutorial] 존1 검사 없음 — G1 바로 열림.", this);
        }

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
            if (!_zone2Woken && _nm.ServerTime.Time - _zone2EnteredAt >= Zone2WakeDelaySeconds)
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
