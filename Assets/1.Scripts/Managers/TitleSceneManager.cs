using UnityEngine;

/// <summary>
/// 타이틀 씬 전환·페이드. 입력과 연출은 전부 <see cref="TitleFlowDirector"/> 가 쥐고, 여기는 <see cref="StartGame"/> 하나만 연다.
/// </summary>
/// <remarks>
/// 10-26(PLAN-title-start-settings-tabs) 타이틀의 시작·설정·나가기 버튼과 ESC 옵션창 토글을 지웠다 —
/// 화면엔 Press 캡슐만 있고, 설정·종료는 로비 ESC 메뉴(<see cref="GameMenuController"/>)에서 한다.
/// </remarks>
public class TitleSceneManager : NemoSceneManager
{
    private GameManager _gameManager;

    protected override void Awake()
    {
        base.Awake();
        Debug.Log("[SceneFlow] TitleSceneManager.Awake");
        _gameManager = GetGameManager();
    }

    private void Start()
    {
        Debug.Log("[SceneFlow] TitleSceneManager.Start");
        PlayEnterFade();

        // BGM 재생
        AudioManager.Instance.PlayBGM("BGM_Title");
    }

    /// <summary>검정 페이드 → 로비(부스 데모면 대기 씬). 로비는 진입 시 페이드 인.</summary>
    public void StartGame()
    {
        Debug.Log($"[SceneFlow] TitleSceneManager.StartGame transitioning={IsTransitioning} hasGameManager={_gameManager != null}");
        if (IsTransitioning || _gameManager == null)
        {
            return;
        }

        StartCoroutine(FadeThenInvoke(GoToLobbyOrBeginDemo));
    }

    /// <summary>
    /// 부스 데모 흐름이면 여기서 대기 씬 → 호스팅/자동 접속을 시작하고(할 일 70 — 부팅하면 호스트·클라 모두 타이틀을 거친다),
    /// 아니면 종전처럼 로비로.
    /// </summary>
    private void GoToLobbyOrBeginDemo()
    {
        if (DemoSessionDirector.TryHandleTitleStart())
        {
            return;
        }

        _gameManager.GoToLobby();
    }
}
