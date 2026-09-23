using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class TitleSceneManager : NemoSceneManager
{
    [Header("Buttons")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button optionButton;
    [SerializeField] private Button exitButton;

    [Header("Option")]
    [SerializeField] private GameObject optionPanel;

    [Header("Flow")]
    [Tooltip("연출 디렉터가 있으면 ESC와 버튼 진입점은 전부 그쪽이 쥔다. " +
             "이 매니저는 씬 전환과 페이드만 담당한다. 비워 두면 씬에서 자동으로 찾는다.")]
    [SerializeField] private TitleFlowDirector flowDirector;

    private GameManager _gameManager;

    /// <summary>연출 디렉터가 입력·버튼을 쥐고 있는가.</summary>
    private bool HasFlowDirector => flowDirector != null;

    protected override void Awake()
    {
        base.Awake();
        Debug.Log("[SceneFlow] TitleSceneManager.Awake");
        _gameManager = GetGameManager();
        ResolveSceneReferences();

        // 디렉터가 있으면 버튼 결선도 그쪽이 한다. 여기서 또 붙이면 한 번 눌러 두 번 실행된다.
        if (!HasFlowDirector)
        {
            BindButtons();
        }
    }

    private void Start()
    {
        Debug.Log("[SceneFlow] TitleSceneManager.Start");

        // 🔴 디렉터가 있으면 설정창 표시는 월드 캔버스를 켜고 끄는 방식이다.
        // 여기서 Option_Panel 자체를 꺼 버리면 캔버스를 켜도 안쪽이 꺼진 채라 영영 안 보인다.
        if (!HasFlowDirector)
        {
            SetOptionPanel(false);
        }

        PlayEnterFade();

        // BGM 재생
        AudioManager.Instance.PlayBGM(AudioManager.Instance.Catalog.TitleBGM);
    }

    private void Update()
    {
        // 🔴 디렉터가 있으면 ESC는 건드리지 않는다.
        // 여기 있는 IsTransitioning은 '페이드 잠금'이지 '카메라 연출 중'이 아니라서,
        // 접근 연출 도중 ESC(스킵)를 누르면 스킵과 동시에 옵션창까지 열렸다.
        if (HasFlowDirector)
        {
            return;
        }

        // ESC로 옵션창 토글 (열려있으면 닫고, 닫혀있으면 연다)
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true && !IsTransitioning)
        {
            ToggleOption();
        }
    }

    public void StartGame()
    {
        Debug.Log($"[SceneFlow] TitleSceneManager.StartGame transitioning={IsTransitioning} hasGameManager={_gameManager != null}");
        if (IsTransitioning || _gameManager == null)
        {
            return;
        }

        SetTitleButtonsInteractable(false);
        StartCoroutine(FadeThenInvoke(_gameManager.GoToLobby));
    }

    /// <summary>
    /// 🔴 화면이 이미 검은 상태(타이틀 CRT 꺼짐 연출 뒤)에서 부른다 — 기존 <see cref="StartGame"/> 은 1.5초 페이드를
    /// 한 번 더 기다려 전환이 늘어진다(Codex 검토 09-23). 페이드 없이 바로 로비로.
    /// </summary>
    public void StartGameImmediate()
    {
        Debug.Log($"[SceneFlow] TitleSceneManager.StartGameImmediate transitioning={IsTransitioning}");
        if (IsTransitioning || _gameManager == null)
        {
            return;
        }

        SetTitleButtonsInteractable(false);
        _gameManager.GoToLobby();
    }

    /// <summary>검은 화면에서 페이드 없이 종료(<see cref="StartGameImmediate"/> 와 같은 이유).</summary>
    public void ExitGameImmediate()
    {
        Debug.Log($"[SceneFlow] TitleSceneManager.ExitGameImmediate transitioning={IsTransitioning}");
        if (IsTransitioning)
        {
            return;
        }

        SetTitleButtonsInteractable(false);
        QuitApplication();
    }

    public void ToggleOption()
    {
        SetOptionPanel(optionPanel == null || !optionPanel.activeSelf);
    }

    public void OpenOption()
    {
        SetOptionPanel(true);
    }

    public void CloseOption()
    {
        SetOptionPanel(false);
    }

    public void ExitGame()
    {
        Debug.Log($"[SceneFlow] TitleSceneManager.ExitGame transitioning={IsTransitioning}");
        if (IsTransitioning)
        {
            return;
        }

        SetTitleButtonsInteractable(false);
        StartCoroutine(FadeThenInvoke(QuitApplication));
    }

    private void QuitApplication()
    {
        Debug.Log("[SceneFlow] TitleSceneManager.QuitApplication");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void ResolveSceneReferences()
    {
        if (flowDirector == null)
        {
            flowDirector = FindAnyObjectByType<TitleFlowDirector>(FindObjectsInactive.Include);
        }

        startButton ??= FindButton("Start_Button");
        optionButton ??= FindButton("Option_Button");
        exitButton ??= FindButton("Exit_Button");

        if (optionPanel == null)
        {
            optionPanel = FindInActiveScene("Option_Panel");
        }

        WarnMissingReferences();
    }

    private void BindButtons()
    {
        if (startButton != null)
        {
            startButton.onClick.RemoveListener(StartGame);
            startButton.onClick.AddListener(StartGame);
        }

        if (optionButton != null)
        {
            optionButton.onClick.RemoveListener(OpenOption);
            optionButton.onClick.AddListener(OpenOption);
        }

        if (exitButton != null)
        {
            exitButton.onClick.RemoveListener(ExitGame);
            exitButton.onClick.AddListener(ExitGame);
        }
    }

    private void SetOptionPanel(bool active)
    {
        if (optionPanel != null)
        {
            optionPanel.SetActive(active);
        }
    }

    private void SetTitleButtonsInteractable(bool interactable)
    {
        SetButtonsInteractable(interactable, startButton, optionButton, exitButton);
    }

    private void WarnMissingReferences()
    {
        if (startButton == null)
        {
            WarnMissingReference(nameof(startButton));
        }

        if (optionButton == null)
        {
            WarnMissingReference(nameof(optionButton));
        }

        if (exitButton == null)
        {
            WarnMissingReference(nameof(exitButton));
        }

        if (optionPanel == null)
        {
            WarnMissingReference(nameof(optionPanel));
        }
    }
}
