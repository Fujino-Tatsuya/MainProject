using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 튜토리얼 존2 흐름(S8) — 첫 진입 안내 → 4초 뒤 기상 → Q·E 안내·넛지 → 9마리 전멸(G3 는 존 장부가 연다).
/// 원본: Docs/design/tutorial-scenario.md §3.3·§5(존2) · Docs/tech/tutorial-corridors-zone2.md.
/// 규칙은 순수 <see cref="TutorialZone2Rules"/>·<see cref="TutorialZone2Guide"/>·<see cref="TutorialZone2Goal"/>(TutorialZone2Model.cs).
///
/// 붙는 곳: <see cref="TutorialZone1Flow"/> 와 같다(디렉터 GameObject 런타임 부착, 모든 피어).
///
/// 서버:
///  · <see cref="TutorialDirector.AreaFirstEntered"/>(Zone2) → 단계 Zone2 → 4초(서버 실제 시간) 뒤 <see cref="TutorialDirector.WakeZoneServer"/>(Zone2)
///  · <see cref="PlayerSkillController.ServerSkillUsed"/>(Main/Sub) → <see cref="TutorialDirector.ReportCheckServer"/>(SkillQ/SkillE) — 존2 진입 뒤·G3 전만(개인 안내용, 게이트 아님 D13)
///  · G3 는 건드리지 않는다 — 존 장부(S4)가 전멸 때 이미 연다.
/// 모든 피어(로컬 플레이어):
///  · 단계 Zone2 를 본 순간 자막 TUT_Z2_INTRO + 캐릭터별 TUT_Z2_Q · Q 칸 강조 · 목표 TUT_Z2_GOAL(Q·E 사용 체크·처치 n/장부 수)
///  · Q 첫 사용(복제 체크) 또는 기상 10초 뒤 → 캐릭터별 TUT_Z2_E · E 칸 강조(E 를 쓸 때까지) · 기상 15초에 안 쓴 키만 TUT_Z2_NUDGE
///  · G3 → 자막 TUT_ZONE_CLEAR · 목표 "복도로" · 강조 해제 → 손을 뗀다(복도2 는 복도 흐름).
///  · 처음 그릴 때 안내 UI 가 비어 있으면(늦은 클라) RebuildFromState — 지나간 자막·시간 안내 생략.
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialZone2Flow : MonoBehaviour
{
    private TutorialDirector _director;
    private NetworkManager _nm;
    private bool _isServer;

    // 서버
    private double _enteredAt = -1d;
    private bool _pendingStep;
    private bool _woken;

    // 로컬
    private readonly TutorialZone2Guide _guide = new TutorialZone2Guide();
    private TutorialGuideUI _ui;
    private bool _started;
    private bool _handedOff;
    private bool _highlightQ, _highlightE;

    // ─── 부착(모든 피어) ───────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InstallBootstrap()
    {
        TutorialDirector.StateChanged -= Ensure;
        TutorialDirector.StateChanged += Ensure;
    }

    private static void Ensure()
    {
        TutorialDirector d = TutorialDirector.Instance;
        if (d == null || !d.IsSpawned || !d.IsTutorialActive) return;
        if (d.GetComponent<TutorialZone2Flow>() != null) return;
        d.gameObject.AddComponent<TutorialZone2Flow>();
    }

    // ─── 생명주기 ────────────────────────────────────────────────────────

    private void OnEnable()
    {
        _director = GetComponent<TutorialDirector>();
        _nm = NetworkManager.Singleton;
        if (_director == null || _nm == null || !_nm.IsListening)
        {
            enabled = false;
            return;
        }

        _isServer = _nm.IsServer;
        if (_isServer)
        {
            TutorialDirector.AreaFirstEntered += HandleAreaFirstEnteredServer;
            PlayerSkillController.ServerSkillUsed += HandleSkillUsedServer;
        }

        TutorialDirector.StateChanged += Render;
        TutorialDirector.GateOpened += HandleGateOpened;
        _ui = TutorialGuideUI.Instance;
        Render();
    }

    private void OnDisable()
    {
        TutorialDirector.AreaFirstEntered -= HandleAreaFirstEnteredServer;
        PlayerSkillController.ServerSkillUsed -= HandleSkillUsedServer;
        TutorialDirector.StateChanged -= Render;
        TutorialDirector.GateOpened -= HandleGateOpened;
    }

    private bool G3Open => _director.IsGateOpen(TutorialGate.G3_Corridor2Entry);

    private void Update()
    {
        if (_director == null) return;

        // 보스방 도착(Finished) 뒤엔 남은 기상 시계를 돌리지 않는다 — 휴면 몬스터는 휴면 그대로(S12 정리).
        if (_isServer && _director.IsTutorialActive && !_director.IsFinished)
        {
            if (_pendingStep)
            {
                _pendingStep = false;
                _director.SetStepServer(TutorialStep.Zone2);
            }

            if (!_woken && TutorialZone2Rules.ShouldWake(_enteredAt, _nm.ServerTime.Time))
            {
                _woken = true;
                int n = _director.WakeZoneServer(TutorialArea.Zone2);
                Edit.Log($"[Tutorial] 존2 기상 {n}마리 — 첫 진입 {TutorialZone2Rules.WakeDelaySeconds:0}초 뒤.", this);
            }
        }

        if (TutorialGuideUI.Instance != _ui)
        {
            _ui = TutorialGuideUI.Instance;
            Render();
        }

        TickLocal();
    }

    // ─── 서버 ────────────────────────────────────────────────────────────

    private void HandleAreaFirstEnteredServer(TutorialArea area, ulong clientId)
    {
        if (area != TutorialArea.Zone2 || _enteredAt >= 0d || !_director.IsTutorialActive) return;
        _enteredAt = _nm.ServerTime.Time;
        _pendingStep = true;
        Edit.Log($"[Tutorial] 존2 첫 진입(client {clientId}) — {TutorialZone2Rules.WakeDelaySeconds:0}초 뒤 기상.", this);
    }

    private void HandleSkillUsedServer(Player player, PlayerSkillSlot slot)
    {
        if (player == null || _enteredAt < 0d || !_director.IsTutorialActive || G3Open) return;
        TutorialCheck check;
        switch (slot)
        {
            case PlayerSkillSlot.Main: check = TutorialCheck.SkillQ; break;
            case PlayerSkillSlot.Sub: check = TutorialCheck.SkillE; break;
            default: return;
        }
        _director.ReportCheckServer(player.OwnerClientId, check);
    }

    // ─── 로컬 ────────────────────────────────────────────────────────────

    private bool Zone2Running =>
        _director != null && _director.IsTutorialActive && _director.Step == TutorialStep.Zone2 && !G3Open;

    private void GetLocalSkillUse(out bool usedQ, out bool usedE)
    {
        usedQ = usedE = false;
        if (_director.TryGetPlayerProgress(_nm.LocalClientId, out TutorialPlayerProgress p))
        {
            usedQ = p.Has(TutorialCheck.SkillQ);
            usedE = p.Has(TutorialCheck.SkillE);
        }
    }

    private void TickLocal()
    {
        if (!_started || _handedOff || !Zone2Running) return;
        TutorialGuideUI ui = TutorialGuideUI.Instance;

        GetLocalSkillUse(out bool usedQ, out bool usedE);
        TutorialZone2Cue cue = _guide.Tick(Time.unscaledDeltaTime, usedQ, usedE);
        if (ui != null)
        {
            if (cue.ShowEPrompt) ui.ShowSubtitle(TutorialGuideIds.Z2E);
            if (cue.Nudge != TutorialZone2Keys.None) ui.ShowSubtitle(TutorialGuideIds.Z2Nudge, NudgeArgs(cue.Nudge));
        }
        SyncHighlights(ui, usedE);
    }

    private static Dictionary<string, string> NudgeArgs(TutorialZone2Keys keys)
    {
        var names = new Dictionary<string, string>();
        TutorialLocalPlayerInfo.AddSkillNames(Player.LocalPlayer, names);
        names.TryGetValue("Q", out string q);
        names.TryGetValue("E", out string e);
        return new Dictionary<string, string>
        {
            { "키", TutorialZone2Guide.KeyLabel(keys) },
            { "스킬명", TutorialZone2Guide.SkillLabel(keys, q, e) },
        };
    }

    // 같은 강조를 매번 다시 걸면 UI 가 HUD 칸 찾기 재시도·경고를 초기화한다 — 바뀔 때만.
    private void SyncHighlights(TutorialGuideUI ui, bool usedE)
    {
        bool q = _guide.HighlightQ;
        bool e = _guide.HighlightE(usedE);
        if (ui == null) return;
        if (q != _highlightQ)
        {
            if (q) ui.Highlight(TutorialHudTarget.SkillSlotQ, TutorialHighlightStyle.Pulse);
            else ui.ClearHighlight(TutorialHudTarget.SkillSlotQ);
        }
        if (e != _highlightE)
        {
            if (e) ui.Highlight(TutorialHudTarget.SkillSlotE, TutorialHighlightStyle.Pulse);
            else ui.ClearHighlight(TutorialHudTarget.SkillSlotE);
        }
        _highlightQ = q;
        _highlightE = e;
    }

    private void HandleGateOpened(TutorialGate gate, TutorialGateOpenReason reason)
    {
        if (gate == TutorialGate.G3_Corridor2Entry) Render();
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
            ui.ClearHighlight(TutorialHudTarget.SkillSlotQ);
            ui.ClearHighlight(TutorialHudTarget.SkillSlotE);
            _highlightQ = _highlightE = false;
            ui.ShowSubtitle(TutorialGuideIds.ZoneClear);
            ui.SetObjective(TutorialGuideIds.Z1Go);
            return;
        }

        if (_director.Step != TutorialStep.Zone2) return;

        GetLocalSkillUse(out bool usedQ, out bool usedE);
        Dictionary<string, string> goal = TutorialZone2Goal.Args(usedQ, usedE,
            _director.GetCounter(TutorialCounter.Zone2Kills), _director.GetCounterTarget(TutorialCounter.Zone2Kills));

        if (!_started)
        {
            _started = true;
            if (TutorialGuideFlowUtil.IsGuideEmpty(ui))
            {
                // 늦은 클라 — 지나간 자막·시간 안내 없이 지금 상태만.
                _guide.SkipTimedCues();
                bool e = _guide.HighlightE(usedE);
                ui.RebuildFromState(new TutorialGuideState
                {
                    ObjectiveId = TutorialGuideIds.Z2Goal,
                    ObjectiveArgs = goal,
                    Highlights = e
                        ? new[] { new TutorialHighlightRequest(TutorialHudTarget.SkillSlotE, TutorialHighlightStyle.Pulse) }
                        : System.Array.Empty<TutorialHighlightRequest>(),
                });
                _highlightQ = false;
                _highlightE = e;
                return;
            }

            // Z2-1 — 진입 안내 + 캐릭터별 Q 한 줄(자막 큐 순서대로).
            ui.ShowSubtitle(TutorialGuideIds.Z2Intro);
            ui.ShowSubtitle(TutorialGuideIds.Z2Q);
            ui.ClearWait();
        }

        ui.SetObjective(TutorialGuideIds.Z2Goal, goal);
        SyncHighlights(ui, usedE);
    }
}
