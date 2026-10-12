using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 튜토리얼 존3 목표 패널·강조(S9 → v8) — <b>모든 피어</b>(호스트 포함) 로컬 표시. 판단은 서버(<see cref="TutorialZone3Flow"/>)가 하고 결과는 복제 상태
/// (단계 Zone3Practice/Zone3Fight · G6 · Zone3Interrupts(연계) 카운터 · 스피너봇 <see cref="TutorialSpinnerPractice.ComboPhase"/>·유효 벽 지점)로 온다 —
/// 여기서는 그 값만 읽어 <see cref="TutorialGuideUI"/> 를 맞춘다. 준비·간파 = 간파 목표 + 스피너봇·우클릭 칸 · 취약 = 밀치기 목표 + 유효 벽(화살표)·스피너봇·우클릭 칸 ·
/// 회복/복귀 = 연계 n/3 목표만.
/// 늦게 붙은 클라도 <see cref="TutorialDirector.StateChanged"/>(스폰 직후 1회 포함) 한 번으로 같은 화면이 된다. 자막은 디렉터 ClientRpc 가 띄운다.
///
/// 붙는 곳: 각 피어에서 디렉터가 튜토리얼 활성으로 보일 때 디렉터 GameObject 에 런타임으로 붙는다(일반 MonoBehaviour — 씬 무수정).
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialZone3Guide : MonoBehaviour
{
    // 안내 UI·스피너봇(클라는 스폰 복제 순서가 정해지지 않음)이 아직 없으면 이 간격으로 다시 맞춘다.
    // 연계 단계(스피너봇 복제 변수)는 디렉터 StateChanged 로 오지 않아 이 간격으로 따라간다.
    private const float RetrySeconds = 0.2f;
    private const float WallMarkerHeight = 1.5f;

    private TutorialDirector _director;
    private TutorialSpinnerPractice _practice;
    private Transform _spinner;
    private Transform _wallMarker;
    private bool _highlightedRmb;
    private bool _highlightedWall;
    private Transform _highlightedSpinner;
    private string _shownObjectiveId;
    private int _shownCount = -1;
    private int _shownTotal = -1;
    private bool _dirty = true;
    private float _retryAt;

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
        if (d.GetComponent<TutorialZone3Guide>() != null) return;
        d.gameObject.AddComponent<TutorialZone3Guide>();
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

        // 스피너봇은 클라 복제가 늦을 수 있다 — 찾을 때까지 재시도 주기마다 다시 본다. 연계 단계가 바뀌면 다시 그린다.
        if (_practice == null) _practice = FindFirstObjectByType<TutorialSpinnerPractice>();
        if (_practice != null && _spinner == null) _spinner = _practice.transform;
        SpinnerComboPhase combo = _practice != null ? _practice.ComboPhase : SpinnerComboPhase.Ready;

        TutorialStep step = _director.Step;
        bool g6 = _director.IsGateOpen(TutorialGate.G6_Corridor3Entry);

        ApplyObjective(ui, TutorialZone3Display.ObjectiveFor(step, g6, combo));
        ApplyHighlights(ui, TutorialZone3Display.HighlightsFor(step, g6, combo));
    }

    private void ApplyObjective(TutorialGuideUI ui, string id)
    {
        if (id == null)
        {
            // 존3 을 벗어났다 — 내가 띄운 목표가 아직 떠 있을 때만 지운다(다음 단계가 이미 띄운 목표는 그대로).
            if (_shownObjectiveId != null && TutorialZone3Display.IsZone3Objective(ui.CaptureState().ObjectiveId))
                ui.SetObjective(null);
            _shownObjectiveId = null;
            return;
        }

        int n = _director.GetCounter(TutorialCounter.Zone3Interrupts);
        int total = _director.GetCounterTarget(TutorialCounter.Zone3Interrupts);
        if (id == _shownObjectiveId && n == _shownCount && total == _shownTotal
            && ui.CaptureState().ObjectiveId == id) return;

        var args = new Dictionary<string, string> { { "n", n.ToString() }, { "total", total.ToString() } };
        // 연계 n/3 — TUT_Z3_GOAL 은 문구에 있고, 단계별 목표(간파·밀치기)는 진행도 칸으로 보인다.
        TutorialProgress progress = id == TutorialGuideIds.Z3Goal || id == TutorialGuideIds.Z3Boss
            ? default
            : new TutorialProgress(n, total);
        ui.SetObjective(id, args, null, progress);
        _shownObjectiveId = id;
        _shownCount = n;
        _shownTotal = total;
    }

    private void ApplyHighlights(TutorialGuideUI ui, TutorialZone3Highlight want)
    {
        // 우클릭 칸
        bool rmb = (want & TutorialZone3Highlight.RmbSlot) != 0;
        if (rmb != _highlightedRmb)
        {
            if (rmb) ui.Highlight(TutorialHudTarget.SkillSlotRmb);
            else ui.ClearHighlight(TutorialHudTarget.SkillSlotRmb);
            _highlightedRmb = rmb;
        }

        // 스피너봇(월드)
        bool spinner = (want & TutorialZone3Highlight.Spinner) != 0 && _spinner != null;
        if (spinner && _highlightedSpinner == null)
        {
            ui.Highlight(_spinner);
            _highlightedSpinner = _spinner;
        }
        else if (!spinner && _highlightedSpinner != null)
        {
            ui.ClearHighlight(_highlightedSpinner);
            _highlightedSpinner = null;
        }

        // 유효 벽(연습용 지정 벽) — 복제된 표시 지점에 로컬 표식을 두고 강조한다(Arrow — 간파 신호와 구분).
        bool wall = (want & TutorialZone3Highlight.Wall) != 0 && EnsureWallMarker();
        if (wall != _highlightedWall)
        {
            if (wall) ui.Highlight(_wallMarker, TutorialHighlightStyle.Arrow);
            else if (_wallMarker != null) ui.ClearHighlight(_wallMarker);
            _highlightedWall = wall;
        }
    }

    private bool EnsureWallMarker()
    {
        if (_practice == null || !_practice.TryGetWallPoint(out Vector3 point)) return false;
        if (_wallMarker == null)
        {
            var go = new GameObject("TutorialZone3WallMarker");
            _wallMarker = go.transform;
        }
        _wallMarker.position = point + Vector3.up * WallMarkerHeight;
        return true;
    }

    private void OnDestroy()
    {
        if (_wallMarker != null) Destroy(_wallMarker.gameObject);
    }
}
