#if UNITY_EDITOR
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 에디터 전용 QA 자동 부트스트랩. 0.BootStrapScene에서 Play를 누르면 QA 하네스를
/// 코드로 생성해 붙인다(씬/프리팹 파일 무수정). 빌드에는 포함되지 않는다(#if UNITY_EDITOR).
/// Play 시간·반복 횟수·자동실행 토글은 Tools ▸ QA ▸ Settings(=QASettings)에서 조정한다.
/// </summary>
public static class QAAutoBootstrap
{
    private const string BootStrapScene = "0.BootStrapScene";

    private static bool _spawnedThisSession;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBootstrap()
    {
        if (_spawnedThisSession || !QASettings.AutoRun)
            return;
        if (SceneManager.GetActiveScene().name != BootStrapScene)
            return;

        _spawnedThisSession = true;
        CreateHarness();
    }

    private static void CreateHarness()
    {
        var go = new GameObject("[QA] Harness");
        go.SetActive(false);                 // 구성 완료 후 활성화(에이전트 초기화 순서 보장)
        Object.DontDestroyOnLoad(go);

        var bp = go.AddComponent<BehaviorParameters>();
        bp.BehaviorName = "GameQAAgent";
        bp.BehaviorType = BehaviorType.HeuristicOnly;   // Python 트레이너 없이 Heuristic()으로 동작
        bp.BrainParameters.VectorObservationSize = QAAgent.ObservationSize;
        bp.BrainParameters.NumStackedVectorObservations = 1;
        bp.BrainParameters.ActionSpec = new ActionSpec(
            QAAgent.ContinuousActions, (int[])QAAgent.DiscreteBranches.Clone());

        go.AddComponent<QAInputController>();
        go.AddComponent<QAAgent>();

        var dr = go.AddComponent<DecisionRequester>();
        dr.DecisionPeriod = 10;                 // ~0.2s마다 결정(과도한 스킬 스팸 방지)
        dr.TakeActionsBetweenDecisions = false; // 이동 지속은 QAInputController.FixedUpdate가 담당

        var session = go.AddComponent<QASessionController>();
        session.Configure(QASettings.Duration, QASettings.RepeatCount, QASettings.PlayerCount);

        go.SetActive(true);
        Debug.Log($"[QA] Harness 생성 완료 — {QASettings.PlayerCount}인, 사이클당 {QASettings.Duration:F0}초, " +
                  $"{(QASettings.RepeatCount <= 0 ? "무한" : QASettings.RepeatCount + "회")} 반복.");
    }
}
#endif
