using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TitleSceneManager : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField] private string lobbySceneName = "Temp_LobbyScene";

    [Header("Fade")]
    [SerializeField] private Image blackImage;
    [SerializeField] private float fadeDuration = 0.8f;

    [Header("Buttons")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button optionButton;
    [SerializeField] private Button exitButton;

    [Header("Option")]
    [SerializeField] private GameObject optionPanel;

    private bool _isTransitioning;
    private Coroutine _fadeRoutine;

    private void Awake()
    {
        ResolveSceneReferences();
        BindButtons();
    }

    private void Start()
    {
        SetOptionPanel(false);

        if (blackImage != null)
        {
            SetBlackAlpha(1f);
            FadeOut();
        }
    }

    public void StartGame()
    {
        if (_isTransitioning)
        {
            return;
        }

        StartCoroutine(LoadLobbyRoutine());
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
        if (_isTransitioning)
        {
            return;
        }

        StartCoroutine(ExitRoutine());
    }

    public void FadeIn()
    {
        StartFade(1f);
    }

    public void FadeOut()
    {
        StartFade(0f);
    }

    private IEnumerator LoadLobbyRoutine()
    {
        _isTransitioning = true;
        SetButtonsInteractable(false);
        StopActiveFade();
        yield return FadeTo(1f);
        SceneManager.LoadScene(lobbySceneName);
    }

    private IEnumerator ExitRoutine()
    {
        _isTransitioning = true;
        SetButtonsInteractable(false);
        StopActiveFade();
        yield return FadeTo(1f);

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void StartFade(float targetAlpha)
    {
        StopActiveFade();
        _fadeRoutine = StartCoroutine(FadeTo(targetAlpha));
    }

    private void StopActiveFade()
    {
        if (_fadeRoutine == null)
        {
            return;
        }

        StopCoroutine(_fadeRoutine);
        _fadeRoutine = null;
    }

    private IEnumerator FadeTo(float targetAlpha)
    {
        if (blackImage == null)
        {
            yield break;
        }

        blackImage.gameObject.SetActive(true);
        blackImage.raycastTarget = true;

        var startAlpha = blackImage.color.a;
        var elapsed = 0f;
        var duration = Mathf.Max(0.01f, fadeDuration);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            var alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
            SetBlackAlpha(alpha);
            yield return null;
        }

        SetBlackAlpha(targetAlpha);
        blackImage.raycastTarget = targetAlpha > 0f;
        blackImage.gameObject.SetActive(targetAlpha > 0f);
        _fadeRoutine = null;
    }

    private void ResolveSceneReferences()
    {
        if (blackImage == null)
        {
            var black = GameObject.Find("Black_Image");
            blackImage = black != null ? black.GetComponent<Image>() : null;
        }

        startButton ??= FindButton("Start_Button");
        optionButton ??= FindButton("Option_Button");
        exitButton ??= FindButton("Exit_Button");

        if (optionPanel == null)
        {
            optionPanel = GameObject.Find("Option_Panel");
        }
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

    private void SetButtonsInteractable(bool interactable)
    {
        if (startButton != null)
        {
            startButton.interactable = interactable;
        }

        if (optionButton != null)
        {
            optionButton.interactable = interactable;
        }

        if (exitButton != null)
        {
            exitButton.interactable = interactable;
        }
    }

    private void SetBlackAlpha(float alpha)
    {
        if (blackImage == null)
        {
            return;
        }

        var color = blackImage.color;
        color.a = Mathf.Clamp01(alpha);
        blackImage.color = color;
    }

    private static Button FindButton(string objectName)
    {
        var target = GameObject.Find(objectName);
        return target != null ? target.GetComponent<Button>() : null;
    }
}
