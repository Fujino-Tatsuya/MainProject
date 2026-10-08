using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// QA 세션 수명주기 오케스트레이터. 게임 코드 무수정 — public 진입점만 호출한다.
/// 부팅(0.BootStrapScene)→타이틀→로비→호스트→로딩→4.MapScene 자동 진행 후,
/// 플레이어가 스폰되면 QAAgent에 조작 대상을 넘기고, 감지기 4종을 세션 내내 돌려
/// 종료 시 로컬 리포트를 남긴다.
///
/// 반복(RepeatCount): 한 사이클 = 부팅흐름 진입 + Play시간 + 리포트 1개.
/// 2회차부터는 네트워크를 종료하고 로비→호스트→맵을 다시 태워 "부팅부터 재시작"한다
/// (GameManager 등 DontDestroyOnLoad 싱글턴은 유지되므로 GoToLobby부터 재구동).
/// RepeatCount &lt;= 0 이면 무한 반복(사용자가 Play 정지 시까지).
/// </summary>
public sealed class QASessionController : MonoBehaviour
{
    [Header("자동 진행 씬 이름(빌드 세팅 기준)")]
    [SerializeField] private string titleScene = "1.TitleScene";
    [SerializeField] private string lobbyScene = "3.LobbyScene";

    [Header("세션")]
    [Tooltip("맵 도달 후 각 사이클을 돌릴 시간(초).")]
    [SerializeField] private float sessionDuration = 180f;
    [Tooltip("QA 전체 사이클 반복 횟수. 0 이하이면 무한 반복.")]
    [SerializeField] private int repeatCount = 1;
    [Tooltip("각 진행 단계가 이 시간 내 완료되지 않으면 흐름 타임아웃(소프트락)으로 기록.")]
    [SerializeField] private float phaseTimeout = 45f;
    [Tooltip("스폰(로딩 포함) 대기 타임아웃(초).")]
    [SerializeField] private float spawnTimeout = 90f;

    [Header("멀티플레이(MPPM)")]
    [Tooltip("인원 수(호스트 포함). 3=호스트1+클라2(가상 플레이어). 1=호스트 단독.")]
    [SerializeField] private int expectedPlayers = 3;
    [Tooltip("멀티플레이 접속·ready·스폰 대기 타임아웃(초).")]
    [SerializeField] private float multiplayerTimeout = 120f;

    [Header("전멸 조기 종료")]
    [Tooltip("전원(기대 인원 전부)이 유령(Soul)/영구사망 상태로 이 시간(초) 이상 유지되면 세션 시간이 남아도 사이클을 조기 종료한다.")]
    [SerializeField] private float allDefeatedGraceSeconds = 10f;

    [Header("결과 화면 경로 모드(QAResultFlow)")]
    [Tooltip("켜면 Play 시간 뒤 호스트가 판을 끝내 결과 화면까지 보낸다. 사이클 1=중도 종료, 2=전멸, 3=클리어.")]
    [SerializeField] private bool resultPaths;
    [Tooltip("판 종료 강제 시작부터 결과 씬 진입까지 대기 한도(초). 클리어는 텔레포트·연출·사망 클립까지 포함한다.")]
    [SerializeField] private float resultWaitTimeout = 300f;
    [Tooltip("호스트가 결과 검증 뒤 네트워크를 끄기 전 기다리는 시간(초) — 클라가 결과 씬에 들어가 검증할 시간.")]
    [SerializeField] private float hostResultLingerSeconds = 12f;

    private readonly QAResultFlow _resultFlow = new QAResultFlow();

    private const string ConnectIp = "127.0.0.1";
    private const ushort ConnectPort = 7777;

    private readonly List<IQADetector> _detectors = new List<IQADetector>();
    private QARecorder _recorder;
    private QAInputController _input;
    private bool _running;
    private bool _timedOut;
    private bool _cycleFailed;
    private int _iteration;
    private string _cycleSummary = "중단";

    /// <summary>부트스트랩이 활성화 전에 EditorPrefs 값으로 주입한다.</summary>
    private QAResultPath[] _pathOrder = { QAResultPath.Aborted, QAResultPath.Wiped, QAResultPath.Cleared };

    /// <summary>"Wiped,Cleared" 처럼 쉼표로 경로 순서를 준다. 비거나 못 읽으면 기본(중도 종료→전멸→클리어).</summary>
    public void ConfigurePathOrder(string order)
    {
        var parsed = new List<QAResultPath>();
        foreach (string token in (order ?? string.Empty).Split(','))
        {
            if (Enum.TryParse(token.Trim(), true, out QAResultPath p))
                parsed.Add(p);
        }
        if (parsed.Count > 0)
            _pathOrder = parsed.ToArray();
    }

    private QAResultPath PathForIteration(int iteration) => _pathOrder[(iteration - 1) % _pathOrder.Length];

    public void Configure(float sessionDurationSeconds, int repeats, int playerCount, bool resultPathsMode)
    {
        sessionDuration = Mathf.Max(5f, sessionDurationSeconds);
        repeatCount = repeats;
        expectedPlayers = Mathf.Max(1, playerCount);
        resultPaths = resultPathsMode;
    }

    private void Start()
    {
        _input = GetComponent<QAInputController>();

        // 감지기는 한 번 생성해 사이클마다 재사용(OnSessionStart/End로 리셋).
        _detectors.Add(new LogErrorDetector());
        _detectors.Add(new SoftlockDetector());
        _detectors.Add(new PerfDetector());
        _detectors.Add(new NetworkDesyncDetector());
        _detectors.Add(new GameplayInvariantDetector());

        Player.LocalPlayerChanged += OnLocalPlayerChanged;

        string plan = repeatCount <= 0 ? "무한 반복" : $"{repeatCount}회 반복";
        string role = IsHostRole() ? "호스트" : "클라이언트(가상 플레이어)";
        Debug.Log($"[QA] 세션 시작 — 역할={role}, {expectedPlayers}인, 사이클당 {sessionDuration:F0}초, {plan}. 부팅부터 자동 진행합니다.");
        StartCoroutine(RunAll());
    }

    private void Update()
    {
        if (!_running)
            return;

        float dt = Time.deltaTime;
        for (int i = 0; i < _detectors.Count; i++)
            _detectors[i].Tick(_recorder, dt);
    }

    private void OnLocalPlayerChanged(Player player)
    {
        if (_input == null)
            return;

        _input.SetTarget(player);
        if (player != null)
            Debug.Log("[QA] 플레이어 스폰 감지 — QA 조작 시작.");
    }

    private IEnumerator RunAll()
    {
        _iteration = 0;
        while (repeatCount <= 0 || _iteration < repeatCount)
        {
            _iteration++;
            string label = repeatCount <= 0 ? "무한" : $"{repeatCount}";
            Debug.Log($"[QA] ===== 사이클 {_iteration}/{label} 시작 =====");

            BeginCycle();
            yield return RunCycle(_iteration == 1);
            EndCycle(_cycleSummary);

            bool more = repeatCount <= 0 || _iteration < repeatCount;
            if (more)
                yield return ResetBetweenCycles();
        }

        Debug.Log($"[QA] 전체 QA 반복 완료 ({_iteration}회) — Play 모드를 자동 종료합니다.");

#if UNITY_EDITOR
        // RepeatCount<=0(무한)이면 이 지점에 도달하지 않는다(위 while이 계속 돌아 사용자가
        // 직접 정지할 때까지 유지) — 정해진 반복이 전부 끝났을 때만 자동 종료한다.
        EditorApplication.isPlaying = false;
#endif
    }

    private IEnumerator RunCycle(bool first)
    {
        _cycleSummary = "중단";
        _cycleFailed = false;

        // 1) 첫 사이클만 부팅 완료 대기(GameManager 준비 + 타이틀 씬 로드).
        if (first)
        {
            yield return WaitOrTimeout(() => GameManager.Instance != null, phaseTimeout);
            if (AbortCycle("부팅: GameManager 미준비")) yield break;

            yield return WaitOrTimeout(() => QAUtil.ActiveSceneName() == titleScene, phaseTimeout);
            if (AbortCycle("타이틀 씬 진입")) yield break;
        }

        // 2) 로비로.
        Debug.Log("[QA] → GoToLobby");
        GameManager.Instance.GoToLobby();
        yield return WaitOrTimeout(
            () => QAUtil.ActiveSceneName() == lobbyScene && GetLauncher() != null,
            phaseTimeout);
        if (AbortCycle("로비 씬 진입")) yield break;

        // 3) 역할 분기: 메인 에디터 = 호스트, MPPM 가상 플레이어 = 클라이언트.
        NetworkSessionLauncher launcher = GetLauncher();
        if (IsHostRole())
            yield return RunHostLobby(launcher);
        else
            yield return RunClientLobby(launcher);
        if (_cycleFailed) yield break;

        // 4) 플레이어 스폰 대기(호스트가 맵 로딩 후 서버가 스폰).
        float spawnWait = expectedPlayers > 1 ? multiplayerTimeout : spawnTimeout;
        yield return WaitOrTimeout(() => Player.LocalPlayer != null, spawnWait);
        if (AbortCycle("맵 로딩·플레이어 스폰")) yield break;

        if (resultPaths)
        {
            yield return RunResultPhase();
            yield break;
        }

        // 5) 플레이 세션. 시간 만료 또는 전멸(전원 유령) 10초 경과 중 먼저 오는 쪽으로 끝난다.
        Debug.Log($"[QA] 전투 씬 도달 — {sessionDuration:F0}초 QA 세션 진행.");
        float endTime = Time.time + sessionDuration;
        _cycleSummary = $"정상 종료: {sessionDuration:F0}초 세션 완료";
        yield return WaitForSessionEnd(endTime);
    }

    /// <summary>
    /// 결과 화면 경로 모드: Play 시간 동안 평소처럼 돌린 뒤 호스트가 이번 사이클의 경로로 판을 끝내고,
    /// 모든 피어가 결과 씬에서 SessionResult·화면 행을 검증한다. 클라는 결과 씬이 뜰 때까지 기다리기만 한다.
    /// </summary>
    private IEnumerator RunResultPhase()
    {
        bool host = IsHostRole();
        QAResultPath path = PathForIteration(_iteration);
        QAResultPath? expected = host ? path : (QAResultPath?)null;
        Debug.Log($"[QA] 전투 씬 도달 — 결과 화면 경로 모드: {sessionDuration:F0}초 플레이 후 " +
                  (host ? $"{path} 로 종료." : "호스트의 종료를 기다린다."));

        if (host)
        {
            // 플레이 중 자연히 결과가 나면(무작위 플레이 전멸 등) 강제 경로는 건너뛴다.
            float endTime = Time.time + sessionDuration;
            while (Time.time < endTime && !SessionResult.HasValue && !QAResultFlow.InResultScene)
                yield return null;

            if (SessionResult.HasValue || QAResultFlow.InResultScene)
            {
                _resultFlow.Note($"플레이 중 결과 자연 확정({SessionResult.Outcome}) — 강제 경로 {path} 생략");
                expected = null;
            }
            else
            {
                yield return _resultFlow.DriveHost(path, _recorder);
            }

            yield return WaitOrTimeout(() => QAResultFlow.InResultScene, resultWaitTimeout);
        }
        else
        {
            yield return WaitOrTimeout(() => QAResultFlow.InResultScene, sessionDuration + resultWaitTimeout);
        }
        if (AbortCycle("결과 씬 진입")) yield break;

        // ResultStatsView.Start 가 행을 복제할 시간.
        yield return new WaitForSeconds(1.5f);
        _resultFlow.Verify(_recorder, expected, host);
        _cycleSummary = $"결과 화면 검증 — 경로 {(host ? path.ToString() : "(호스트 기준)")} → {SessionResult.Outcome}";

        if (host)
            yield return new WaitForSeconds(hostResultLingerSeconds);
    }

    /// <summary>
    /// 세션 종료 대기: 시간 만료(endTime) 또는 기대 인원 전부가 유령(Soul)/영구사망 상태로
    /// allDefeatedGraceSeconds 이상 유지되는 것 중 먼저 만족되는 쪽으로 끝난다.
    /// 전멸 조기 종료 시 _cycleSummary를 덮어써 리포트 요약에 남긴다.
    /// </summary>
    private IEnumerator WaitForSessionEnd(float endTime)
    {
        float allDefeatedSince = -1f;
        while (Time.time < endTime)
        {
            if (AreAllPlayersDefeated())
            {
                if (allDefeatedSince < 0f)
                    allDefeatedSince = Time.time;

                float elapsed = Time.time - allDefeatedSince;
                if (elapsed >= allDefeatedGraceSeconds)
                {
                    _cycleSummary = $"조기 종료: 전원 유령(Soul) 상태 {allDefeatedGraceSeconds:F0}초 경과";
                    _recorder.Add(QASeverity.Info, "Flow",
                        $"전원 전멸 확인 후 {allDefeatedGraceSeconds:F0}초 경과 — 세션 조기 종료");
                    yield break;
                }
            }
            else
            {
                allDefeatedSince = -1f;
            }

            yield return null;
        }
    }

    /// <summary>기대 인원 전부를 찾았고, 전부 유령(Soul) 또는 영구사망 상태인지.</summary>
    private bool AreAllPlayersDefeated()
    {
        Player[] players = FindObjectsByType<Player>(FindObjectsSortMode.None);
        if (players.Length < expectedPlayers)
            return false;

        for (int i = 0; i < players.Length; i++)
        {
            PlayerLifeCycleController life = players[i] != null
                ? players[i].GetComponent<PlayerLifeCycleController>()
                : null;
            if (life == null)
                return false;

            if (life.State != PlayerLifeState.Soul && life.State != PlayerLifeState.PermanentDead)
                return false;
        }

        return true;
    }

    /// <summary>타임아웃이 났으면 Critical로 기록하고 현재 사이클 요약을 설정한다.</summary>
    private bool AbortCycle(string phase)
    {
        if (!_timedOut)
            return false;

        _cycleFailed = true;
        _recorder.Add(QASeverity.Critical, "Flow",
            $"진행 단계 '{phase}'에서 진척 없음(부트/네트워크 흐름 소프트락 의심)");
        _cycleSummary = $"흐름 타임아웃: {phase}";
        return true;
    }

    /// <summary>호스트 역할: StartHost → 클라 접속·ready 대기(멀티) → StartGameLoading.</summary>
    private IEnumerator RunHostLobby(NetworkSessionLauncher launcher)
    {
        Debug.Log("[QA][Host] → StartHost");
        launcher.StartHost();
        yield return WaitOrTimeout(
            () => NetworkManager.Singleton != null &&
                  NetworkManager.Singleton.IsListening &&
                  NetworkManager.Singleton.IsHost,
            phaseTimeout);
        if (AbortCycle("호스트 시작")) yield break;

        if (expectedPlayers > 1)
        {
            Debug.Log($"[QA][Host] 클라이언트 {expectedPlayers - 1}명 접속·ready 대기");
            yield return WaitOrTimeout(HostReadyToStart, multiplayerTimeout);
            if (AbortCycle($"클라이언트 접속·ready(기대 {expectedPlayers}인)")) yield break;
        }

        yield return new WaitForSeconds(0.5f);
        Debug.Log("[QA][Host] → StartGameLoading");
        launcher.StartGameLoading();
    }

    /// <summary>모든 인원 접속 + 전원 ready(호스트는 자동 ready)일 때만 게임 시작 가능.</summary>
    private bool HostReadyToStart()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer)
            return false;
        if (nm.ConnectedClientsList.Count < expectedPlayers)
            return false;
        LobbyUIController lobby = LobbyUIController.Active;
        return lobby == null || lobby.CanStartGame;
    }

    /// <summary>클라이언트 역할(MPPM 가상 플레이어): 호스트에 접속 → ready. 스폰은 공통 단계에서 대기.</summary>
    private IEnumerator RunClientLobby(NetworkSessionLauncher launcher)
    {
        launcher.OnSetConnectionData(ConnectIp, ConnectPort);

        // 호스트가 아직 안 떴을 수 있어 접속을 재시도한다.
        float deadline = Time.time + multiplayerTimeout;
        while (Time.time < deadline &&
               !(NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient))
        {
            NetworkManager nm = NetworkManager.Singleton;
            bool attemptInFlight = nm != null && (nm.IsClient || nm.IsListening);
            if (!attemptInFlight)
            {
                Debug.Log($"[QA][Client] → StartClient ({ConnectIp}:{ConnectPort})");
                launcher.StartClient();
            }
            yield return new WaitForSeconds(2f);
        }

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient)
        {
            _timedOut = true;
            if (AbortCycle("클라이언트 접속")) yield break;
        }

        Debug.Log("[QA][Client] 접속 완료 → ready");
        yield return WaitOrTimeout(() => LobbyUIController.Active != null, phaseTimeout);
        if (AbortCycle("로비 UI 대기")) yield break;

        LobbyUIController.Active.SetLocalReady(true);
        // 호스트가 StartGameLoading하면 서버가 스폰 → 공통 4)에서 Player.LocalPlayer 대기.
    }

    /// <summary>MPPM 가상 플레이어(클론)는 --virtual-project-clone 인자를 갖는다. 메인 에디터=호스트.</summary>
    private static bool IsHostRole()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--virtual-project-clone" || args[i] == "com.unity.mppm.clone")
                return false;
        }
        return true;
    }

    private void BeginCycle()
    {
        QABlackboard.Reset();
        _recorder = new QARecorder { SessionStartTime = Time.time };
        for (int i = 0; i < _detectors.Count; i++)
            _detectors[i].OnSessionStart(_recorder);
        if (resultPaths)
            _resultFlow.Begin();
        _running = true;
    }

    private void EndCycle(string summary)
    {
        if (!_running)
            return;
        _running = false;
        QABlackboard.Controlling = false;

        for (int i = 0; i < _detectors.Count; i++)
            _detectors[i].OnSessionEnd(_recorder);

        if (resultPaths)
            _resultFlow.End();

        string dir = QAReportWriter.Write(_recorder, $"사이클 {_iteration} — {summary}");

        if (dir != null && resultPaths)
        {
            string text = _resultFlow.Dump ?? "# 결과 화면에 도달하지 못함\n\n## 진행 로그\n\n" + _resultFlow.LogText;
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "result-stats.md"), text);
        }

        if (dir != null && _recorder.CountBySeverity(QASeverity.Critical) > 0)
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "end.png"));

        Debug.Log($"[QA] 사이클 {_iteration} 종료: {summary} " +
                  $"(Critical {_recorder.CountBySeverity(QASeverity.Critical)}, " +
                  $"Error {_recorder.CountBySeverity(QASeverity.Error)}, " +
                  $"Warning {_recorder.CountBySeverity(QASeverity.Warning)})");
    }

    /// <summary>다음 사이클을 위해 네트워크를 종료하고 안정될 때까지 대기(부팅부터 재시작 준비).</summary>
    private IEnumerator ResetBetweenCycles()
    {
        _input?.SetTarget(null);

        // 결과 화면 경로 모드의 클라: 호스트가 결과 씬에 머무는 동안(hostResultLingerSeconds) 먼저 끊고
        // 재접속하면 끝나가는 세션에 다시 붙는다(10-08 1차 — 사이클 2·3 로비 소프트락). 호스트가 닫을 때까지 기다린다.
        if (resultPaths && !IsHostRole())
        {
            yield return WaitOrTimeout(
                () => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient,
                hostResultLingerSeconds + 30f);
        }

        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening)
        {
            Debug.Log("[QA] 사이클 재시작 — 네트워크 종료.");
            nm.Shutdown();
        }

        yield return WaitOrTimeout(
            () => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening,
            10f);
        yield return new WaitForSeconds(1f);
    }

    private static NetworkSessionLauncher GetLauncher()
    {
        NetworkManager nm = NetworkManager.Singleton;
        return nm != null ? nm.GetComponent<NetworkSessionLauncher>() : null;
    }

    /// <summary>cond가 참이 될 때까지 대기. timeout 초과 시 _timedOut=true로 즉시 반환.</summary>
    private IEnumerator WaitOrTimeout(Func<bool> cond, float timeout)
    {
        _timedOut = false;
        float t = 0f;
        while (!cond())
        {
            if (t >= timeout)
            {
                _timedOut = true;
                yield break;
            }
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void OnApplicationQuit()
    {
        // 사용자가 수동으로 플레이를 멈춰도 진행 중 사이클 리포트는 남긴다.
        if (_running && _recorder != null)
            EndCycle("중단: 플레이 종료");
    }

    private void OnDestroy()
    {
        Player.LocalPlayerChanged -= OnLocalPlayerChanged;
        if (_running && _recorder != null)
            EndCycle("중단: 오브젝트 파괴");
    }
}
