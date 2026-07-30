using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

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
    [SerializeField] private string lobbyScene = "3.BeaverLobby";

    [Header("세션")]
    [Tooltip("맵 도달 후 각 사이클을 돌릴 시간(초).")]
    [SerializeField] private float sessionDuration = 180f;
    [Tooltip("QA 전체 사이클 반복 횟수. 0 이하이면 무한 반복.")]
    [SerializeField] private int repeatCount = 1;
    [Tooltip("각 진행 단계가 이 시간 내 완료되지 않으면 흐름 타임아웃(소프트락)으로 기록.")]
    [SerializeField] private float phaseTimeout = 45f;
    [Tooltip("스폰(로딩 포함) 대기 타임아웃(초).")]
    [SerializeField] private float spawnTimeout = 90f;

    private readonly List<IQADetector> _detectors = new List<IQADetector>();
    private QARecorder _recorder;
    private QAInputController _input;
    private bool _running;
    private bool _timedOut;
    private int _iteration;
    private string _cycleSummary = "중단";

    /// <summary>부트스트랩이 활성화 전에 EditorPrefs 값으로 주입한다.</summary>
    public void Configure(float sessionDurationSeconds, int repeats)
    {
        sessionDuration = Mathf.Max(5f, sessionDurationSeconds);
        repeatCount = repeats;
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
        Debug.Log($"[QA] 세션 시작 — 사이클당 {sessionDuration:F0}초, {plan}. 부팅부터 자동 진행합니다.");
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

        Debug.Log($"[QA] 전체 QA 반복 완료 ({_iteration}회).");
    }

    private IEnumerator RunCycle(bool first)
    {
        _cycleSummary = "중단";

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

        // 3) 호스트 단독 세션 시작.
        NetworkSessionLauncher launcher = GetLauncher();
        Debug.Log("[QA] → StartHost");
        launcher.StartHost();
        yield return WaitOrTimeout(
            () => NetworkManager.Singleton != null &&
                  NetworkManager.Singleton.IsListening &&
                  NetworkManager.Singleton.IsHost,
            phaseTimeout);
        if (AbortCycle("호스트 시작")) yield break;

        yield return new WaitForSeconds(0.5f);

        // 4) 게임 로딩(→ 4.MapScene) + 플레이어 스폰.
        Debug.Log("[QA] → StartGameLoading");
        launcher.StartGameLoading();
        yield return WaitOrTimeout(() => Player.LocalPlayer != null, spawnTimeout);
        if (AbortCycle("맵 로딩·플레이어 스폰")) yield break;

        // 5) 플레이 세션.
        Debug.Log($"[QA] 전투 씬 도달 — {sessionDuration:F0}초 QA 세션 진행.");
        float endTime = Time.time + sessionDuration;
        yield return WaitOrTimeout(() => Time.time >= endTime, sessionDuration + 5f);

        _cycleSummary = $"정상 종료: {sessionDuration:F0}초 세션 완료";
    }

    /// <summary>타임아웃이 났으면 Critical로 기록하고 현재 사이클 요약을 설정한다.</summary>
    private bool AbortCycle(string phase)
    {
        if (!_timedOut)
            return false;

        _recorder.Add(QASeverity.Critical, "Flow",
            $"진행 단계 '{phase}'에서 {phaseTimeout:F0}s 내 진척 없음(부트 흐름 소프트락 의심)");
        _cycleSummary = $"흐름 타임아웃: {phase}";
        return true;
    }

    private void BeginCycle()
    {
        QABlackboard.Reset();
        _recorder = new QARecorder { SessionStartTime = Time.time };
        for (int i = 0; i < _detectors.Count; i++)
            _detectors[i].OnSessionStart(_recorder);
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

        string dir = QAReportWriter.Write(_recorder, $"사이클 {_iteration} — {summary}");

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
