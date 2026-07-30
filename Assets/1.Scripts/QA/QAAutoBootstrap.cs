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
/// 메뉴 Tools ▸ QA ▸ Auto-run from BootStrap 으로 켜고 끌 수 있다(기본 켜짐).
/// </summary>
public static class QAAutoBootstrap
{
    private const string PrefKey = "QA.AutoRunFromBootstrap";
    private const string MenuPath = "Tools/QA/Auto-run from BootStrap";
    private const string BootStrapScene = "0.BootStrapScene";

    private static bool _spawnedThisSession;

    private static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBootstrap()
    {
        if (_spawnedThisSession || !Enabled)
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

        go.AddComponent<QASessionController>();

        go.SetActive(true);
        Debug.Log("[QA] Harness 생성 완료(0.BootStrapScene 자동 부트스트랩).");
    }

    [MenuItem(MenuPath)]
    private static void ToggleEnabled()
    {
        Enabled = !Enabled;
    }

    [MenuItem(MenuPath, validate = true)]
    private static bool ToggleEnabledValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }
}
#endif
