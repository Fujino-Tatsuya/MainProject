using UnityEngine;
using UnityEngine.UI;

public class ResultSceneManager : NemoSceneManager
{
    [Header("Buttons")]
    [SerializeField] private Button lobbyButton;

    private GameManager _gameManager;

    protected override void Awake()
    {
        base.Awake();
        Debug.Log("[SceneFlow] ResultSceneManager.Awake");
        _gameManager = GetGameManager();
        ResolveSceneReferences();
        BindButtons();
    }

    private void Start()
    {
        Debug.Log("[SceneFlow] ResultSceneManager.Start");
        PlayEnterFade();
    }

    public void GoToLobby()
    {
        Debug.Log($"[SceneFlow] ResultSceneManager.GoToLobby transitioning={IsTransitioning} hasGameManager={_gameManager != null}");
        if (IsTransitioning || _gameManager == null)
        {
            return;
        }

        SetButtonsInteractable(false, lobbyButton);
        StartCoroutine(FadeThenInvoke(_gameManager.GoToLobby));
    }

    private void ResolveSceneReferences()
    {
        lobbyButton ??= FindButton("Button_GotoLobby");

        if (lobbyButton == null)
        {
            WarnMissingReference(nameof(lobbyButton));
        }
    }

    private void BindButtons()
    {
        if (lobbyButton != null)
        {
            lobbyButton.onClick.RemoveListener(GoToLobby);
            lobbyButton.onClick.AddListener(GoToLobby);
        }
    }
}
