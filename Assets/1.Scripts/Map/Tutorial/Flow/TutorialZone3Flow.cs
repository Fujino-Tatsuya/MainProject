using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 튜토리얼 존3 흐름(S9, §3.5 Z3-1~Z3-8) — <b>서버 전용</b>. 규칙은 순수 <see cref="TutorialZone3Model"/>·<see cref="TutorialZone3Geometry"/>,
/// 이 컴포넌트는 디렉터·스피너봇·간파 신호를 모델에 넣고 결과(기상·모드 전환·카운터·단계·자막)를 실행한다.
/// 표시(목표 패널·강조)는 모든 피어의 <see cref="TutorialZone3Guide"/> 가 복제 상태로 그린다. 상세: Docs/tech/tutorial-zone3.md.
///
/// 붙는 곳: 서버에서 디렉터가 튜토리얼을 켠 순간(<see cref="TutorialDirector.StateChanged"/> ∧ <see cref="TutorialDirector.IsTutorialActive"/>)
/// 디렉터 GameObject 에 런타임으로 붙는다(<see cref="TutorialDeathRules"/> 와 같은 방식 — 씬·프리팹 저장 없음).
///
/// 흐름(v8 §3.5 — 간파 → 취약 밀치기 → 벽 충돌 → 원위치 복귀, 연계 파티 합계 3회 → 본 전투):
///  · 활성 순간: 연습용 유효 벽·연습 위치(<see cref="TutorialZone3Geometry.ChoosePracticeLayout"/> — 존3 긴 변)를 정해 스피너봇을 옮기고
///    (휴면 중), 연습 반경 = 그 위치 → 출구 배리어(G6) 거리 − 몸 반경 − 여유.
///  · G4 열림 ∧ 존3 파티 첫 진입 → 스피너봇 휴면 해제 → <see cref="TutorialSpinnerPractice.EnterPractice"/> · 단계 Zone3Practice ·
///    자막 TUT_Z3_INTRO → TUT_Z3_PRACTICE_NOTE(공통 — 캐릭터별 간파 설명 없음, §2.4).
///  · 간파 성공(준비 단계) = <see cref="PlayerInterruptSkillBase.ServerInterruptSucceeded"/>(연습 중 · 시전자 존3 안 — 존3 의 카운터 대상은
///    스피너봇뿐이고, 간파 창은 연계 준비 단계에서만 열리므로 이 성공 = 연계 시작) → <b>파티 전원 우클릭 쿨 1회 초기화</b>
///    (<see cref="TutorialCooldownReset"/> — 시전자 쿨 감소(할 일 26)를 적용한 <b>뒤</b> 나는 이벤트라 그 위에 0 으로 덮는다) ·
///    첫 성공만 TUT_Z3_FIRST_SUCCESS · TUT_Z3_PUSH_INTRO. 간파 횟수는 세지 않는다.
///  · 연계 일(<see cref="TutorialSpinnerPractice.ServerComboEvent"/>): 벽 충돌 → Zone3Interrupts +1 · TUT_Z3_WALL_SUCCESS({n}/3) ·
///    밀치기 빗나감 → TUT_Z3_PUSH_HINT(5초 간격) · 복귀 시작 → TUT_Z3_RETURN · 다시 준비 → TUT_Z3_COUNTER_GOAL ·
///    완료(3회 + 마지막 복귀) → <b>G5</b>(<see cref="TutorialDirector.RequestGateServer"/>) → <see cref="TutorialSpinnerPractice.EnterRealMode"/>
///    (교육 제한 해제 + 2초 정지) · 단계 Zone3Fight · TUT_Z3_REAL.
///  · 준비·간파 단계 개인 피드백: 빗나감(적중 0) TUT_Z3_MISS · 노랑 아닐 때 스피너봇 적중 TUT_Z3_EARLY — 그 시전자에게만. 연속 놓침 4·8… → TUT_Z3_HINT(전원).
///  · 처치 = 존 장부의 존3 클리어(<see cref="TutorialDirector.ZoneCleared"/>) → TUT_ZONE_CLEAR. G6 은 장부가 이미 연다(여기서 열지 않는다).
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialZone3Flow : MonoBehaviour
{
    [Header("피드백")]
    [Tooltip("이른 간파(TUT_Z3_EARLY) 판정의 '스피너봇 곁' 반경(m, 시전자 ↔ 스피너봇 수평). 간파 판정 이벤트에 적중 대상이 없어 거리로 추정한다.")]
    [SerializeField, Min(0f)] private float earlyNearRadius = 4f;
    [Tooltip("연속 놓침이 이 수의 배수가 될 때마다 TUT_Z3_HINT (D2-2: 4).")]
    [SerializeField, Min(1)] private int hintEvery = TutorialZone3Model.DefaultHintEvery;

    [Header("연습 반경")]
    [Tooltip("스피너봇 몸 반경에 더 두는 여유(m). 반경 = 출구 배리어까지 거리 − 몸 반경 − 이 값.")]
    [SerializeField, Min(0f)] private float radiusMargin = TutorialZone3Geometry.DefaultMargin;

    [Header("연계 연습 위치 (v8 §3.5 · D26 튜닝)")]
    [Tooltip("유효 벽 면 ↔ 스피너봇 몸 가장자리(m) — 밀치기(3m) 1~2회로 닿는 거리.")]
    [SerializeField, Min(0.5f)] private float practiceWallGap = TutorialZone3Geometry.DefaultWallGap;
    [Tooltip("연습 위치를 존 끝(입구·출구)에서 이만큼(m) 안쪽으로.")]
    [SerializeField, Min(0f)] private float practiceEndClearance = TutorialZone3Geometry.DefaultEndClearance;

    private TutorialDirector _director;
    private TutorialZone3Model _model;
    private MonsterBase _spinner;
    private TutorialSpinnerPractice _practice;
    private MonsterCounterWindow _window;
    private bool _subscribed;

    // ServerInterruptSucceeded 는 같은 판정(ResolveHit) 호출 안에서 ServerInterruptResolved 바로 앞에 난다 — 그 사이에만 들고 있다(이른 간파 판정용).
    private Player _succeededCaster;

    private readonly List<MonsterBase> _buffer = new List<MonsterBase>();

    // ─── 부착(서버) ──────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InstallBootstrap()
    {
        TutorialDirector.StateChanged -= EnsureOnServer;
        TutorialDirector.StateChanged += EnsureOnServer;
    }

    private static void EnsureOnServer()
    {
        TutorialDirector d = TutorialDirector.Instance;
        if (d == null || !d.IsSpawned || !d.IsServer || !d.IsTutorialActive) return;
        if (d.GetComponent<TutorialZone3Flow>() != null) return;
        d.gameObject.AddComponent<TutorialZone3Flow>();
    }

    // ─── 생명주기 ────────────────────────────────────────────────────────

    private void OnEnable()
    {
        _director = GetComponent<TutorialDirector>();
        NetworkManager nm = NetworkManager.Singleton;
        if (_director == null || nm == null || !nm.IsServer)
        {
            enabled = false;
            return;
        }

        _model = new TutorialZone3Model(hintEvery);
        Subscribe();
        ResolveSpinner();

        // 붙기 전에 이미 지나간 신호(정상 흐름에서는 없음 — 활성 순간 붙는다)를 따라잡는다.
        bool start = false;
        if (_director.IsGateOpen(TutorialGate.G4_Zone3Entry)) start |= _model.NotifyEntryGateOpen();
        if (_director.HasPartyEnteredServer(TutorialArea.Zone3)) start |= _model.NotifyPartyEntered();
        if (start) StartPractice();
    }

    private void OnDisable() => Unsubscribe();

    private void Subscribe()
    {
        if (_subscribed) return;
        TutorialDirector.AreaFirstEntered += HandleAreaFirstEntered;
        TutorialDirector.GateOpened += HandleGateOpened;
        TutorialDirector.ZoneCleared += HandleZoneCleared;
        PlayerInterruptSkillBase.ServerInterruptSucceeded += HandleInterruptSucceeded;
        PlayerInterruptSkillBase.ServerInterruptResolved += HandleInterruptResolved;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        TutorialDirector.AreaFirstEntered -= HandleAreaFirstEntered;
        TutorialDirector.GateOpened -= HandleGateOpened;
        TutorialDirector.ZoneCleared -= HandleZoneCleared;
        PlayerInterruptSkillBase.ServerInterruptSucceeded -= HandleInterruptSucceeded;
        PlayerInterruptSkillBase.ServerInterruptResolved -= HandleInterruptResolved;
        if (_practice != null)
        {
            _practice.ServerWindowMissed -= HandleWindowMissed;
            _practice.ServerComboEvent -= HandleComboEvent;
        }
        _succeededCaster = null;
        _subscribed = false;
    }

    // ─── 스피너봇 · 연습 반경 ─────────────────────────────────────────────

    private void ResolveSpinner()
    {
        _director.GetZoneMonstersServer(TutorialArea.Zone3, TutorialMonsterFilter.KeyOnly, _buffer);
        _spinner = _buffer.Count > 0 ? _buffer[0] : null;
        _buffer.Clear();
        if (_spinner == null)
        {
            Edit.LogError("[Tutorial] 존3: 장부에 핵심 몬스터(스피너봇)가 없다 — 존3 흐름이 돌지 않는다.", this);
            return;
        }

        _window = _spinner.GetComponent<MonsterCounterWindow>();
        _practice = _spinner.GetComponent<TutorialSpinnerPractice>();
        if (_practice == null)
        {
            Edit.LogError($"[Tutorial] 존3: '{_spinner.name}' 에 TutorialSpinnerPractice 가 없다 — 튜토리얼 Variant(SpinnerBot_Tutorial)로 " +
                          "치환되지 않았다(DefaultNetworkPrefabs 등록·TutorialSpinnerSpawnOverride 확인). 연습 모드 없이 원래 스피너봇으로 돈다.", this);
            return;
        }

        _practice.ServerWindowMissed += HandleWindowMissed;
        _practice.ServerComboEvent += HandleComboEvent;
        ApplyPracticeLayout();
    }

    // 연습용 유효 벽·연습 위치(원위치) → 연습 반경(출구 배리어 기준). 스피너봇은 이때 휴면이라 옮겨도 보는 사람이 없다(존3 진입 전).
    private void ApplyPracticeLayout()
    {
        TutorialStageMarker stage = TutorialStageMarker.Active;
        var areas = new TutorialAreaTracker();
        if (!TutorialAreaLayout.TryBuild(stage != null ? stage.transform : null, areas, out string error))
        {
            Edit.LogWarning($"[Tutorial] 존3: 구간 계산 실패({error}) — 연습 위치·반경 {_practice.PracticeRadius:0.##}m 그대로. " +
                            "유효 벽이 없어 연계(벽 충돌)가 성립하지 않는다.", this);
            return;
        }

        float body = BodyRadius(_spinner);
        if (areas.TryGetArea(TutorialArea.Zone3, out TutorialRect zone))
        {
            Vector3 spawn = _practice.PracticeAnchor;
            TutorialZone3PracticeLayout layout = TutorialZone3Geometry.ChoosePracticeLayout(
                zone, spawn.x, spawn.z, body, practiceWallGap, practiceEndClearance);
            _practice.SetPracticeWall(new Vector3(layout.WallX, spawn.y, layout.WallZ), new Vector3(layout.NormalX, 0f, layout.NormalZ), body);
            bool moved = _practice.RelocateAnchor(new Vector3(layout.AnchorX, spawn.y, layout.AnchorZ));
            Edit.Log($"[Tutorial] 존3 연계 연습 — 유효 벽 ({layout.WallX:0.##}, {layout.WallZ:0.##}) 안쪽 법선 ({layout.NormalX:0.##}, {layout.NormalZ:0.##}) · " +
                     $"연습 위치 ({_practice.PracticeAnchor.x:0.##}, {_practice.PracticeAnchor.z:0.##}){(moved ? "" : " — NavMesh 못 찾아 스폰 위치")} · " +
                     $"벽 간격 {practiceWallGap:0.#}m.", this);
        }
        else
        {
            Edit.LogWarning("[Tutorial] 존3: 존3 사각형이 없다 — 유효 벽이 없어 연계(벽 충돌)가 성립하지 않는다.", this);
        }

        int target = _director.GetCounterTarget(TutorialCounter.Zone3Interrupts);
        if (target != _practice.ComboTarget)
            Edit.LogWarning($"[Tutorial] 존3: 디렉터 연계 목표 {target} ≠ 스피너봇 연계 목표 {_practice.ComboTarget} — 두 값을 맞출 것(완료는 스피너봇 값으로 판정).", this);

        List<TutorialBarrierSpec> specs = TutorialBarrierRules.BuildSpecs(
            areas, TutorialBarrierRules.DefaultThickness, TutorialBarrierRules.DefaultSideOverlap, out _);
        int i = specs.FindIndex(s => s.Gate == TutorialGate.G6_Corridor3Entry);
        if (i < 0)
        {
            Edit.LogWarning($"[Tutorial] 존3: 출구 배리어(G6) 모양이 없다 — 연습 반경 {_practice.PracticeRadius:0.##}m 그대로.", this);
            return;
        }

        Vector3 anchor = _practice.PracticeAnchor;
        float max = _practice.PracticeRadius;
        float r = TutorialZone3Geometry.ComputePracticeRadius(anchor.x, anchor.z, specs[i].Rect, body, radiusMargin, max);
        _practice.SetPracticeRadius(r);
        float d = TutorialZone3Geometry.DistanceToRect(anchor.x, anchor.z, specs[i].Rect);
        Edit.Log($"[Tutorial] 존3 연습 반경 {r:0.##}m — 기준점 ({anchor.x:0.##}, {anchor.z:0.##}) → G6 배리어 {d:0.##}m − 몸 {body:0.##} − 여유 {radiusMargin:0.##} (상한 {max:0.##}).", this);
    }

    private static float BodyRadius(MonsterBase m)
    {
        float r = 0f;
        if (m.TryGetComponent(out NavMeshAgent agent)) r = agent.radius * Mathf.Max(m.transform.lossyScale.x, m.transform.lossyScale.z);
        if (m.TryGetComponent(out CapsuleCollider capsule))
            r = Mathf.Max(r, capsule.radius * Mathf.Max(m.transform.lossyScale.x, m.transform.lossyScale.z));
        return r > 0f ? r : 0.5f;
    }

    // ─── 신호 ────────────────────────────────────────────────────────────

    private void HandleAreaFirstEntered(TutorialArea area, ulong clientId)
    {
        if (area == TutorialArea.Zone3 && _model.NotifyPartyEntered()) StartPractice();
    }

    private void HandleGateOpened(TutorialGate gate, TutorialGateOpenReason reason)
    {
        if (gate == TutorialGate.G4_Zone3Entry)
        {
            if (_model.NotifyEntryGateOpen()) StartPractice();
        }
        else if (gate == TutorialGate.G5_SpinnerRealMode)
        {
            if (_model.NotifyRealModeGate(out bool skipped)) StartRealMode(skipped);
        }
    }

    private void HandleZoneCleared(TutorialArea zone)
    {
        if (zone != TutorialArea.Zone3 || !_model.NotifyCleared()) return;
        // G6 은 디렉터(존 장부 → RequestGateServer)가 연다 — 여기서는 안내만.
        _director.ShowSubtitleServer(TutorialGuideIds.ZoneClear);
    }

    // v8 Z3-4 — 간파 성공(준비 단계)마다 파티 전원 우클릭 쿨 1회 초기화. 간파 횟수는 세지 않는다(연계 = 벽 충돌).
    // 26 이 시전자 우클릭 쿨을 줄인 뒤 나는 이벤트라 여기서 0 으로 덮는다. 연습 모드 없는 스피너봇(Variant 미치환)은 연계가 없어 초기화하지 않는다.
    private void HandleInterruptSucceeded(Player caster, PlayerInterruptSkillBase skill)
    {
        if (caster == null) return;
        _succeededCaster = caster;

        ulong attackerClientId = caster.OwnerClientId;
        if (_practice == null || _director.GetPlayerAreaServer(attackerClientId) != TutorialArea.Zone3) return;
        if (!_model.NotifyCounterSucceeded(out bool first)) return;

        int players = 0, skills = 0;
        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            Player p = client.PlayerObject != null ? client.PlayerObject.GetComponent<Player>() : null;
            if (p == null) continue;
            players++;
            skills += TutorialCooldownReset.ResetSkillsServer(p, TutorialCooldownResetScope.Zone3CounterIncludes);
        }
        Edit.Log($"[Tutorial] 존3 간파 성공(client {attackerClientId}) → 파티 우클릭 쿨 초기화 — {players}명 · 스킬 {skills}개.", this);

        if (first) _director.ShowSubtitleServer(TutorialGuideIds.Z3FirstSuccess);
        _director.ShowSubtitleServer(TutorialGuideIds.Z3PushIntro);
    }

    private void HandleComboEvent(TutorialSpinnerPractice practice, SpinnerComboEvent events, int successes)
    {
        if (_model.Phase != TutorialZone3Phase.Practice) return;

        if ((events & SpinnerComboEvent.WallHit) != 0)
        {
            _director.AddCounterServer(TutorialCounter.Zone3Interrupts);
            int total = _director.GetCounterTarget(TutorialCounter.Zone3Interrupts);
            _director.ShowSubtitleServer(TutorialGuideIds.Z3WallSuccess,
                new[] { "n", successes.ToString(), "total", total.ToString() });
        }
        if ((events & SpinnerComboEvent.PushEndedNoWall) != 0 && _model.TryPushHint(Time.unscaledTimeAsDouble))
            _director.ShowSubtitleServer(TutorialGuideIds.Z3PushHint);
        if ((events & SpinnerComboEvent.ReturnStarted) != 0 && successes < practice.ComboTarget)
            _director.ShowSubtitleServer(TutorialGuideIds.Z3Return);
        if ((events & SpinnerComboEvent.ReadyAgain) != 0)
            _director.ShowSubtitleServer(TutorialGuideIds.Z3CounterGoal);
        if ((events & SpinnerComboEvent.Completed) != 0)
        {
            Edit.Log($"[Tutorial] 존3 연계 {successes}회 + 마지막 원위치 복귀 완료 → G5.", this);
            _director.RequestGateServer(TutorialGate.G5_SpinnerRealMode);   // → HandleGateOpened → 본 전투
        }
    }

    private void HandleInterruptResolved(Player caster, int hitCount)
    {
        bool succeeded = caster != null && _succeededCaster == caster;
        _succeededCaster = null;
        if (caster == null || _spinner == null || _model.Phase != TutorialZone3Phase.Practice) return;
        bool comboReady = _practice == null || _practice.ComboPhase == SpinnerComboPhase.Ready;

        ulong id = caster.OwnerClientId;
        bool inZone3 = _director.GetPlayerAreaServer(id) == TutorialArea.Zone3;
        bool windowOpen = _window != null && _window.IsOpen;
        Vector3 d = caster.transform.position - _spinner.transform.position;
        d.y = 0f;
        bool near = d.sqrMagnitude <= earlyNearRadius * earlyNearRadius;

        switch (_model.ClassifyInterrupt(hitCount, inZone3, succeeded, windowOpen, near, comboReady))
        {
            case TutorialZone3Feedback.Miss:
                _director.ShowSubtitleServer(TutorialGuideIds.Z3Miss, targets: new[] { id });
                break;
            case TutorialZone3Feedback.Early:
                _director.ShowSubtitleServer(TutorialGuideIds.Z3Early, targets: new[] { id });
                break;
        }
    }

    private void HandleWindowMissed(TutorialSpinnerPractice practice, int consecutiveMisses)
    {
        if (_model.ShouldShowHint(consecutiveMisses)) _director.ShowSubtitleServer(TutorialGuideIds.Z3Hint);
    }

    // ─── 실행 ────────────────────────────────────────────────────────────

    private void StartPractice()
    {
        Edit.Log("[Tutorial] 존3 연습 모드 시작 — 스피너봇 기상.", this);
        if (_spinner != null) _director.SetMonsterDormantServer(_spinner, false);
        if (_practice != null) _practice.EnterPractice();
        _director.SetStepServer(TutorialStep.Zone3Practice);

        // 공통 안내만 — 캐릭터별 간파 동작·사거리·후폭풍 설명은 하지 않는다(v8 §2.4·§3.5).
        _director.ShowSubtitleServer(TutorialGuideIds.Z3Intro);
        _director.ShowSubtitleServer(TutorialGuideIds.Z3PracticeNote);
    }

    private void StartRealMode(bool skippedPractice)
    {
        Edit.Log($"[Tutorial] 존3 G5 → 스피너봇 본 모드{(skippedPractice ? " (연습 없이 — 디버그 개방)" : "")}.", this);
        if (skippedPractice && _spinner != null) _director.SetMonsterDormantServer(_spinner, false);
        if (_practice != null) _practice.EnterRealMode();
        _director.SetStepServer(TutorialStep.Zone3Fight);
        _director.ShowSubtitleServer(TutorialGuideIds.Z3Real);
    }
}
