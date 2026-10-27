using UnityEngine;

/// <summary>
/// 보스 진입 목표·강조·도착 정리(S10 Z4-6 · B-2) — <b>모든 피어</b>(호스트 포함) 로컬 표시. 판단은 서버(<see cref="TutorialZone4Flow"/>)가 하고
/// 결과는 복제 상태(단계 Zone4Lore/Finished · G9)로 온다 — 여기서는 그 값만 읽어 <see cref="TutorialGuideUI"/> 를 맞춘다.
/// 늦게 붙은 클라도 <see cref="TutorialDirector.StateChanged"/> 한 번으로 같은 화면이 된다. 자막은 디렉터 ClientRpc 가 띄운다.
/// 할 일 107: 존4 <b>교육</b> 표시(패널 n/4·중간보스 목표·타이머 펄스·계단/중앙 섬 강조)는 Legacy(<c>TutorialZone4EducationGuide</c>)로 뺐다.
///
/// 붙는 곳: 각 피어에서 디렉터가 튜토리얼 활성으로 보일 때 디렉터 GameObject 에 런타임으로 붙는다(일반 MonoBehaviour — 씬 무수정).
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialZone4Guide : MonoBehaviour
{
    // 안내 UI 가 아직 없으면 이 간격으로 다시 맞춘다.
    private const float RetrySeconds = 0.5f;

    private TutorialDirector _director;
    private string _shownObjectiveId;
    private bool _dirty = true;
    private float _retryAt;
    private bool _finishedCleared;
    private bool _minimapShown;

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
            _minimapShown = false;
            _shownObjectiveId = null;
            Edit.Log("[Tutorial] 보스방 도착 — 안내 정리.", this);
            return;
        }
        // 전멸 재도전 되감기(Finished → BossEnter) — 다음 도착 때 다시 정리하게 푼다(안 풀면 "보스방으로"·미니맵 펄스가 보스전 내내 남는다).
        _finishedCleared = false;

        TutorialStep step = _director.Step;
        bool g9 = _director.IsGateOpen(TutorialGate.G9_Corridor4Entry);

        ApplyObjective(ui, TutorialZone4Display.ObjectiveFor(step, g9));
        ApplyMinimap(ui, TutorialZone4Display.ShowsMinimapBossIcon(step, g9));
    }

    private void ApplyObjective(TutorialGuideUI ui, string id)
    {
        string current = ui.CaptureState().ObjectiveId;
        if (id == null)
        {
            // 내가 띄운 목표가 아직 떠 있을 때만 지운다(다른 단계가 띄운 목표는 그대로).
            if (_shownObjectiveId != null && TutorialZone4Display.IsZone4Objective(current))
                ui.SetObjective(null);
            // 존4 첫 진입(설정 자막)엔 목표 줄이 없다 — 복도3 의 "다음 구역으로"가 남아 있으면 지운다.
            else if (TutorialZone4Display.ShouldClearStaleCorridorObjective(step: _director.Step, currentObjectiveId: current))
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

        if (id == _shownObjectiveId && current == id) return;
        ui.SetObjective(id);
        _shownObjectiveId = id;
    }

    private void ApplyMinimap(TutorialGuideUI ui, bool on)
    {
        if (on == _minimapShown) return;
        if (on) ui.Highlight(TutorialHudTarget.MinimapBossIcon);
        else ui.ClearHighlight(TutorialHudTarget.MinimapBossIcon);
        _minimapShown = on;
    }
}
