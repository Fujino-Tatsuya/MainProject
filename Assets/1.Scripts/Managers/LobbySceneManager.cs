using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 걷는 로비(3.LobbyScene)의 씬 연출 — 진입 페이드·BGM·직전 판 결과 패널(PLAN-48 C9 슬림화).
/// 연결은 DemoSessionDirector(설정 파일 자동 접속), 로비 상태는 <see cref="LobbySession"/>,
/// 플레이어 스폰은 <see cref="LobbyPlayerSpawner"/> 가 맡는다. 옛 IP/Port·StartHost/StartClient·Relay·GameStart·Ready·캐릭터 버튼은 씬째 지웠다.
/// </summary>
public class LobbySceneManager : NemoSceneManager
{
    // 직전 판 결과 — 4.Map 이 끝나고 로비에 들어왔을 때만 자동으로 연다(PLAN-48 C7b, Docs/tech/lobby-result-panel.md).
    // 첫 부팅·재접속은 SessionResult 가 비어 있어 열리지 않는다(비우기 = SessionStatsTracker.Awake · DemoSessionDirector.CleanupAfterDrop).
    [Header("Result")]
    [SerializeField] private LobbyResultPanel resultPanel;
    [SerializeField] private Button showLastResultButton;

    protected override void Awake()
    {
        base.Awake();
        Debug.Log("[SceneFlow] LobbySceneManager.Awake");
        ResolveSceneReferences();

        if (showLastResultButton != null)
        {
            showLastResultButton.onClick.RemoveAllListeners();
            showLastResultButton.onClick.AddListener(ShowLastResult);
        }
    }

    private void Start()
    {
        Debug.Log("[SceneFlow] LobbySceneManager.Start");
        PlayEnterFade();
        ShowLastResultOnEnter();

        // BGM 재생
        AudioManager.Instance.PlayBGM("BGM_Lobby");
    }

    /// <summary>다시 보기 버튼 — 결과는 로비에 있는 동안 지우지 않으므로 몇 번이든 다시 연다.</summary>
    public void ShowLastResult()
    {
        if (resultPanel != null)
        {
            resultPanel.Show();
        }
    }

    // 클라는 결과 페이로드(MapSceneManager.HandleGoToResultMessage → SessionResult.Capture)가 로비 로드보다 먼저 와 있다.
    // 결과는 로비에 있는 동안 바뀌지 않으므로(다음 판 Map 진입·끊김 정리 때만 비움) 버튼 표시는 진입 때 한 번 정한다.
    private void ShowLastResultOnEnter()
    {
        var hasResult = resultPanel != null && resultPanel.HasResult;
        Debug.Log($"[SceneFlow] LobbySceneManager.ShowLastResultOnEnter hasPanel={resultPanel != null} hasResult={hasResult}");

        if (showLastResultButton != null)
        {
            showLastResultButton.gameObject.SetActive(hasResult);
            var label = showLastResultButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = UiStrings.LobbyShowLastResult.Text;
            }
        }

        if (hasResult)
        {
            resultPanel.Show();
        }
    }

    private void ResolveSceneReferences()
    {
        showLastResultButton ??= FindButton("Button_ShowLastResult");
        resultPanel ??= FindFirstObjectByType<LobbyResultPanel>();

        if (resultPanel == null)
        {
            WarnMissingReference(nameof(resultPanel));
        }

        if (showLastResultButton == null)
        {
            WarnMissingReference(nameof(showLastResultButton));
        }
    }
}
