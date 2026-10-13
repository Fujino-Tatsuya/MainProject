// ----------------------------------------------------------------------------
//  TutorialSkipController.cs — 튜토리얼 구간 스킵 Alt+1~0 (할 일 34, QA·테스트용)
//
//  Alt+1 복도1 · Alt+2 존2 · Alt+3 복도2 · Alt+4 존3 · Alt+5 복도3 · Alt+6 존4 · Alt+7 복도4 · Alt+8 보스 진입 · Alt+9 보스방 · Alt+0 처음부터.
//  목적 구간 앞의 모든 단계를 정식 규칙대로 끝낸 것처럼 만든 뒤(게이트 순서·개인 체크·특성 자동 획득·존 장부 클리어·G5·G8)
//  파티 전원을 목적 구간 입구 안쪽 안전 지점으로 옮긴다. 계획은 순수 TutorialSkipPlan, 원본 Docs/tech/tutorial-director.md §10.5.
//  Alt+0 = 판 재시작 — 맵 씬을 로딩 씬을 거쳐 다시 불러온다(NetworkLoadingFlowController.TryRestartTargetScene).
//  이 컴포넌트는 디렉터와 함께 맵 씬에 있어 재로드로 사라진다 — 재시작 자체는 NetworkManager 쪽 흐름이 끝까지 진행한다.
//
//  🔴 호스트만 — 요청은 TutorialDirector.RequestSkip → ServerRpc 가 보낸 사람이 호스트인지 검증한다.
//  🔴 에디터·Development Build 에만 있다(DevBossEntranceWarp 와 같은 부류). 릴리스 빌드엔 클래스가 없어 입력이 무시된다.
//  🔴 로그는 Debug.Log — Edit.* 는 에디터 전용이라 빌드에서 사라진다(빌드로 검증하는 도구가 말을 못 하면 안 된다).
//
//  화면: Alt 를 누르고 있는 동안만 호스트 화면 오른쪽 위에 구간 버튼 10개 — 단축키와 같은 동작.
//  (토글이 아니라 누르는 동안 표시: 단축키가 이미 Alt 조합이라 Alt 를 누르는 순간 = 쓸 때만 보이고, 켜 둔 채 잊어 화면·캡처에 남지 않는다.)
// ----------------------------------------------------------------------------
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public sealed class TutorialSkipController : MonoBehaviour
{
    const string Tag = "[TutorialSkip]";
    const float ToastSeconds = 3f;

    TutorialDirector _director;
    NetworkManager _nm;

    // 서버
    bool _running;
    readonly List<TutorialSkipAction> _plan = new List<TutorialSkipAction>();
    readonly List<int> _corridors = new List<int>();
    readonly List<Vector3> _points = new List<Vector3>();
    readonly List<NetworkObject> _movers = new List<NetworkObject>();

    // 로컬
    string _toast;
    float _toastUntil;
    GUIStyle _toastStyle;

    // ─── 부착(모든 피어 — 입력·토스트는 각자, 실행은 서버) ───────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void InstallBootstrap()
    {
        TutorialDirector.StateChanged -= Ensure;
        TutorialDirector.StateChanged += Ensure;
    }

    static void Ensure()
    {
        TutorialDirector d = TutorialDirector.Instance;
        if (d == null || !d.IsSpawned || !d.IsTutorialActive) return;
        if (d.GetComponent<TutorialSkipController>() != null) return;
        d.gameObject.AddComponent<TutorialSkipController>();
    }

    void OnEnable()
    {
        _director = GetComponent<TutorialDirector>();
        _nm = NetworkManager.Singleton;
        if (_director == null || _nm == null || !_nm.IsListening) enabled = false;
    }

    void OnDisable()
    {
        StopAllCoroutines();
        _running = false;
    }

    bool IsHost => _nm != null && _nm.IsServer && _nm.IsClient;

    // ─── 입력 ────────────────────────────────────────────────────────────

    void Update()
    {
        if (_director == null || !_director.IsTutorialActive) return;
        if (!IsAltHeld() || !TryReadDigit(out int digit)) return;
        if (TutorialSkipPlan.TryFromDigit(digit, out TutorialSkipTarget target)) Request(target);
    }

    static bool IsAltHeld()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        return kb != null && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed);
#else
        return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
#endif
    }

    static bool TryReadDigit(out int digit)
    {
        for (digit = 0; digit <= 9; digit++)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null && kb[DigitKey(digit)].wasPressedThisFrame) return true;
#else
            if (Input.GetKeyDown(KeyCode.Alpha0 + digit)) return true;
#endif
        }
        digit = -1;
        return false;
    }

#if ENABLE_INPUT_SYSTEM
    // Key.Digit1..Digit9 가 연속이고 Digit0 은 그 뒤에 있다.
    static Key DigitKey(int digit) => digit == 0 ? Key.Digit0 : Key.Digit1 + (digit - 1);
#endif

    void Request(TutorialSkipTarget target)
    {
        if (!IsHost)
        {
            Toast($"Alt+{TutorialSkipPlan.DigitOf(target)}: 구간 스킵은 호스트만");
            Debug.LogWarning($"{Tag} 구간 스킵은 호스트만 — 이 피어(client {_nm.LocalClientId})는 보내지 않는다.", this);
            return;
        }
        _director.RequestSkip(target);
    }

    // ─── 실행(서버) ──────────────────────────────────────────────────────

    /// <summary>[서버] 스킵 실행 — <see cref="TutorialDirector"/> 의 호스트 전용 ServerRpc 가 부른다.</summary>
    public void RunServer(TutorialSkipTarget target)
    {
        if (_nm == null || !_nm.IsServer) return;
        string name = TutorialSkipPlan.NameOf(target);
        bool restart = TutorialSkipPlan.IsRestart(target);
        if (_running && !restart)
        {
            Warn($"→ {name} 거부 — 이전 스킵이 아직 진행 중이다.");
            return;
        }

        switch (TutorialSkipPlan.Check(_director.Step, target, IsRestartInProgress))
        {
            case TutorialSkipVerdict.RestartInProgress:
                Warn($"→ {name} 거부 — 판 재시작(맵 재로드)이 진행 중이다.");
                return;
            case TutorialSkipVerdict.Backward:
                Warn($"→ {name} 거부 — 지금 구간({TutorialSkipPlan.NameOf(TutorialSkipPlan.CurrentOf(_director.Step))})보다 뒤로는 못 간다.");
                return;
            case TutorialSkipVerdict.Finished:
                Warn($"→ {name} 거부 — 이미 보스방에 도착했다(튜토리얼 안내 끝).");
                return;
        }

        if (restart) RestartServer();
        else StartCoroutine(Run(target));
    }

    NetworkLoadingFlowController LoadingFlow => _nm != null ? _nm.GetComponent<NetworkLoadingFlowController>() : null;

    // 재시작 요청부터 새 맵 로드가 끝날 때까지 — 로딩 흐름 원본을 읽는다(이 컴포넌트는 그 사이 맵 씬과 함께 사라진다).
    bool IsRestartInProgress
    {
        get
        {
            NetworkLoadingFlowController flow = LoadingFlow;
            return flow != null && flow.IsFlowInProgress;
        }
    }

    // [서버] 판 재시작 — 진행 중인 구간 스킵은 끊는다(어차피 맵과 함께 사라진다). 확인 대화상자 없이 바로.
    void RestartServer()
    {
        NetworkLoadingFlowController flow = LoadingFlow;
        if (flow == null)
        {
            Fail("→ 처음부터 거부 — NetworkLoadingFlowController 가 없다(NetworkManager.prefab 인스턴스인지 확인).");
            return;
        }

        StopAllCoroutines();
        _running = false;
        if (!flow.TryRestartTargetScene(out string reason))
        {
            Fail($"→ 처음부터 실패 — {reason}. 판은 그대로 이어진다.");
            return;
        }

        Debug.Log($"{Tag} → 처음부터 — 맵 씬 재로드 시작(단계 {_director.Step} 에서). 로딩 씬 → 맵 언로드 → 재로드 → 전원 첫 스폰.", this);
        Toast("구간 스킵 → 처음부터 (맵 다시 불러오는 중…)");
    }

    IEnumerator Run(TutorialSkipTarget target)
    {
        _running = true;
        string name = TutorialSkipPlan.NameOf(target);
        int before = _director.OpenedGateMaskServer;
        TutorialSkipPlan.Build(target, before, _plan);
        TutorialSkipPlan.AutoChosenCorridors(_plan, _corridors);
        Debug.Log($"{Tag} 시작 → {name} (단계 {_director.Step}) — 할 일 {(_plan.Count > 0 ? string.Join(" → ", _plan) : "없음(이동만)")}", this);

        foreach (TutorialSkipAction action in _plan.ToArray())
        {
            // 앞 단계가 보류 게이트까지 같이 열었을 수 있다(존 클리어 보류 → 앞 게이트 열림 때 함께).
            if (!_director.IsGateOpen(action.Gate)) Execute(action);

            float until = Time.realtimeSinceStartup + TutorialSkipPlan.GateWaitSeconds;
            while (!_director.IsGateOpen(action.Gate) && Time.realtimeSinceStartup < until) yield return null;

            if (!_director.IsGateOpen(action.Gate))
            {
                Debug.LogWarning($"{Tag} {action} — {TutorialSkipPlan.GateWaitSeconds:0}초 안에 정식 경로로 안 열렸다 → 강제 개방(디버그 메뉴와 같은 코드).", this);
                TutorialDebugActions.OpenGatesThrough(_director, action.Gate);
                if (!_director.IsGateOpen(action.Gate))
                {
                    Fail($"→ {name} 중단 — {action.Gate} 를 열 수 없다(순서 규칙). 이동하지 않는다.");
                    yield break;
                }
            }

            // 흐름들(복도·존)의 게이트 후속 처리는 다음 Update 에서 돈다 — 한 프레임 넘긴다.
            yield return null;
        }

        TutorialArea area = TutorialSkipPlan.AreaOf(target);
        _director.SetStepServer(TutorialSkipPlan.StepOf(target));   // 앞으로만 — 같거나 이미 앞이면 그대로
        int moved = MoveParty(area);

        bool bossRoom = target == TutorialSkipTarget.BossRoom;
        string bossNote = "";
        if (bossRoom)
        {
            BossTeleportManager teleport = BossTeleportManager.Instance;
            bool started = teleport != null && teleport.ForceStartEncounter(TutorialSkipPlan.BossRoomCountdownSeconds);
            bossNote = started
                ? $" · 패드 이동 {TutorialSkipPlan.BossRoomCountdownSeconds:0}초 뒤"
                : " · 패드 이동 시작 실패(이미 카운트다운·연출 중이거나 BossTeleportManager 없음)";
        }

        int opened = _director.OpenedGateMaskServer & ~before;
        string traits = _corridors.Count > 0 ? string.Join("·", _corridors) : "없음";
        Debug.Log($"{Tag} → 구간 {name} (열린 게이트 {TutorialSkipPlan.DescribeGates(opened)} · 특성 자동 획득 복도 {traits} · " +
                  $"이동 {moved}명 · 단계 {_director.Step}{bossNote})", this);
        Toast($"구간 스킵 → {name} (열린 게이트 {TutorialSkipPlan.DescribeGates(opened)})");
        _running = false;
    }

    void Execute(TutorialSkipAction action)
    {
        switch (action.Kind)
        {
            case TutorialSkipActionKind.CompleteZone1Checks:
                foreach (ulong id in _nm.ConnectedClientsIds)
                    for (int bit = 0; bit < 32; bit++)
                        if ((TutorialFlowModel.G1Mask & (1 << bit)) != 0) _director.ReportCheckServer(id, (TutorialCheck)bit);
                break;

            case TutorialSkipActionKind.AutoChooseTraits:
                TutorialCorridorFlow corridor = GetComponent<TutorialCorridorFlow>();
                if (corridor != null) corridor.SkipRoundServer(action.Corridor);
                else
                    foreach (ulong id in _nm.ConnectedClientsIds)
                        _director.ReportCheckServer(id, TutorialCorridorRules.CheckOf(action.Corridor));
                break;

            case TutorialSkipActionKind.ClearZone:
                // 장부 경로 — 처치 → 존 클리어 → RequestGateServer. 몬스터가 0마리면 장부가 클리어를 못 내니 같은 요청을 직접 한다.
                int killed = TutorialDebugActions.KillZoneMonsters(_director, action.Zone, TutorialMonsterFilter.All);
                if (killed == 0 && !_director.IsZoneClearedServer(action.Zone))
                {
                    Debug.LogWarning($"{Tag} {action.Zone} 에 살아 있는 몬스터가 없다 — 장부 클리어 없이 {action.Gate} 요청.", this);
                    _director.RequestGateServer(action.Gate);
                }
                break;

            case TutorialSkipActionKind.CompleteSpinnerPractice:
                int target = _director.GetCounterTarget(TutorialCounter.Zone3Interrupts);
                int now = _director.GetCounter(TutorialCounter.Zone3Interrupts);
                if (now < target) _director.AddCounterServer(TutorialCounter.Zone3Interrupts, target - now);
                _director.RequestGateServer(TutorialGate.G5_SpinnerRealMode);   // → 존3 흐름 본 모드(연습 없이)
                break;

            case TutorialSkipActionKind.StartBossTimer:
                _director.StartBossTimerServer();   // = G8 → 존4 흐름 전투 시작
                break;
        }
    }

    // 개인 뒤 배리어를 목적지 기준으로 다시 쓴 뒤(전멸 리스폰과 같은 예외) 입구 안쪽 안전 지점으로 옮긴다. 옮긴 인원 수.
    int MoveParty(TutorialArea area)
    {
        _movers.Clear();
        foreach (NetworkClient client in _nm.ConnectedClientsList)
        {
            NetworkObject po = client.PlayerObject;
            if (po == null) continue;
            var life = po.GetComponent<PlayerLifeCycleController>();
            if (life != null && life.State == PlayerLifeState.PermanentDead) continue;
            _director.ResetBackBarrierMaskServer(client.ClientId, area);
            _movers.Add(po);
        }
        if (_movers.Count == 0) return 0;

        TutorialDeathRules rules = GetComponent<TutorialDeathRules>();
        if (rules == null)
        {
            Debug.LogWarning($"{Tag} TutorialDeathRules 가 없다 — 안전 지점을 못 구해 이동하지 않는다.", this);
            return 0;
        }

        // 기준점 = 그 구간으로 들어가는 배리어(입구) — 없으면(존1) 구간 가운데.
        float refX, refZ;
        if (rules.TryGetEntryBarrier(area, out TutorialBarrierSpec entry))
        {
            refX = entry.Rect.CenterX;
            refZ = entry.Rect.CenterZ;
        }
        else if (rules.TryGetAreaRect(area, out TutorialRect rect))
        {
            refX = rect.CenterX;
            refZ = rect.CenterZ;
        }
        else
        {
            Debug.LogWarning($"{Tag} {area} 구간 모양이 없다 — 이동하지 않는다.", this);
            return 0;
        }

        if (!rules.TryFindAreaSpreadPoints(area, _movers.Count, refX, refZ, _points))
        {
            Debug.LogWarning($"{Tag} {area} 안 안전 지점이 없다(바닥·점유·패드) — 이동하지 않는다.", this);
            return 0;
        }

        int moved = 0;
        for (int i = 0; i < _movers.Count; i++)
        {
            var recovery = _movers[i].GetComponent<PlayerFallRecovery>();
            if (recovery != null && recovery.RelocateServer(_points[i])) moved++;
            else Debug.LogWarning($"{Tag} client {_movers[i].OwnerClientId} 순간이동 불가(PlayerFallRecovery 없음).", this);
        }
        return moved;
    }

    void Warn(string message)
    {
        Debug.LogWarning($"{Tag} {message}", this);
        Toast(message);
    }

    void Fail(string message)
    {
        Debug.LogError($"{Tag} {message}", this);
        Toast(message);
        _running = false;
    }

    // ─── 화면(로컬) ──────────────────────────────────────────────────────

    void Toast(string message)
    {
        _toast = message;
        _toastUntil = Time.unscaledTime + ToastSeconds;
    }

    void OnGUI()
    {
        if (!string.IsNullOrEmpty(_toast) && Time.unscaledTime <= _toastUntil)
        {
            _toastStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = Color.yellow } };
            GUI.Label(new Rect(12f, 44f, 900f, 28f), _toast, _toastStyle);
        }

        if (!IsHost || _director == null || !_director.IsTutorialActive || !IsAltHeld()) return;
        DrawPanel();
    }

    // Alt 를 누르는 동안만 — 버튼 10개(Alt+1 ~ Alt+9 구간, 마지막 Alt+0 처음부터). 뒤 구간은 비활성, 지금 구간은 ▶.
    // 처음부터는 어느 구간에서든(스킵 진행 중에도) 누를 수 있고, 판 재시작 중엔 전부 비활성.
    void DrawPanel()
    {
        const float w = 190f, h = 24f, pad = 6f;
        float x = Screen.width - w - 12f, y = 12f;
        bool restarting = IsRestartInProgress;
        GUI.Box(new Rect(x - pad, y - pad, w + pad * 2f, h * (TutorialSkipPlan.TargetCount + 1) + pad * 2f), GUIContent.none);
        GUI.Label(new Rect(x, y, w, h), restarting ? "구간 스킵 (재시작 중…)" : _running ? "구간 스킵 (진행 중…)" : "구간 스킵 (호스트)");

        TutorialStep step = _director.Step;
        TutorialSkipTarget current = TutorialSkipPlan.CurrentOf(step);
        bool enabledBefore = GUI.enabled;
        for (int i = 0; i < TutorialSkipPlan.TargetCount; i++)
        {
            int digit = (i + 1) % 10;   // Alt+1 … Alt+9, Alt+0
            TutorialSkipPlan.TryFromDigit(digit, out TutorialSkipTarget target);
            string label = $"{(target == current ? "▶ " : "")}Alt+{digit}  {TutorialSkipPlan.NameOf(target)}";
            GUI.enabled = (!_running || TutorialSkipPlan.IsRestart(target)) &&
                          TutorialSkipPlan.Check(step, target, restarting) == TutorialSkipVerdict.Ok;
            if (GUI.Button(new Rect(x, y + h * (i + 1), w, h - 2f), label)) Request(target);
        }
        GUI.enabled = enabledBefore;
    }
}
#endif
