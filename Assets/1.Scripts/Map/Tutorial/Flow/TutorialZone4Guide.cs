using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 튜토리얼 존4·보스 진입 목표 패널·강조·도착 정리(S10) — <b>모든 피어</b>(호스트 포함) 로컬 표시. 판단은 서버(<see cref="TutorialZone4Flow"/>)가 하고
/// 결과는 복제 상태(단계 Zone4Lore/Zone4Combat/Finished · Zone4Panels 카운터 · G9)로 온다 — 여기서는 그 값만 읽어 <see cref="TutorialGuideUI"/> 를 맞춘다.
/// 늦게 붙은 클라도 <see cref="TutorialDirector.StateChanged"/> 한 번으로 같은 화면이 된다. 자막은 디렉터 ClientRpc 가 띄운다.
///
/// 붙는 곳: 각 피어에서 디렉터가 튜토리얼 활성으로 보일 때 디렉터 GameObject 에 런타임으로 붙는다(일반 MonoBehaviour — 씬 무수정).
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialZone4Guide : MonoBehaviour
{
    // 안내 UI·존 오브젝트·중간보스(클라는 스폰·맵 생성 순서가 정해지지 않음)가 아직 없으면 이 간격으로 다시 맞춘다.
    // 가장 가까운 계단도 이 간격으로 다시 고른다.
    private const float RetrySeconds = 0.5f;
    // ZoneL_typeB 계단 메시 이름(floor_staire_1_1, _001~_003 — 2026-10-11 프리팹 확인). 바뀌면 여기만 고친다.
    private const string StairNamePrefix = "floor_staire";
    // 중간보스 찾기 반경(m, 존4 루트 기준) — 존L 발자국 안. 다른 존의 핵심 몬스터(스피너봇·23호)를 잡지 않게.
    private const float MidbossSearchRadius = 25f;

    private TutorialDirector _director;
    private string _shownObjectiveId;
    private int _shownCount = -1;
    private int _shownTotal = -1;
    private bool _dirty = true;
    private float _retryAt;
    private bool _finishedCleared;

    private TutorialZone4Highlight _hudShown;
    private Transform _stairShown;
    private Transform _midbossShown;

    private ZoneBridgeGate _gate;
    private readonly List<Transform> _stairs = new List<Transform>();
    private Transform _midboss;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InstallBootstrap()
    {
        TutorialDirector.StateChanged -= EnsureOnPeer;
        TutorialDirector.StateChanged += EnsureOnPeer;
    }

    private static void EnsureOnPeer()
    {
        TutorialDirector d = TutorialDirector.Instance;
        if (d == null || !d.IsSpawned || !d.IsTutorialActive) return;
        if (d.GetComponent<TutorialZone4Guide>() != null) return;
        d.gameObject.AddComponent<TutorialZone4Guide>();
    }

    private void OnEnable()
    {
        _director = GetComponent<TutorialDirector>();
        TutorialDirector.StateChanged += MarkDirty;
        _dirty = true;
    }

    private void OnDisable()
    {
        TutorialDirector.StateChanged -= MarkDirty;
    }

    private void MarkDirty() => _dirty = true;

    private void Update()
    {
        if (!_dirty && Time.unscaledTime < _retryAt) return;
        _dirty = false;
        _retryAt = Time.unscaledTime + RetrySeconds;
        Apply();
    }

    private void Apply()
    {
        TutorialGuideUI ui = TutorialGuideUI.Instance;
        if (_director == null || ui == null || !_director.IsTutorialActive) return;

        // B-2 보스방 도착 = 튜토리얼 안내 끝 — 목표·강조·대기 줄·남은 자막을 한 번 비운다(23호 팁은 서버가 그 뒤에 보낸다).
        if (_director.IsFinished)
        {
            if (_finishedCleared) return;
            _finishedCleared = true;
            ui.Clear();
            _hudShown = TutorialZone4Highlight.None;
            _stairShown = null;
            _midbossShown = null;
            _shownObjectiveId = null;
            Edit.Log("[Tutorial] 보스방 도착 — 안내 정리.", this);
            return;
        }
        // 전멸 재도전 되감기(Finished → BossEnter) — 다음 도착 때 다시 정리하게 푼다(안 풀면 "보스방으로"·미니맵 펄스가 보스전 내내 남는다).
        _finishedCleared = false;

        TutorialStep step = _director.Step;
        int panels = _director.GetCounter(TutorialCounter.Zone4Panels);
        int total = _director.GetCounterTarget(TutorialCounter.Zone4Panels);
        bool g9 = _director.IsGateOpen(TutorialGate.G9_Corridor4Entry);

        ApplyObjective(ui, TutorialZone4Display.ObjectiveFor(step, panels, total, g9), panels, total);
        ApplyHighlights(ui, TutorialZone4Display.HighlightsFor(step, panels, total, g9));
    }

    private void ApplyObjective(TutorialGuideUI ui, string id, int n, int total)
    {
        string current = ui.CaptureState().ObjectiveId;
        if (id == null)
        {
            // 내가 띄운 목표가 아직 떠 있을 때만 지운다(다른 단계가 띄운 목표는 그대로).
            if (_shownObjectiveId != null && TutorialZone4Display.IsZone4Objective(current))
                ui.SetObjective(null);
            // 존4 첫 진입(설정 자막)엔 목표 줄이 없다 — 복도3 의 "다음 구역으로"가 남아 있으면 지운다.
            else if (TutorialZone4Display.ShouldClearStaleCorridorObjective(_director.Step, current))
                ui.SetObjective(null);
            _shownObjectiveId = null;
            return;
        }

        // 복도4(G9 뒤) — 복도 흐름이 고르기·대기·발판 목표를 띄웠으면 "보스방으로"로 덮지 않는다(0.5초 재시도가 서로 덮어쓰는 것 방지).
        if (TutorialZone4Display.YieldsToCorridor(id, current))
        {
            _shownObjectiveId = null;
            return;
        }

        if (id == _shownObjectiveId && n == _shownCount && total == _shownTotal
            && current == id) return;

        Dictionary<string, string> args = id == TutorialGuideIds.Z4PanelGoal
            ? new Dictionary<string, string> { { "n", n.ToString() }, { "total", total.ToString() } }
            : null;
        ui.SetObjective(id, args);
        _shownObjectiveId = id;
        _shownCount = n;
        _shownTotal = total;
    }

    private void ApplyHighlights(TutorialGuideUI ui, TutorialZone4Highlight want)
    {
        SetHud(ui, want, TutorialZone4Highlight.BossTimer, TutorialHudTarget.BossTimer);
        SetHud(ui, want, TutorialZone4Highlight.MinimapBossIcon, TutorialHudTarget.MinimapBossIcon);

        bool route = (want & TutorialZone4Highlight.MidbossRoute) != 0;
        SetWorld(ui, ref _stairShown, route ? NearestStair() : null);
        SetWorld(ui, ref _midbossShown, route ? FindMidboss() : null);
    }

    private void SetHud(TutorialGuideUI ui, TutorialZone4Highlight want, TutorialZone4Highlight flag, TutorialHudTarget target)
    {
        bool on = (want & flag) != 0;
        bool shown = (_hudShown & flag) != 0;
        if (on == shown) return;
        if (on)
        {
            ui.Highlight(target);
            _hudShown |= flag;
        }
        else
        {
            ui.ClearHighlight(target);
            _hudShown &= ~flag;
        }
    }

    private static void SetWorld(TutorialGuideUI ui, ref Transform shown, Transform want)
    {
        if (shown == want) return;
        if (shown != null) ui.ClearHighlight(shown);
        if (want != null) ui.Highlight(want);
        shown = want;
    }

    // ─── 존4 오브젝트 찾기(클라 — 존은 비네트워크, 각 피어가 로컬 생성) ─────────

    private bool ResolveGate()
    {
        if (_gate != null) return true;
        _gate = FindFirstObjectByType<ZoneBridgeGate>();
        if (_gate == null) return false;

        _stairs.Clear();
        foreach (Transform t in _gate.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith(StairNamePrefix)) _stairs.Add(t);
        if (_stairs.Count == 0)
            Edit.LogWarning($"[Tutorial] 존4 '{_gate.name}' 에 '{StairNamePrefix}*' 계단이 없다 — 계단 강조 없이 중앙 섬만 강조한다.", this);
        return true;
    }

    /// <summary>로컬 플레이어에게 가장 가까운 계단(수평 거리). 플레이어·계단이 없으면 null.</summary>
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

    /// <summary>중앙 섬의 중간보스 — 존4 게이트에 가장 가까운 비일반 몬스터(클라는 복제 순서가 정해지지 않아 재시도).</summary>
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
