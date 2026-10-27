using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 튜토리얼 존2 흐름(S8 → v8 V3) — 첫 진입 안내 → 4초 뒤 기상 → 9마리 전멸(G3 는 존 장부가 연다).
/// v8: 존2 = <b>몬스터 전멸만</b>(D13). Q·E 사용 체크·캐릭터별 Q/E 안내·슬롯 강조·미사용 넛지는 없다 — 처치 진행도만 표시한다(§2.4·§3.3).
/// 원본: Docs/design/tutorial-scenario.md §3.3·§5(존2) · Docs/tech/tutorial-corridors-zone2.md.
/// 규칙은 순수 <see cref="TutorialZone2Rules"/>·<see cref="TutorialZone2Goal"/>(TutorialZone2Model.cs).
///
/// 붙는 곳: <see cref="TutorialZone1Flow"/> 와 같다(디렉터 GameObject 런타임 부착, 모든 피어).
///
/// 서버:
///  · <see cref="MapSceneDirector.AreaFirstEntered"/>(Zone2) → 단계 Zone2 → 4초(서버 실제 시간) 뒤 <see cref="MapSceneDirector.WakeZoneServer"/>(Zone2)
///  · G3 는 건드리지 않는다 — 존 장부(S4)가 전멸 때 이미 연다.
/// 모든 피어(로컬 플레이어):
///  · 단계 Zone2 를 본 순간 자막 TUT_Z2_INTRO · 목표 TUT_Z2_GOAL(적 처치 n/장부 수)
///  · G3 → 자막 TUT_ZONE_CLEAR · 목표 "복도로" → 손을 뗀다(복도2 는 복도 흐름).
///  · 처음 그릴 때 안내 UI 가 비어 있으면(늦은 클라) RebuildFromState — 지나간 자막 생략.
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialZone2Flow : MonoBehaviour
{
    private MapSceneDirector _director;
    private NetworkManager _nm;
    private bool _isServer;

    // 서버
    private double _enteredAt = -1d;
    private bool _pendingStep;
    private bool _woken;

    // 로컬
    private TutorialGuideUI _ui;
    private bool _started;
    private bool _handedOff;

    // ─── 부착(모든 피어) ───────────────────────────────────────────────────

    // 107 Legacy — 맵씬에서 뺐다: 런타임 부착(RuntimeInitializeOnLoadMethod)을 끊었다. 존2 진행은 MapSceneZoneFlow. 보관용.
    private static void InstallBootstrap()
    {
        MapSceneDirector.StateChanged -= Ensure;
        MapSceneDirector.StateChanged += Ensure;
    }

    private static void Ensure()
    {
        MapSceneDirector d = MapSceneDirector.Instance;
        if (d == null || !d.IsSpawned || !d.IsTutorialActive) return;
        if (d.GetComponent<TutorialZone2Flow>() != null) return;
        d.gameObject.AddComponent<TutorialZone2Flow>();
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
        if (_isServer) MapSceneDirector.AreaFirstEntered += HandleAreaFirstEnteredServer;

        MapSceneDirector.StateChanged += Render;
        MapSceneDirector.GateOpened += HandleGateOpened;
        _ui = TutorialGuideUI.Instance;
        Render();
    }

    private void OnDisable()
    {
        MapSceneDirector.AreaFirstEntered -= HandleAreaFirstEnteredServer;
        MapSceneDirector.StateChanged -= Render;
        MapSceneDirector.GateOpened -= HandleGateOpened;
    }

    private bool G3Open => _director.IsGateOpen(MapSceneGate.G3_Corridor2Entry);

    private void Update()
    {
        if (_director == null) return;

        // 보스방 도착(Finished) 뒤엔 남은 기상 시계를 돌리지 않는다 — 휴면 몬스터는 휴면 그대로(S12 정리).
        if (_isServer && _director.IsTutorialActive && !_director.IsFinished)
        {
            if (_pendingStep)
            {
                _pendingStep = false;
                _director.SetStepServer(MapSceneStep.Zone2);
            }

            if (!_woken && TutorialZone2Rules.ShouldWake(_enteredAt, _nm.ServerTime.Time))
            {
                _woken = true;
                int n = _director.WakeZoneServer(MapSceneArea.Zone2);
                Edit.Log($"[Tutorial] 존2 기상 {n}마리 — 첫 진입 {TutorialZone2Rules.WakeDelaySeconds:0}초 뒤.", this);
            }
        }

        if (TutorialGuideUI.Instance != _ui)
        {
            _ui = TutorialGuideUI.Instance;
            Render();
        }
    }

    // ─── 서버 ────────────────────────────────────────────────────────────

    private void HandleAreaFirstEnteredServer(MapSceneArea area, ulong clientId)
    {
        if (area != MapSceneArea.Zone2 || _enteredAt >= 0d || !_director.IsTutorialActive) return;
        _enteredAt = _nm.ServerTime.Time;
        _pendingStep = true;
        Edit.Log($"[Tutorial] 존2 첫 진입(client {clientId}) — {TutorialZone2Rules.WakeDelaySeconds:0}초 뒤 기상.", this);
    }

    // ─── 로컬 ────────────────────────────────────────────────────────────

    private void HandleGateOpened(MapSceneGate gate, MapSceneGateOpenReason reason)
    {
        if (gate == MapSceneGate.G3_Corridor2Entry) Render();
    }

    private void Render()
    {
        if (_director == null || _nm == null || !_director.IsTutorialActive || _handedOff) return;
        TutorialGuideUI ui = TutorialGuideUI.Instance;
        if (ui == null) return;

        if (G3Open)
        {
            _handedOff = true;
            // 존2 를 지켜본 클라만 마무리 — G3 뒤에 처음 그리는(늦은) 클라는 복도 흐름 몫.
            if (!_started) return;
            ui.ShowSubtitle(TutorialGuideIds.ZoneClear);
            ui.SetObjective(TutorialGuideIds.Z1Go);
            return;
        }

        if (_director.Step != MapSceneStep.Zone2) return;

        var goal = TutorialZone2Goal.Args(
            _director.GetCounter(MapSceneCounter.Zone2Kills), _director.GetCounterTarget(MapSceneCounter.Zone2Kills));

        if (!_started)
        {
            _started = true;
            if (MapSceneGuideFlowUtil.IsGuideEmpty(ui))
            {
                // 늦은 클라 — 지나간 자막 없이 지금 상태만.
                ui.RebuildFromState(new TutorialGuideState { ObjectiveId = TutorialGuideIds.Z2Goal, ObjectiveArgs = goal });
                return;
            }

            // Z2-1 — 진입 안내(공통 문구만, 캐릭터별 Q/E 안내 없음 — v8 §2.4).
            ui.ShowSubtitle(TutorialGuideIds.Z2Intro);
            ui.ClearWait();
        }

        ui.SetObjective(TutorialGuideIds.Z2Goal, goal);
    }
}
