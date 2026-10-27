using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 할 일 107 Legacy — 튜토리얼 존4 의 <b>교육</b> 부분(S10 Z4-2~Z4-5 중 장치 설명·첫 패널·패널 n/4·중간보스 목표·간파 리마인드·강조).
// 맵씬에서 뺐다(로비 브리핑이 대신). 존4 진행·보스 진입 안내는 TutorialZone4Flow·TutorialZone4Guide 에 남았다.
// 🔴 보관용 — 런타임 부착(RuntimeInitializeOnLoadMethod)이 없다. 다시 쓰려면 디렉터 GameObject 에 붙이는 부트스트랩을 새로 단다.
// 원래 한 파일이던 판(분리 전)은 git fd6f4292 의 Assets/1.Scripts/Map/Tutorial/Flow/TutorialZone4Flow.cs·TutorialZone4Guide.cs·TutorialZone4Rules.cs.

/// <summary>존4 교육 강조 대상(동시에 여러 개).</summary>
[Flags]
public enum TutorialZone4Highlight : byte
{
    None = 0,
    /// <summary>타이머 게이지 펄스(Z4-0 — 설정 자막 동안).</summary>
    BossTimer = 1 << 0,
    /// <summary>가장 가까운 계단 + 중앙 섬(중간보스) — 다리 개통 뒤 처치 전(Z4-4).</summary>
    MidbossRoute = 1 << 1,
}

/// <summary>존4 교육 목표·강조 — 복제 상태(단계·패널 카운터·G9)만으로 정한다.</summary>
public static class TutorialZone4EducationDisplay
{
    /// <summary>지금 띄울 교육 목표 id(패널 n/4 → 중간보스). 존4 전투 전·G9 뒤·도착 뒤면 null.</summary>
    public static string ObjectiveFor(TutorialStep step, int panels, int panelTarget, bool corridor4EntryOpen)
    {
        if (step == TutorialStep.Finished || corridor4EntryOpen || step < TutorialStep.Zone4Combat) return null;
        return panels < Math.Max(1, panelTarget) ? TutorialGuideIds.Z4PanelGoal : TutorialGuideIds.Z4Midboss;
    }

    public static TutorialZone4Highlight HighlightsFor(TutorialStep step, int panels, int panelTarget, bool corridor4EntryOpen)
    {
        if (step == TutorialStep.Finished || corridor4EntryOpen) return TutorialZone4Highlight.None;
        if (step == TutorialStep.Zone4Lore) return TutorialZone4Highlight.BossTimer;
        if (step >= TutorialStep.Zone4Combat && panels >= Math.Max(1, panelTarget)) return TutorialZone4Highlight.MidbossRoute;
        return TutorialZone4Highlight.None;
    }

    public static bool IsEducationObjective(string id) => id == TutorialGuideIds.Z4PanelGoal || id == TutorialGuideIds.Z4Midboss;
}

/// <summary>
/// [Legacy · 서버] 존4 교육 자막·카운터 — G8 → TUT_Z4_START·TUT_Z4_PANEL_INTRO(소개 연출이 꺼진 판만) · 패널마다 Zone4Panels +1(첫 1번 TUT_Z4_PANEL_FIRST) ·
/// 다리 개통 뒤 중간보스 첫 간파 창 → TUT_Z4_COUNTER_REMIND.
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialZone4EducationFlow : MonoBehaviour
{
    private TutorialDirector _director;
    private bool _introMode;
    private bool _panelFirstShown;
    private bool _bridgeOpened;
    private bool _counterRemindShown;
    private MonsterCounterWindow _midbossWindow;
    private bool _midbossWindowWasOpen;
    private readonly List<MonsterBase> _buffer = new List<MonsterBase>();

    private void OnEnable()
    {
        _director = GetComponent<TutorialDirector>();
        NetworkManager nm = NetworkManager.Singleton;
        if (_director == null || nm == null || !nm.IsServer)
        {
            enabled = false;
            return;
        }
        _introMode = TutorialZone4IntroData.Load().IntroEnabled;
        TutorialDirector.GateOpened += HandleGateOpened;
        TutorialDirector.ZoneCleared += HandleZoneCleared;
        ZoneBridgeGateManager.ServerPanelActivated += HandlePanelActivated;
        ZoneBridgeGateManager.ServerBridgeOpened += HandleBridgeOpened;
    }

    private void OnDisable()
    {
        TutorialDirector.GateOpened -= HandleGateOpened;
        TutorialDirector.ZoneCleared -= HandleZoneCleared;
        ZoneBridgeGateManager.ServerPanelActivated -= HandlePanelActivated;
        ZoneBridgeGateManager.ServerBridgeOpened -= HandleBridgeOpened;
    }

    private void Update()
    {
        if (_midbossWindow == null || _director == null || _director.IsFinished) return;
        bool open = _midbossWindow.IsOpen;
        if (open && !_midbossWindowWasOpen && !_counterRemindShown)
        {
            _counterRemindShown = true;
            _director.ShowSubtitleServer(TutorialGuideIds.Z4CounterRemind);
            _midbossWindow = null;   // 1번이면 끝
            return;
        }
        _midbossWindowWasOpen = open;
    }

    private void HandleGateOpened(TutorialGate gate, TutorialGateOpenReason reason)
    {
        // 소개 연출 판에서는 장치 설명을 연출이 대신하고, 차오르기 시작은 타이머 HUD 가 보여 준다(경석 10-26 D8).
        if (gate != TutorialGate.G8_TimerStart || _introMode) return;
        _director.ShowSubtitleServer(TutorialGuideIds.Z4Start);
        _director.ShowSubtitleServer(TutorialGuideIds.Z4PanelIntro);
    }

    private void HandlePanelActivated(int slotId, int panelIndex, int activated, int panelCount)
    {
        if (!_panelFirstShown)
        {
            _panelFirstShown = true;
            _director.ShowSubtitleServer(TutorialGuideIds.Z4PanelFirst);
        }
        _director.AddCounterServer(TutorialCounter.Zone4Panels);   // 목표(4)에서 멈춘다
    }

    private void HandleBridgeOpened(int slotId)
    {
        if (_bridgeOpened) return;
        _bridgeOpened = true;
        _director.GetZoneMonstersServer(TutorialArea.Zone4, TutorialMonsterFilter.KeyOnly, _buffer);
        _midbossWindow = _buffer.Count > 0 ? _buffer[0].GetComponent<MonsterCounterWindow>() : null;
        _midbossWindowWasOpen = _midbossWindow != null && _midbossWindow.IsOpen;
        _buffer.Clear();
    }

    private void HandleZoneCleared(TutorialArea zone)
    {
        if (zone == TutorialArea.Zone4) _midbossWindow = null;
    }
}

/// <summary>[Legacy · 모든 피어] 존4 교육 목표(패널 n/4 → 중간보스)·강조(타이머 펄스·가장 가까운 계단·중앙 섬 중간보스).</summary>
[DisallowMultipleComponent]
public sealed class TutorialZone4EducationGuide : MonoBehaviour
{
    private const float RetrySeconds = 0.5f;
    // ZoneL_typeB 계단 메시 이름(floor_staire_1_1, _001~_003 — 2026-10-11 프리팹 확인).
    private const string StairNamePrefix = "floor_staire";
    // 중간보스 찾기 반경(m, 존4 루트 기준) — 다른 존의 핵심 몬스터(스피너봇·23호)를 잡지 않게.
    private const float MidbossSearchRadius = 25f;

    private TutorialDirector _director;
    private string _shownObjectiveId;
    private int _shownCount = -1;
    private int _shownTotal = -1;
    private float _retryAt;
    private TutorialZone4Highlight _hudShown;
    private Transform _stairShown;
    private Transform _midbossShown;
    private ZoneBridgeGate _gate;
    private readonly List<Transform> _stairs = new List<Transform>();
    private Transform _midboss;

    private void OnEnable() => _director = GetComponent<TutorialDirector>();

    private void Update()
    {
        if (Time.unscaledTime < _retryAt) return;
        _retryAt = Time.unscaledTime + RetrySeconds;

        TutorialGuideUI ui = TutorialGuideUI.Instance;
        if (_director == null || ui == null || !_director.IsTutorialActive) return;

        TutorialStep step = _director.Step;
        int panels = _director.GetCounter(TutorialCounter.Zone4Panels);
        int total = _director.GetCounterTarget(TutorialCounter.Zone4Panels);
        bool g9 = _director.IsGateOpen(TutorialGate.G9_Corridor4Entry);

        string id = TutorialZone4EducationDisplay.ObjectiveFor(step, panels, total, g9);
        string current = ui.CaptureState().ObjectiveId;
        if (id == null)
        {
            if (_shownObjectiveId != null && TutorialZone4EducationDisplay.IsEducationObjective(current)) ui.SetObjective(null);
            _shownObjectiveId = null;
        }
        else if (id != _shownObjectiveId || panels != _shownCount || total != _shownTotal || current != id)
        {
            Dictionary<string, string> args = id == TutorialGuideIds.Z4PanelGoal
                ? new Dictionary<string, string> { { "n", panels.ToString() }, { "total", total.ToString() } }
                : null;
            ui.SetObjective(id, args);
            _shownObjectiveId = id;
            _shownCount = panels;
            _shownTotal = total;
        }

        TutorialZone4Highlight want = TutorialZone4EducationDisplay.HighlightsFor(step, panels, total, g9);
        bool timer = (want & TutorialZone4Highlight.BossTimer) != 0;
        if (timer != ((_hudShown & TutorialZone4Highlight.BossTimer) != 0))
        {
            if (timer) ui.Highlight(TutorialHudTarget.BossTimer);
            else ui.ClearHighlight(TutorialHudTarget.BossTimer);
            _hudShown = timer ? TutorialZone4Highlight.BossTimer : TutorialZone4Highlight.None;
        }
        bool route = (want & TutorialZone4Highlight.MidbossRoute) != 0;
        SetWorld(ui, ref _stairShown, route ? NearestStair() : null);
        SetWorld(ui, ref _midbossShown, route ? FindMidboss() : null);
    }

    private static void SetWorld(TutorialGuideUI ui, ref Transform shown, Transform want)
    {
        if (shown == want) return;
        if (shown != null) ui.ClearHighlight(shown);
        if (want != null) ui.Highlight(want);
        shown = want;
    }

    private bool ResolveGate()
    {
        if (_gate != null) return true;
        _gate = FindFirstObjectByType<ZoneBridgeGate>();
        if (_gate == null) return false;
        _stairs.Clear();
        foreach (Transform t in _gate.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith(StairNamePrefix)) _stairs.Add(t);
        return true;
    }

    private Transform NearestStair()
    {
        if (!ResolveGate() || _stairs.Count == 0) return null;
        NetworkObject po = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClient?.PlayerObject : null;
        if (po == null) return _stairShown;
        Vector3 p = po.transform.position;
        Transform best = null;
        float bestSqr = float.MaxValue;
        foreach (Transform s in _stairs)
        {
            if (s == null) continue;
            Vector3 d = s.position - p;
            float sqr = d.x * d.x + d.z * d.z;
            if (sqr < bestSqr) { bestSqr = sqr; best = s; }
        }
        return best;
    }

    private Transform FindMidboss()
    {
        if (_midboss != null) return _midboss;
        if (!ResolveGate()) return null;
        Vector3 c = _gate.transform.position;
        float bestSqr = MidbossSearchRadius * MidbossSearchRadius;
        foreach (MonsterBase m in FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
        {
            if (m == null || m.Rank == MonsterRank.Normal) continue;
            float sqr = (m.transform.position - c).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; _midboss = m.transform; }
        }
        return _midboss;
    }
}
