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
/// </summary>
public sealed class QASessionController : MonoBehaviour
{
    [Header("자동 진행 씬 이름(빌드 세팅 기준)")]
    [SerializeField] private string titleScene = "1.TitleScene";
    [SerializeField] private string lobbyScene = "3.BeaverLobby";

    [Header("세션")]
    [Tooltip("플레이어 스폰 후 QA를 돌릴 시간(초).")]
    [SerializeField] private float sessionDuration = 180f;
    [Tooltip("각 진행 단계가 이 시간 내 완료되지 않으면 흐름 타임아웃(소프트락)으로 기록.")]
    [SerializeField] private float phaseTimeout = 45f;
    [Tooltip("스폰(로딩 포함) 대기 타임아웃(초).")]
    [SerializeField] private float spawnTimeout = 90f;

    private readonly List<IQADetector> _detectors = new List<IQADetector>();
    private QARecorder _recorder;
    private QAInputController _input;
    private bool _running;
    private bool _ended;
    private bool _timedOut;

    private void Start()
    {
        QABlackboard.Reset();
        _input = GetComponent<QAInputController>();

        _recorder = new QARecorder { SessionStartTime = Time.time };
        _detectors.Add(new LogErrorDetector());
        _detectors.Add(new SoftlockDetector());
        _detectors.Add(new PerfDetector());
        _detectors.Add(new NetworkDesyncDetector());
        foreach (IQADetector d in _detectors)
            d.OnSessionStart(_recorder);

        _running = true;
        Player.LocalPlayerChanged += OnLocalPlayerChanged;

        Debug.Log("[QA] 세션 시작 — 부팅부터 자동 진행합니다.");
        StartCoroutine(DriveFlow());
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

    private IEnumerator DriveFlow()
    {
        // 1) 부팅 완료 대기(GameManager 준비 + 타이틀 씬 로드).
        yield return WaitOrTimeout(() => GameManager.Instance != null, phaseTimeout);
        if (Aborted("부팅: GameManager 미준비")) yield break;

        yield return WaitOrTimeout(() => QAUtil.ActiveSceneName() == titleScene, phaseTimeout);
        if (Aborted("타이틀 씬 진입")) yield break;

        // 2) 로비로.
        Debug.Log("[QA] → GoToLobby");
        GameManager.Instance.GoToLobby();
        yield return WaitOrTimeout(
            () => QAUtil.ActiveSceneName() == lobbyScene && GetLauncher() != null,
            phaseTimeout);
        if (Aborted("로비 씬 진입")) yield break;

        // 3) 호스트 단독 세션 시작.
        NetworkSessionLauncher launcher = GetLauncher();
        Debug.Log("[QA] → StartHost");
        launcher.StartHost();
        yield return WaitOrTimeout(
            () => NetworkManager.Singleton != null &&
                  NetworkManager.Singleton.IsListening &&
                  NetworkManager.Singleton.IsHost,
            phaseTimeout);
        if (Aborted("호스트 시작")) yield break;

        // 로비 ready 동기화가 한두 프레임 뒤에 붙으므로 잠깐 대기.
        yield return new WaitForSeconds(0.5f);

        // 4) 게임 로딩(→ 4.MapScene) + 플레이어 스폰.
        Debug.Log("[QA] → StartGameLoading");
        launcher.StartGameLoading();
        yield return WaitOrTimeout(() => Player.LocalPlayer != null, spawnTimeout);
        if (Aborted("맵 로딩·플레이어 스폰")) yield break;

        // 5) 플레이 세션.
        Debug.Log($"[QA] 전투 씬 도달 — {sessionDuration:F0}초 QA 세션 진행.");
        float endTime = Time.time + sessionDuration;
        yield return WaitOrTimeout(() => Time.time >= endTime, sessionDuration + 5f);

        EndSession($"정상 종료: {sessionDuration:F0}초 세션 완료");
    }

    /// <summary>타임아웃이 났으면 Critical로 기록하고 세션을 마감한다.</summary>
    private bool Aborted(string phase)
    {
        if (!_timedOut)
            return false;

        _recorder.Add(QASeverity.Critical, "Flow",
            $"진행 단계 '{phase}'에서 {phaseTimeout:F0}s 내 진척 없음(부트 흐름 소프트락 의심)");
        EndSession($"흐름 타임아웃: {phase}");
        return true;
    }

    private void EndSession(string summary)
    {
        if (_ended)
            return;
        _ended = true;
        _running = false;
        QABlackboard.Controlling = false;

        for (int i = 0; i < _detectors.Count; i++)
            _detectors[i].OnSessionEnd(_recorder);

        string dir = QAReportWriter.Write(_recorder, summary);

        // Critical이 있으면 종료 시점 스크린샷 1장(best-effort).
        if (dir != null && _recorder.CountBySeverity(QASeverity.Critical) > 0)
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "end.png"));

        Debug.Log($"[QA] 세션 종료: {summary} (Critical {_recorder.CountBySeverity(QASeverity.Critical)}, " +
                  $"Error {_recorder.CountBySeverity(QASeverity.Error)}, Warning {_recorder.CountBySeverity(QASeverity.Warning)})");
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
        // 사용자가 수동으로 플레이를 멈춰도 리포트는 남긴다.
        if (!_ended && _recorder != null)
            EndSession("중단: 플레이 종료");
    }

    private void OnDestroy()
    {
        Player.LocalPlayerChanged -= OnLocalPlayerChanged;
        if (!_ended && _recorder != null)
            EndSession("중단: 오브젝트 파괴");
    }
}
