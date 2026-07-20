using UnityEngine;
using UnityEngine.UI;

public class MapSceneManager : NemoSceneManager
{
    [Header("Buttons")]
    [SerializeField] private Button resultButton;

    private GameManager _gameManager;

    protected override void Awake()
    {
        base.Awake();
        Debug.Log("[SceneFlow] MapSceneManager.Awake");
        _gameManager = GetGameManager();
        ResolveSceneReferences();
        BindButtons();
    }

    private void Start()
    {
        Debug.Log("[SceneFlow] MapSceneManager.Start");
        PlayEnterFade();
    }

    public void GoToResult()
    {
        Debug.Log($"[SceneFlow] MapSceneManager.GoToResult transitioning={IsTransitioning} hasGameManager={_gameManager != null}");
        if (IsTransitioning || _gameManager == null)
        {
            return;
        }

        SetButtonsInteractable(false, resultButton);
        StartCoroutine(FadeThenInvoke(_gameManager.GoToResult));
    }

    private void ResolveSceneReferences()
    {
        resultButton ??= FindButton("Button_GotoResult");

        if (resultButton == null)
        {
            WarnMissingReference(nameof(resultButton));
        }
    }

    private void BindButtons()
    {
        if (resultButton != null)
        {
            resultButton.onClick.RemoveListener(GoToResult);
            resultButton.onClick.AddListener(GoToResult);
        }
    }
}
