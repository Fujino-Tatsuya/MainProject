using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class NetworkLoadingScreenView : MonoBehaviour
{
    private static NetworkLoadingScreenView _active;

    [SerializeField] private Image progressFill;
    [SerializeField] private Image centerImage;
    [SerializeField] private TMP_Text tooltipText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text percentText;
    [SerializeField] private string[] tooltipTable =
    {
        "Loading game data...",
        "Synchronizing players...",
        "Preparing the session..."
    };
    [SerializeField] private Sprite[] imageTable;
    [SerializeField] private string[] readyMessages =
    {
        "Enter...",
        "All players are ready.",
        "Starting..."
    };
    [SerializeField] private float tooltipInterval = 3f;
    [SerializeField] private float imageInterval = 5f;

    private NetworkLoadingPhase _phase;
    private float _progress;
    private int _tooltipIndex;
    private int _imageIndex;
    private Coroutine _tableRoutine;
    private Coroutine _registerRoutine;

    private void Awake()
    {
        if (_active != null && _active != this)
        {
            Destroy(gameObject);
            return;
        }

        _active = this;
        DontDestroyOnLoad(gameObject);
        ApplyProgress(0f);
        SetPhase(NetworkLoadingPhase.LoadingScene);
    }

    private void OnEnable()
    {
        _tableRoutine = StartCoroutine(CycleTables());
        _registerRoutine = StartCoroutine(RegisterWhenNetworkManagerExists());
    }

    private void OnDisable()
    {
        if (_tableRoutine != null)
        {
            StopCoroutine(_tableRoutine);
            _tableRoutine = null;
        }

        if (_registerRoutine != null)
        {
            StopCoroutine(_registerRoutine);
            _registerRoutine = null;
        }
    }

    private void OnDestroy()
    {
        if (_active == this)
        {
            _active = null;
        }
    }

    public void SetProgress(float progress)
    {
        _progress = Mathf.Clamp01(progress);
        ApplyProgress(_progress);
    }

    public void SetPhase(NetworkLoadingPhase phase)
    {
        _phase = phase;
        ApplyPhaseText();
    }

    public void CompleteAndDestroy()
    {
        SetPhase(NetworkLoadingPhase.Completed);
        Destroy(gameObject);
    }

    private IEnumerator RegisterWhenNetworkManagerExists()
    {
        while (true)
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager != null)
            {
                var controller = networkManager.GetComponent<NetworkLoadingFlowController>();
                if (controller != null)
                {
                    controller.RegisterView(this);
                    yield break;
                }
            }

            yield return null;
        }
    }

    private IEnumerator CycleTables()
    {
        float tooltipTimer = 0f;
        float imageTimer = 0f;

        ApplyTooltip();
        ApplyImage();

        while (true)
        {
            tooltipTimer += Time.unscaledDeltaTime;
            imageTimer += Time.unscaledDeltaTime;

            if (tooltipTimer >= Mathf.Max(0.1f, tooltipInterval))
            {
                tooltipTimer = 0f;
                AdvanceTooltip();
            }

            if (imageTimer >= Mathf.Max(0.1f, imageInterval))
            {
                imageTimer = 0f;
                AdvanceImage();
            }

            if (_phase == NetworkLoadingPhase.Ready)
            {
                ApplyPhaseText();
            }

            yield return null;
        }
    }

    private void AdvanceTooltip()
    {
        if (tooltipTable == null || tooltipTable.Length == 0)
        {
            return;
        }

        _tooltipIndex = (_tooltipIndex + 1) % tooltipTable.Length;
        ApplyTooltip();
    }

    private void AdvanceImage()
    {
        if (imageTable == null || imageTable.Length == 0)
        {
            return;
        }

        _imageIndex = (_imageIndex + 1) % imageTable.Length;
        ApplyImage();
    }

    private void ApplyProgress(float progress)
    {
        if (progressFill != null)
        {
            progressFill.fillAmount = progress;
        }

        if (percentText != null)
        {
            percentText.text = Mathf.RoundToInt(progress * 100f) + "%";
        }
    }

    private void ApplyPhaseText()
    {
        if (statusText == null)
        {
            return;
        }

        switch (_phase)
        {
            case NetworkLoadingPhase.LoadingScene:
                statusText.text = "Preparing...";
                break;
            case NetworkLoadingPhase.LoadingGame:
                statusText.text = "Loading...";
                break;
            case NetworkLoadingPhase.WaitingForPlayers:
                statusText.text = "Waiting for players...";
                break;
            case NetworkLoadingPhase.Ready:
                statusText.text = GetReadyMessage();
                break;
            case NetworkLoadingPhase.Activating:
                statusText.text = "Starting...";
                break;
            case NetworkLoadingPhase.Completed:
                statusText.text = string.Empty;
                break;
            default:
                statusText.text = "Stand by...";
                break;
        }
    }

    private string GetReadyMessage()
    {
        if (readyMessages == null || readyMessages.Length == 0)
        {
            return "Enter...";
        }

        var index = Mathf.FloorToInt(Time.unscaledTime * 2f) % readyMessages.Length;
        return readyMessages[index];
    }

    private void ApplyTooltip()
    {
        if (tooltipText == null)
        {
            return;
        }

        if (tooltipTable == null || tooltipTable.Length == 0)
        {
            tooltipText.text = string.Empty;
            return;
        }

        tooltipText.text = tooltipTable[Mathf.Clamp(_tooltipIndex, 0, tooltipTable.Length - 1)];
    }

    private void ApplyImage()
    {
        if (centerImage == null || imageTable == null || imageTable.Length == 0)
        {
            return;
        }

        centerImage.sprite = imageTable[Mathf.Clamp(_imageIndex, 0, imageTable.Length - 1)];
        centerImage.preserveAspect = true;
    }

    public void SetEditorReferences(Image fill, Image center, TMP_Text tooltip, TMP_Text status, TMP_Text percent)
    {
        progressFill = fill;
        centerImage = center;
        tooltipText = tooltip;
        statusText = status;
        percentText = percent;
    }
}
