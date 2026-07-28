using UnityEngine;
using UnityEngine.UI;

public class TitleSceneManager : NemoSceneManager
{
    [Header("Buttons")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button optionButton;
    [SerializeField] private Button exitButton;

    [Header("Option")]
    [SerializeField] private GameObject optionPanel;

    private GameManager _gameManager;

    protected override void Awake()
    {
        base.Awake();
        Debug.Log("[SceneFlow] TitleSceneManager.Awake");
        _gameManager = GetGameManager();
        ResolveSceneReferences();
        BindButtons();
    }

    private void Start()
    {
        Debug.Log("[SceneFlow] TitleSceneManager.Start");
        SetOptionPanel(false);
        PlayEnterFade();
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
