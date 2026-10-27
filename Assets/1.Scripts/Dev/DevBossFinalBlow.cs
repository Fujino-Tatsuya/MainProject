#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 개발용 "보스 막타 직전" 키(2026-10-27 경석 요청 — 보스 B 승리 연출 테스트, PLAN-boss-victory-cinematic).
///
/// <list type="bullet">
/// <item><b>F11</b> — 어디에 있든 한 번 누르면 단계를 알아서 이어 간다:
///   튜토리얼 중이면 보스 진입까지 스킵(Alt+8 과 같은 경로) → 엘리베이터 스킵(정상 엘리베이터 경로에 시작 시각만 당김 — 카메라 복귀도 정상 경로) → 등장 연출 스킵(전원 동의 스킵과 같은 경로)
///   → 전투가 시작되면 23호의 남은 HP 페이즈 기믹·0줄 자폭을 끝난 것으로 두고 HP 1. <b>다음 한 대(누가 때리든)가 실제 처치</b>다.</item>
/// </list>
///
/// 🔴 <b>DEV_HOTKEYS 를 지운 릴리스 빌드에는 안 들어간다.</b> 파일 전체가 <c>UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS</c> 안에 있다.
/// 🔴 <b>배선이 없다.</b> <see cref="DevBossHpCut"/>(F4)처럼 <see cref="RuntimeInitializeOnLoadMethod"/> 로 스스로 올라온다.
/// 🔴 <b>호스트에서만 듣는다.</b> 이동·연출·체력은 서버 권한이다. 엘리베이터 연출·도착 대기·기믹 진행 중이면 끝나길 기다렸다 이어 간다.
/// </summary>
public class DevBossFinalBlow : MonoBehaviour
{
    const KeyCode Key = KeyCode.F11;
    const float TimeoutSeconds = 60f;
    const float TutorialSettleSeconds = 1.5f;   // Alt+8 경로로 넘어갔을 때만 — 그 스킵이 위치·상태를 정리할 틈
    const string Tag = "[DevBossFinalBlow]";

    bool _running;
    float _startedAt;
    bool _tutorialSkipRequested;
    bool _tutorialSlowPath;
    float _tutorialReadyAt = -1f;
    bool _teleportRequested;
    string _lastStatus;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        // 도메인 리로드를 꺼 두면 두 번째 Play 에서 또 불린다. 이미 있으면 그대로 둔다.
        if (FindAnyObjectByType<DevBossFinalBlow>() != null) return;

        var go = new GameObject("[Dev] BossFinalBlow");
        go.AddComponent<DevBossFinalBlow>();
        DontDestroyOnLoad(go);
        Debug.Log($"{Tag} F11 = 어디서든 엘리베이터 스킵 → 등장 연출 스킵 → 23호 HP 1(페이즈·자폭 완료, 다음 한 대가 처치) (호스트에서만, 디버그 단축키 빌드 전용)");
    }

    void Update()
    {
        if (Input.GetKeyDown(Key)) Begin();
        if (_running) Step();   // 매 프레임 — 조건이 맞는 즉시 다음 단계로
    }

    void Begin()
    {
        if (_running)
        {
            Debug.Log($"{Tag} 이미 진행 중 — {_lastStatus ?? "시작"}");   // 다시 눌러도 처음부터 하지 않는다
            return;
        }

        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
        {
            Debug.LogWarning($"{Tag} 네트워크가 아직 안 떴다.");
            return;
        }
        if (!nm.IsServer)
        {
            Debug.LogWarning($"{Tag} 클라에서는 못 합니다 — 이동·연출·체력은 서버 권한입니다. 호스트에서 누르세요.");
            return;
        }

        _running = true;
        _startedAt = Time.unscaledTime;
        _tutorialSkipRequested = false;
        _tutorialSlowPath = false;
        _tutorialReadyAt = -1f;
        _teleportRequested = false;
        _lastStatus = null;
        Debug.Log($"{Tag} 시작 — 보스 막타 직전까지 진행한다.");
    }

    void Step()
    {
        if (Time.unscaledTime - _startedAt > TimeoutSeconds)
        {
            Finish($"{TimeoutSeconds:0}초 안에 못 끝냈다 — 마지막 상태: {_lastStatus ?? "없음"}", false);
            return;
        }

        BossEncounterDirector director = BossEncounterDirector.Instance;
        if (director == null || !director.IsSpawned)
        {
            Status("이 씬에 보스 디렉터가 없다 — 보스방이 있는 맵에서 누른다");
            return;
        }

        // 4) 전투 중 — 막타 준비. 기믹·자폭 진행 중이면 다음 시도에 다시.
        if (director.DevIsBossInCombat)
        {
            bool done = false;
            foreach (TwentyThreeBoss boss in FindObjectsByType<TwentyThreeBoss>(FindObjectsSortMode.None))
            {
                if (boss == null || !boss.isActiveAndEnabled || boss.CurrentHealth <= 0) continue;
                string result = boss.DevPrepareFinalBlowServer(out bool ok);
                if (ok) { done = true; Debug.Log($"{Tag} '{boss.name}' — {result}", boss); }
                else Status($"막타 준비 대기 — {result}");
            }
            if (done)
            {
                Finish("완료 — 다음 한 대가 처치다", true);
                StartCoroutine(DumpCameraLater());
            }
            return;
        }

        // 3) 등장 연출 — 바로 스킵(카메라 복귀 뒤 전투).
        if (director.DevIsIntroRunning)
        {
            if (director.DevSkipIntroServer()) Debug.Log($"{Tag} 등장 연출 스킵(전원 동의 스킵과 같은 경로)");
            Status("등장 연출 → 전투 대기");
            return;
        }

        if (director.Phase == BossEncounterPhase.AwaitingArrival)
        {
            Status("보스방 도착 대기");
            return;
        }

        // 1) 튜토리얼 중이면 보스 진입 단계로. 🔴 Alt+8 구간 스킵은 게이트마다 정식 경로(존 처치·특성 자동 획득)를 최대 5초씩 기다려 느리다
        //    (경석 10-27 Play) — 보스 테스트엔 필요 없으니 디버그 메뉴와 같은 강제 개방으로 한 프레임에 연다. 순서 규칙에 막히면 그때만 Alt+8 경로.
        TutorialDirector tutorial = TutorialDirector.Instance;
        if (tutorial != null && tutorial.IsTutorialActive && tutorial.Step < TutorialStep.BossEnter)
        {
            if (!_tutorialSkipRequested)
            {
                _tutorialSkipRequested = true;
                TutorialStep from = tutorial.Step;
                TutorialDebugActions.OpenGatesThrough(tutorial, TutorialGate.G10_BossEnterAndPad);
                if (tutorial.IsGateOpen(TutorialGate.G10_BossEnterAndPad) && tutorial.SetStepServer(TutorialStep.BossEnter))
                {
                    Debug.Log($"{Tag} 튜토리얼 {from} → 보스 진입(게이트 G10 까지 강제 개방)");
                }
                else
                {
                    tutorial.RequestSkip(TutorialSkipTarget.BossEnter);
                    Debug.LogWarning($"{Tag} 튜토리얼 강제 개방이 막혔다 → Alt+8 구간 스킵으로 대신(게이트마다 기다려 느리다)");
                    _tutorialSlowPath = true;
                }
            }
            if (tutorial.Step < TutorialStep.BossEnter)
            {
                Status("튜토리얼 보스 진입 스킵 대기");
                return;
            }
        }
        // Alt+8 경로로 넘어갔을 때만 — 그 스킵이 파티 이동·후처리를 마칠 틈.
        if (_tutorialSlowPath && _tutorialReadyAt < 0f)
        {
            _tutorialReadyAt = Time.unscaledTime + TutorialSettleSeconds;
            Status("튜토리얼 스킵 정리 대기");
            return;
        }
        if (_tutorialReadyAt > 0f && Time.unscaledTime < _tutorialReadyAt) return;

        // 2) 보스방 이동 — 카운트다운·엘리베이터 없이. 연출 진행 중이면 기다린다.
        if (!_teleportRequested)
        {
            BossTeleportManager teleport = BossTeleportManager.Instance;
            if (teleport == null || !teleport.IsSpawned)
            {
                Status("보스 텔레포트 매니저가 없다");
                return;
            }
            string refused = teleport.DevStartElevatorSkippedServer();
            if (refused != null)
            {
                Status($"보스방 이동 대기 — {refused}");
                return;
            }
            _teleportRequested = true;
            Debug.Log($"{Tag} 엘리베이터 스킵(정상 경로·시작 시각만 당김) → 보스방");
        }
        Status("보스방 이동 → 등장 대기");
    }

    // 카메라 진단 — "보스방에서 카메라가 플레이어를 안 따라간다"(경석 10-27) 원인을 한 번에 가리려고, 끝난 직후·2초 뒤 화면을 잡은 vcam 을 찍는다.
    System.Collections.IEnumerator DumpCameraLater()
    {
        DumpCamera("직후");
        yield return new WaitForSecondsRealtime(2f);
        DumpCamera("2초 뒤");
    }

    static void DumpCamera(string when)
    {
        Camera main = Camera.main;
        var brain = main != null ? main.GetComponent<Unity.Cinemachine.CinemachineBrain>() : null;
        string live = brain != null && brain.ActiveVirtualCamera != null ? brain.ActiveVirtualCamera.Name : "없음";
        Player local = Player.LocalPlayer;
        CameraTargetSwitcher sw = CameraTargetSwitcher.Active;
        var sb = new System.Text.StringBuilder();
        sb.Append($"{Tag} 카메라({when}) — 화면 vcam '{live}' · 카메라 {(main != null ? main.transform.position.ToString("F1") : "없음")} · " +
                  $"내 플레이어 {(local != null ? local.transform.position.ToString("F1") : "없음")} · " +
                  $"스위처 대상 {(sw != null && sw.CurrentFollowTarget != null ? sw.CurrentFollowTarget.name + sw.CurrentFollowTarget.position.ToString("F1") : "없음")} · " +
                  $"연출중 {(sw != null && sw.IsInCinematic)} · 임시추적 {(sw != null && sw.HasTemporaryFollow)} · 낙하뷰 {(sw != null && sw.IsInFallView)}");
        foreach (var c in FindObjectsByType<Unity.Cinemachine.CinemachineCamera>(FindObjectsSortMode.None))
        {
            if (c == null) continue;
            sb.Append($"\n   · '{c.name}' 우선 {c.Priority.Value} · 켜짐 {c.isActiveAndEnabled} · 위치 {c.transform.position.ToString("F1")} · " +
                      $"Follow {(c.Follow != null ? c.Follow.name + c.Follow.position.ToString("F1") : "없음")}");
        }
        Debug.Log(sb.ToString());
    }

    void Status(string status)
    {
        if (status == _lastStatus) return;
        _lastStatus = status;
        Debug.Log($"{Tag} {status}");
    }

    void Finish(string message, bool ok)
    {
        _running = false;
        if (ok) Debug.Log($"{Tag} {message}");
        else Debug.LogWarning($"{Tag} {message}");
    }
}
#endif
