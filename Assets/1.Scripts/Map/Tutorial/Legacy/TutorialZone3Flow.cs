using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 튜토리얼 존3 흐름(S9, §3.5 Z3-1~Z3-8) — <b>서버 전용</b>. 규칙은 순수 <see cref="TutorialZone3Model"/>·<see cref="TutorialZone3Geometry"/>,
/// 이 컴포넌트는 디렉터·스피너봇·간파 신호를 모델에 넣고 결과(기상·모드 전환·카운터·단계·자막)를 실행한다.
/// 표시(목표 패널·강조)는 모든 피어의 <see cref="TutorialZone3Guide"/> 가 복제 상태로 그린다. 상세: Docs/tech/tutorial-zone3.md.
///
/// 붙는 곳: 서버에서 디렉터가 튜토리얼을 켠 순간(<see cref="MapSceneDirector.StateChanged"/> ∧ <see cref="MapSceneDirector.IsTutorialActive"/>)
/// 디렉터 GameObject 에 런타임으로 붙는다(<see cref="MapSceneDeathRules"/> 와 같은 방식 — 씬·프리팹 저장 없음).
///
/// 흐름(v8 §3.5 — 간파 → 취약 밀치기 → 벽 충돌 → 회복 → 그 자리에서 다음 준비(원위치 복귀 없음 — 사용자 변경), 연계 파티 합계 3회 → 본 전투):
///  · 활성 순간: 연습 위치(<see cref="TutorialZone3Geometry.ChoosePracticeLayout"/> — 존3 긴 변 기준)를 정해 스피너봇을 옮기고
///    (휴면 중), 안내 화살표 = 그 위치에서 가장 가까운 Wall 레이어 콜라이더 면(<see cref="TutorialZone3WallFinder"/>, 0개면 경고),
///    연습 반경 = 그 위치 → 출구 배리어(G6) 거리 − 몸 반경 − 여유. 벽 충돌 판정 = 공용 <see cref="UnitWallImpacts"/>(할 일 32).
///  · G4 열림 ∧ 존3 파티 첫 진입 → 스피너봇 휴면 해제 → <see cref="TutorialSpinnerPractice.EnterPractice"/> · 단계 Zone3Practice ·
///    자막 TUT_Z3_INTRO → TUT_Z3_PRACTICE_NOTE(공통 — 캐릭터별 간파 설명 없음, §2.4).
///  · 간파 성공(준비 단계) = <see cref="PlayerInterruptSkillBase.ServerInterruptSucceeded"/>(연습 중 · 시전자 존3 안 — 존3 의 카운터 대상은
///    스피너봇뿐이고, 간파 창은 연계 준비 단계에서만 열리므로 이 성공 = 연계 시작) → 첫 성공만 TUT_Z3_FIRST_SUCCESS · TUT_Z3_PUSH_INTRO.
///    간파 횟수는 세지 않고, 쿨타임도 돌려주지 않는다(할 일 90).
///  · 연계 일(<see cref="TutorialSpinnerPractice.ServerComboEvent"/>): 벽 충돌 → Zone3Interrupts +1 · TUT_Z3_WALL_SUCCESS({n}/3) ·
///    밀치기 빗나감 → TUT_Z3_PUSH_HINT(5초 간격) · 회복 끝(다시 준비) → TUT_Z3_COUNTER_GOAL ·
///    완료(3회째 회복 끝) → <b>G5</b>(<see cref="MapSceneDirector.RequestGateServer"/>) → <see cref="TutorialSpinnerPractice.EnterRealMode"/>
///    (교육 제한 해제 + 2초 정지) · 단계 Zone3Fight · TUT_Z3_REAL.
///  · 준비·간파 단계 개인 피드백: 빗나감(적중 0) TUT_Z3_MISS · 노랑 아닐 때 스피너봇 적중 TUT_Z3_EARLY — 그 시전자에게만. 연속 놓침 4·8… → TUT_Z3_HINT(전원).
///  · 처치 = 존 장부의 존3 클리어(<see cref="MapSceneDirector.ZoneCleared"/>) → TUT_ZONE_CLEAR. G6 은 장부가 이미 연다(여기서 열지 않는다).
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

    private MapSceneDirector _director;
    private TutorialZone3Model _model;
    private MonsterBase _spinner;
    private TutorialSpinnerPractice _practice;
    private MonsterCounterWindow _window;
    private bool _subscribed;

    // ServerInterruptSucceeded 는 같은 판정(ResolveHit) 호출 안에서 ServerInterruptResolved 바로 앞에 난다 — 그 사이에만 들고 있다(이른 간파 판정용).
    private Player _succeededCaster;

    private readonly List<MonsterBase> _buffer = new List<MonsterBase>();

    // ─── 부착(서버) ──────────────────────────────────────────────────────

    // 107 Legacy — 맵씬에서 뺐다: 런타임 부착(RuntimeInitializeOnLoadMethod)을 끊었다. 존3 진행은 MapSceneZoneFlow. 보관용.
    private static void InstallBootstrap()
    {
        MapSceneDirector.StateChanged -= EnsureOnServer;
        MapSceneDirector.StateChanged += EnsureOnServer;
    }

    private static void EnsureOnServer()
    {
        MapSceneDirector d = MapSceneDirector.Instance;
        if (d == null || !d.IsSpawned || !d.IsServer || !d.IsTutorialActive) return;
        if (d.GetComponent<TutorialZone3Flow>() != null) return;
        d.gameObject.AddComponent<TutorialZone3Flow>();
    }

    // ─── 생명주기 ────────────────────────────────────────────────────────

    private void OnEnable()
    {
        _director = GetComponent<MapSceneDirector>();
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
        if (_director.IsGateOpen(MapSceneGate.G4_Zone3Entry)) start |= _model.NotifyEntryGateOpen();
        if (_director.HasPartyEnteredServer(MapSceneArea.Zone3)) start |= _model.NotifyPartyEntered();
        if (start) StartPractice();
    }

    private void OnDisable() => Unsubscribe();

    private void Subscribe()
    {
        if (_subscribed) return;
        MapSceneDirector.AreaFirstEntered += HandleAreaFirstEntered;
        MapSceneDirector.GateOpened += HandleGateOpened;
        MapSceneDirector.ZoneCleared += HandleZoneCleared;
        PlayerInterruptSkillBase.ServerInterruptSucceeded += HandleInterruptSucceeded;
        PlayerInterruptSkillBase.ServerInterruptResolved += HandleInterruptResolved;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        MapSceneDirector.AreaFirstEntered -= HandleAreaFirstEntered;
        MapSceneDirector.GateOpened -= HandleGateOpened;
        MapSceneDirector.ZoneCleared -= HandleZoneCleared;
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
        _director.GetZoneMonstersServer(MapSceneArea.Zone3, MapSceneMonsterFilter.KeyOnly, _buffer);
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
        MapSceneStageMarker stage = MapSceneStageMarker.Active;
        var areas = new MapSceneAreaTracker();
        if (!MapSceneAreaLayout.TryBuild(stage != null ? stage.transform : null, areas, out string error))
        {
            Edit.LogWarning($"[Tutorial] 존3: 구간 계산 실패({error}) — 연습 위치·반경 {_practice.PracticeRadius:0.##}m 그대로 · 벽 안내 화살표 없음.", this);
            return;
        }

        float body = BodyRadius(_spinner);
        if (areas.TryGetArea(MapSceneArea.Zone3, out MapSceneRect zone))
        {
            Vector3 spawn = _practice.PracticeAnchor;
            TutorialZone3PracticeLayout layout = TutorialZone3Geometry.ChoosePracticeLayout(
                zone, spawn.x, spawn.z, body, practiceWallGap, practiceEndClearance);
            bool moved = _practice.RelocateAnchor(new Vector3(layout.AnchorX, spawn.y, layout.AnchorZ));
            Vector3 anchorPos = _practice.PracticeAnchor;

            // 할 일 32 — 안내 화살표 = 연습 위치에서 가장 가까운 Wall 레이어 콜라이더 면. 연계 판정도 Wall 콜라이더(UnitWallImpacts)라
            // 0개면 연계가 성립하지 않는다 → 경고 + 화살표는 존 긴 변(배치 기준선)으로 대신 가리킨다.
            int wallCount = TutorialZone3WallFinder.FindNearest(zone, anchorPos, out Vector3 wallPoint, out Collider wall);
            if (wallCount == 0)
            {
                wallPoint = new Vector3(layout.WallX, spawn.y, layout.WallZ);
                Edit.LogWarning("[Tutorial] 존3 Wall 레이어 콜라이더 0개 — 존3 벽에 Wall 태그+레이어 세팅 필요(Docs/tech/wall-concept.md). " +
                                "그 전엔 밀치기 연계가 성공하지 않는다. 안내 화살표는 존 긴 변을 가리킨다.", this);
            }
            _practice.SetPracticeWall(wallPoint);
            Edit.Log($"[Tutorial] 존3 연계 연습 — 안내 벽 ({wallPoint.x:0.##}, {wallPoint.z:0.##})" +
                     $"{(wall != null ? $" '{wall.name}' (Wall 콜라이더 {wallCount}개)" : " (존 긴 변 — Wall 콜라이더 없음)")} · " +
                     $"연습 위치 ({anchorPos.x:0.##}, {anchorPos.z:0.##}){(moved ? "" : " — NavMesh 못 찾아 스폰 위치")} · " +
                     $"긴 변 간격 {practiceWallGap:0.#}m.", this);
        }
        else
        {
            Edit.LogWarning("[Tutorial] 존3: 존3 사각형이 없다 — 연습 위치 그대로 · 벽 안내 화살표 없음.", this);
        }

        int target = _director.GetCounterTarget(MapSceneCounter.Zone3Interrupts);
        if (target != _practice.ComboTarget)
            Edit.LogWarning($"[Tutorial] 존3: 디렉터 연계 목표 {target} ≠ 스피너봇 연계 목표 {_practice.ComboTarget} — 두 값을 맞출 것(완료는 스피너봇 값으로 판정).", this);

        List<MapSceneBarrierSpec> specs = MapSceneBarrierRules.BuildSpecs(
            areas, MapSceneBarrierRules.DefaultThickness, MapSceneBarrierRules.DefaultSideOverlap, out _);
        int i = specs.FindIndex(s => s.Gate == MapSceneGate.G6_Corridor3Entry);
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

    private void HandleAreaFirstEntered(MapSceneArea area, ulong clientId)
    {
        if (area == MapSceneArea.Zone3 && _model.NotifyPartyEntered()) StartPractice();
    }

    private void HandleGateOpened(MapSceneGate gate, MapSceneGateOpenReason reason)
    {
        if (gate == MapSceneGate.G4_Zone3Entry)
        {
            if (_model.NotifyEntryGateOpen()) StartPractice();
        }
        else if (gate == MapSceneGate.G5_SpinnerRealMode)
        {
            if (_model.NotifyRealModeGate(out bool skipped)) StartRealMode(skipped);
        }
    }

    private void HandleZoneCleared(MapSceneArea zone)
    {
        if (zone != MapSceneArea.Zone3 || !_model.NotifyCleared()) return;
        // G6 은 디렉터(존 장부 → RequestGateServer)가 연다 — 여기서는 안내만.
        _director.ShowSubtitleServer(TutorialGuideIds.ZoneClear);
    }

    // 간파 성공(준비 단계) — 첫 성공 자막 + 밀치기 안내. 간파 횟수는 세지 않는다(연계 = 벽 충돌). 쿨타임은 돌려주지 않는다(할 일 90).
    // 연습 모드 없는 스피너봇(Variant 미치환)은 연계가 없어 안내하지 않는다.
    private void HandleInterruptSucceeded(Player caster, PlayerInterruptSkillBase skill)
    {
        if (caster == null) return;
        _succeededCaster = caster;

        if (_practice == null || _director.GetPlayerAreaServer(caster.OwnerClientId) != MapSceneArea.Zone3) return;
        if (!_model.NotifyCounterSucceeded(out bool first)) return;

        if (first) _director.ShowSubtitleServer(TutorialGuideIds.Z3FirstSuccess);
        _director.ShowSubtitleServer(TutorialGuideIds.Z3PushIntro);
    }

    private void HandleComboEvent(TutorialSpinnerPractice practice, SpinnerComboEvent events, int successes)
    {
        if (_model.Phase != TutorialZone3Phase.Practice) return;

        if ((events & SpinnerComboEvent.WallHit) != 0)
        {
            _director.AddCounterServer(MapSceneCounter.Zone3Interrupts);
            int total = _director.GetCounterTarget(MapSceneCounter.Zone3Interrupts);
            _director.ShowSubtitleServer(TutorialGuideIds.Z3WallSuccess,
                new[] { "n", successes.ToString(), "total", total.ToString() });
        }
        if ((events & SpinnerComboEvent.PushEndedNoWall) != 0 && _model.TryPushHint(Time.unscaledTimeAsDouble))
            _director.ShowSubtitleServer(TutorialGuideIds.Z3PushHint);
        if ((events & SpinnerComboEvent.ReadyAgain) != 0)
            _director.ShowSubtitleServer(TutorialGuideIds.Z3CounterGoal);
        if ((events & SpinnerComboEvent.Completed) != 0)
        {
            Edit.Log($"[Tutorial] 존3 연계 {successes}회 + 마지막 회복 끝 → G5.", this);
            _director.RequestGateServer(MapSceneGate.G5_SpinnerRealMode);   // → HandleGateOpened → 본 전투
        }
    }

    private void HandleInterruptResolved(Player caster, int hitCount)
    {
        bool succeeded = caster != null && _succeededCaster == caster;
        _succeededCaster = null;
        if (caster == null || _spinner == null || _model.Phase != TutorialZone3Phase.Practice) return;
        bool comboReady = _practice == null || _practice.ComboPhase == SpinnerComboPhase.Ready;

        ulong id = caster.OwnerClientId;
        bool inZone3 = _director.GetPlayerAreaServer(id) == MapSceneArea.Zone3;
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
        _director.SetStepServer(MapSceneStep.Zone3Practice);

        // 공통 안내만 — 캐릭터별 간파 동작·사거리·후폭풍 설명은 하지 않는다(v8 §2.4·§3.5).
        _director.ShowSubtitleServer(TutorialGuideIds.Z3Intro);
        _director.ShowSubtitleServer(TutorialGuideIds.Z3PracticeNote);
    }

    private void StartRealMode(bool skippedPractice)
    {
        Edit.Log($"[Tutorial] 존3 G5 → 스피너봇 본 모드{(skippedPractice ? " (연습 없이 — 디버그 개방)" : "")}.", this);
        if (skippedPractice && _spinner != null) _director.SetMonsterDormantServer(_spinner, false);
        if (_practice != null) _practice.EnterRealMode();
        _director.SetStepServer(MapSceneStep.Zone3Fight);
        _director.ShowSubtitleServer(TutorialGuideIds.Z3Real);
    }
}

/// <summary>
/// 존3 안내 화살표용 — 존3 사각형(+여유) 안의 Wall 레이어 비트리거 콜라이더 중 기준점에서 수평으로 가장 가까운 면 위 점(할 일 32).
/// 반환 = 찾은 Wall 콜라이더 수(0 = 세팅 필요). 비볼록 MeshCollider 는 경계 상자로 잰다.
/// </summary>
internal static class TutorialZone3WallFinder
{
    const float Margin = 3f;       // 존 사각형 밖으로 이만큼(벽은 존 가장자리에 걸쳐 있다)
    const float Below = 1f;
    const float Above = 4f;
    static readonly Collider[] Buffer = new Collider[256];

    public static int FindNearest(MapSceneRect zone, Vector3 from, out Vector3 point, out Collider nearest)
    {
        point = from;
        nearest = null;
        int mask = GameLayers.WallMask;
        if (mask == 0) return 0;

        zone.GetWorldBounds(out float minX, out float minZ, out float maxX, out float maxZ);
        var center = new Vector3((minX + maxX) * 0.5f, from.y + (Above - Below) * 0.5f, (minZ + maxZ) * 0.5f);
        var half = new Vector3((maxX - minX) * 0.5f + Margin, (Above + Below) * 0.5f, (maxZ - minZ) * 0.5f + Margin);
        int count = Physics.OverlapBoxNonAlloc(center, half, Buffer, Quaternion.identity, mask, QueryTriggerInteraction.Ignore);

        Vector3 probe = from + Vector3.up;
        float best = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider c = Buffer[i];
            Vector3 p = c is MeshCollider mesh && !mesh.convex ? c.ClosestPointOnBounds(probe) : c.ClosestPoint(probe);
            float d = new Vector2(p.x - from.x, p.z - from.z).sqrMagnitude;
            if (d >= best) continue;
            best = d;
            nearest = c;
            point = new Vector3(p.x, from.y, p.z);
        }
        return count;
    }
}
