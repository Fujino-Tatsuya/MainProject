using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 튜토리얼 존3 목표 패널·강조(S9) — <b>모든 피어</b>(호스트 포함) 로컬 표시. 판단은 서버(<see cref="TutorialZone3Flow"/>)가 하고 결과는 복제 상태
/// (단계 Zone3Practice/Zone3Fight · G6 · Zone3Interrupts 카운터)로 온다 — 여기서는 그 값만 읽어 <see cref="TutorialGuideUI"/> 를 맞춘다.
/// 늦게 붙은 클라도 <see cref="TutorialDirector.StateChanged"/>(스폰 직후 1회 포함) 한 번으로 같은 화면이 된다. 자막은 디렉터 ClientRpc 가 띄운다.
///
/// 붙는 곳: 각 피어에서 디렉터가 튜토리얼 활성으로 보일 때 디렉터 GameObject 에 런타임으로 붙는다(일반 MonoBehaviour — 씬 무수정).
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialZone3Guide : MonoBehaviour
{
    // 안내 UI·스피너봇(클라는 스폰 복제 순서가 정해지지 않음)이 아직 없으면 이 간격으로 다시 맞춘다.
    private const float RetrySeconds = 0.5f;

    private TutorialDirector _director;
    private Transform _spinner;
    private bool _highlighted;
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

        TutorialStep step = _director.Step;
        bool g6 = _director.IsGateOpen(TutorialGate.G6_Corridor3Entry);

        ApplyObjective(ui, TutorialZone3Display.ObjectiveFor(step, g6));
        ApplyHighlights(ui, TutorialZone3Display.HighlightsFor(step, g6));
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
        ui.SetObjective(id, args);
        _shownObjectiveId = id;
        _shownCount = n;
        _shownTotal = total;
    }

    private void ApplyHighlights(TutorialGuideUI ui, bool want)
    {
        if (!want)
        {
            if (!_highlighted) return;
            ui.ClearHighlight(TutorialHudTarget.SkillSlotRmb);
            if (_highlightedSpinner != null) ui.ClearHighlight(_highlightedSpinner);
            _highlighted = false;
            _highlightedSpinner = null;
            return;
        }

        if (!_highlighted)
        {
            ui.Highlight(TutorialHudTarget.SkillSlotRmb);
            _highlighted = true;
        }

        // 스피너봇은 클라 복제가 늦을 수 있다 — 찾을 때까지 재시도 주기마다 다시 본다.
        if (_highlightedSpinner == null)
        {
            if (_spinner == null)
            {
                TutorialSpinnerPractice practice = FindFirstObjectByType<TutorialSpinnerPractice>();
                if (practice != null) _spinner = practice.transform;
            }
            if (_spinner != null)
            {
                ui.Highlight(_spinner);
                _highlightedSpinner = _spinner;
            }
        }
    }
}
