using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 할 일 48 시나리오 러너(PLAN-48-qa-automation Q1) — 시나리오 id 목록을 순서대로 돌리고 항목별 PASS/FAIL/눈확인을 리포트로 남긴다.
/// 접속은 DemoSessionDirector(설정 파일 자동 접속)에 맡기고 QA 는 IP·Ready 버튼을 누르지 않는다.
/// 기존 탐지기 5종은 실행 내내 상시 감시한다. 리포트 = QAReportWriter.ScenarioRunDir("48") / report.md.
/// MPPM 은 인스턴스마다 이 러너가 돌고(역할 = 호스트/클라), 리포트도 인스턴스마다 따로 쓴다.
/// </summary>
public sealed class QAScenarioRunner : MonoBehaviour
{
    private readonly List<IQADetector> _detectors = new List<IQADetector>();
    private readonly QAScenarioContext _ctx = new QAScenarioContext();
    private string[] _ids = Array.Empty<string>();
    private bool _running;
    private bool _written;
    private string _status = "진행 중";

    public void Configure(string scenarioIds, int expectedPlayers, string endPathOrder)
    {
        var ids = new List<string>();
        foreach (string token in (scenarioIds ?? string.Empty).Split(','))
        {
            string id = token.Trim().ToUpperInvariant();
            if (id.Length > 0)
                ids.Add(id);
        }
        _ids = ids.ToArray();
        _ctx.ExpectedPlayers = Mathf.Max(1, expectedPlayers);

        var paths = new List<QAResultPath>();
        foreach (string token in (endPathOrder ?? string.Empty).Split(','))
            if (Enum.TryParse(token.Trim(), true, out QAResultPath p))
                paths.Add(p);
        if (paths.Count > 0)
            _ctx.EndPaths = paths.ToArray();
    }

    private void Start()
    {
        _ctx.Input = GetComponent<QAInputController>();
        _ctx.Role = DetectRole();
        _ctx.Recorder = new QARecorder { SessionStartTime = Time.time };
        _ctx.RunDir = QAReportWriter.ScenarioRunDir("48", _ctx.Role == "host" ? null : _ctx.Role);
        _ctx.SceneLog.Add(SceneManager.GetActiveScene().name);

        _detectors.Add(new LogErrorDetector());
        _detectors.Add(new SoftlockDetector());
        _detectors.Add(new PerfDetector());
        _detectors.Add(new NetworkDesyncDetector());
        _detectors.Add(new GameplayInvariantDetector());
        QABlackboard.Reset();
        foreach (IQADetector d in _detectors)
            d.OnSessionStart(_ctx.Recorder);

        SceneManager.sceneLoaded += HandleSceneLoaded;
        Player.LocalPlayerChanged += HandleLocalPlayerChanged;
        _running = true;

        Debug.Log($"[QA] 48 시나리오 시작 — 역할={_ctx.Role}, {_ctx.ExpectedPlayers}인, 시나리오={string.Join(",", _ids)}, 리포트={_ctx.RunDir}");
        StartCoroutine(RunAll());
    }

    private void Update()
    {
        if (!_running)
            return;
        float dt = Time.deltaTime;
        for (int i = 0; i < _detectors.Count; i++)
            _detectors[i].Tick(_ctx.Recorder, dt);
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single || _ctx.SceneLog.Count == 0 || _ctx.SceneLog[_ctx.SceneLog.Count - 1] != scene.name)
            _ctx.SceneLog.Add(scene.name);
    }

    private void HandleLocalPlayerChanged(Player player)
    {
        _ctx.Input?.SetTarget(player);
    }

    private IEnumerator RunAll()
    {
        foreach (string id in _ids)
        {
            _ctx.Current = id;
            int before = _ctx.Items.Count;
            bool done = false;
            float limit = QALobbyScenarios.TimeLimit(id);
            Debug.Log($"[QA] ── 시나리오 {id} 시작(상한 {limit:F0}s) ──");

            Coroutine co = StartCoroutine(Guard(QALobbyScenarios.Run(id, _ctx), () => done = true));
            float start = Time.realtimeSinceStartup;
            while (!done && Time.realtimeSinceStartup - start < limit)
                yield return null;

            if (!done)
            {
                StopCoroutine(co);
                _ctx.Input?.SetMove(Vector3.zero);
                QABlackboard.ScriptedControl = false;
                _ctx.Fail("시나리오 상한·예외 중단", $"{limit:F0}s 안에 끝나지 않음(또는 시나리오 코드 예외 — errors.log 확인). scene={QAUtil.ActiveSceneName()}", _ctx.Shot("abort"));
            }
            else if (_ctx.Items.Count == before)
            {
                _ctx.Eye("판정 항목 없음", "시나리오가 항목을 남기지 않고 끝났다");
            }

            WriteReport(); // 중간 저장 — 도중에 Play 를 멈춰도 여기까지는 남는다
        }

        _status = "완료";
        Finish();
        Debug.Log($"[QA] 48 시나리오 전부 끝 — 리포트: {_ctx.RunDir}/report.md . Tools/QA/Stop Active Scenario 로 정지할 것.");
    }

    private static IEnumerator Guard(IEnumerator body, Action onDone)
    {
        yield return body;
        onDone();
    }

    private void Finish()
    {
        if (!_running)
            return;
        _running = false;
        _ctx.Input?.SetMove(Vector3.zero);
        foreach (IQADetector d in _detectors)
            d.OnSessionEnd(_ctx.Recorder);
        WriteReport();
        _written = true;
    }

    private void WriteReport()
    {
        QAReportWriter.WriteScenarioReport(_ctx, _ids, _status);
    }

    private void OnApplicationQuit()
    {
        if (!_written)
        {
            _status = "중단: Play 정지";
            Finish();
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        Player.LocalPlayerChanged -= HandleLocalPlayerChanged;
        if (!_written)
        {
            _status = "중단: 하네스 파괴";
            Finish();
        }
    }

    /// <summary>"host" 또는 MPPM 가상 플레이어 폴더 이름(client-mppmXXXX).</summary>
    private static string DetectRole()
    {
#if UNITY_EDITOR
        if (Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
            return "host";
        Match m = Regex.Match(Application.dataPath, "mppm[0-9a-fA-F]+");
        return "client-" + (m.Success ? m.Value : "virtual");
#else
        return "build";
#endif
    }
}
