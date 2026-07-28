using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public abstract class NemoSceneManager : MonoBehaviour
{
    [Header("Fade")]
    [SerializeField] protected Image blackImage;
    [SerializeField] protected float fadeDuration = 0.8f;

    private Coroutine _fadeRoutine;

    protected bool IsTransitioning { get; private set; }

    protected virtual void Awake()
    {
        Debug.Log($"[SceneFlow] {GetType().Name}.Awake activeScene={SceneManager.GetActiveScene().name}");
        ResolveCommonReferences();
    }

    public void FadeIn()
    {
        Debug.Log($"[SceneFlow] {GetType().Name}.FadeIn activeScene={SceneManager.GetActiveScene().name}");
        StartFade(1f);
    }

    public void FadeOut()
    {
        Debug.Log($"[SceneFlow] {GetType().Name}.FadeOut activeScene={SceneManager.GetActiveScene().name}");
        StartFade(0f);
    }

    protected GameManager GetGameManager()
    {
        var gameManager = GameManager.Instance;

        if (gameManager == null)
        {
            Debug.LogWarning($"{nameof(GameManager)} is missing.");
        }
        else
        {
            Debug.Log($"[SceneFlow] {GetType().Name}.GetGameManager found={gameManager.name}");
        }

        return gameManager;
    }

    protected NetworkManager GetNetworkManager()
    {
        var networkManager = NetworkManager.Singleton;

        if (networkManager == null)
        {
            Debug.LogWarning($"{nameof(NetworkManager)} is missing.");
        }
        else
        {
            Debug.Log(
                $"[SceneFlow] {GetType().Name}.GetNetworkManager found={networkManager.name} " +
                $"listening={networkManager.IsListening} host={networkManager.IsHost} server={networkManager.IsServer} client={networkManager.IsClient}");
        }

        return networkManager;
    }

    protected void PlayEnterFade()
    {
        if (blackImage == null)
        {
            Debug.Log($"[SceneFlow] {GetType().Name}.PlayEnterFade skipped missing blackImage activeScene={SceneManager.GetActiveScene().name}");
            return;
        }

        Debug.Log($"[SceneFlow] {GetType().Name}.PlayEnterFade activeScene={SceneManager.GetActiveScene().name}");
        SetBlackAlpha(1f);
        FadeOut();
    }

    protected IEnumerator TransitionToScene(string sceneName)
    {
        if (IsTransitioning)
        {
            Debug.Log($"[SceneFlow] {GetType().Name}.TransitionToScene ignored already transitioning target={sceneName}");
            yield break;
        }

        Debug.Log($"[SceneFlow] {GetType().Name}.TransitionToScene begin target={sceneName} activeScene={SceneManager.GetActiveScene().name}");
        BeginTransition();
        StopActiveFade();
        yield return FadeTo(1f);

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("Target scene name is empty.");
            EndTransition();
            yield break;
        }

        Debug.Log($"[SceneFlow] {GetType().Name}.TransitionToScene LoadScene target={sceneName}");
        SceneManager.LoadScene(sceneName);
    }

    protected IEnumerator FadeThenInvoke(Action action)
    {
        if (IsTransitioning)
        {
            Debug.Log($"[SceneFlow] {GetType().Name}.FadeThenInvoke ignored already transitioning action={action?.Method.Name}");
            yield break;
        }

        Debug.Log($"[SceneFlow] {GetType().Name}.FadeThenInvoke begin action={action?.Method.Name} activeScene={SceneManager.GetActiveScene().name}");
        BeginTransition();
        StopActiveFade();
        yield return FadeTo(1f);

        if (action == null)
        {
            Debug.LogWarning("Transition action is missing.");
            EndTransition();
            yield break;
        }

        Debug.Log($"[SceneFlow] {GetType().Name}.FadeThenInvoke invoke action={action.Method.Name}");
        action.Invoke();
    }

    protected void SetButtonsInteractable(bool interactable, params Button[] buttons)
    {
        foreach (var button in buttons)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }
    }

    protected static Button FindButton(string objectName)
    {
        var target = FindInActiveScene(objectName);
        return target != null ? target.GetComponent<Button>() : null;
    }

    protected static GameObject FindInActiveScene(string objectName)
    {
        var activeScene = SceneManager.GetActiveScene();
        var rootObjects = activeScene.GetRootGameObjects();

        foreach (var rootObject in rootObjects)
        {
            var target = FindInChildren(rootObject.transform, objectName);
            if (target != null)
            {
                return target.gameObject;
            }
        }

        return null;
    }

    protected void WarnMissingReference(string referenceName)
    {
        Debug.LogWarning($"{GetType().Name} is missing reference: {referenceName}.");
    }

    private void BeginTransition()
    {
        IsTransitioning = true;
        Debug.Log($"[SceneFlow] {GetType().Name}.BeginTransition activeScene={SceneManager.GetActiveScene().name}");
    }

    private void EndTransition()
    {
        IsTransitioning = false;
        Debug.Log($"[SceneFlow] {GetType().Name}.EndTransition activeScene={SceneManager.GetActiveScene().name}");
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
            Debug.Log($"[SceneFlow] {GetType().Name}.FadeTo skipped missing blackImage targetAlpha={targetAlpha}");
            yield break;
        }

        Debug.Log($"[SceneFlow] {GetType().Name}.FadeTo begin targetAlpha={targetAlpha} duration={fadeDuration}");
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
        Debug.Log($"[SceneFlow] {GetType().Name}.FadeTo end targetAlpha={targetAlpha}");
    }

    private void ResolveCommonReferences()
    {
        if (blackImage != null)
        {
            return;
        }

        var black = FindInActiveScene("Black_Image");
        blackImage = black != null ? black.GetComponent<Image>() : null;
        Debug.Log($"[SceneFlow] {GetType().Name}.ResolveCommonReferences blackImageFound={blackImage != null}");
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

    private static Transform FindInChildren(Transform parent, string objectName)
    {
        if (parent.name == objectName)
        {
            return parent;
        }

        for (var i = 0; i < parent.childCount; i++)
        {
            var target = FindInChildren(parent.GetChild(i), objectName);
            if (target != null)
            {
                return target;
            }
        }

        return null;
    }
}
