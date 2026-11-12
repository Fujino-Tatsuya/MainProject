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
    [Tooltip("단계 상태 문구 최소 표시 시간(초) — 그 사이 지나간 단계는 건너뛴다. 준비(Ready) 문구는 기다리지 않는다(275 후속).")]
    [SerializeField] private float minimumStatusSeconds = 1f;
    [Tooltip("준비(Ready) 문구 순환 간격(초) — 예전 0.5초 고정(275 후속).")]
    [SerializeField] private float readyMessageInterval = 1.5f;

    private NetworkLoadingPhase _phase;
    private bool _hasPhase;
    private float _readyStartedAt;
    private float _statusShownAt = float.NegativeInfinity;
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
        Loc.Changed += Relabel;
    }

    private void OnDisable()
    {
        Loc.Changed -= Relabel;

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
        // 흐름이 진행률마다 같은 단계로 다시 부른다 — 바뀔 때만 처리.
        if (_hasPhase && phase == _phase)
        {
            return;
        }

        _hasPhase = true;
        _phase = phase;
        if (phase == NetworkLoadingPhase.Ready)
        {
            _readyStartedAt = Time.unscaledTime;
        }

        ApplyPhaseText();
    }

    public void CompleteAndDestroy()
    {
        SetPhase(NetworkLoadingPhase.Completed);
        Destroy(gameObject);
    }

    private void Relabel()
    {
        ApplyPhaseText(force: true);
        ApplyTooltip();
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

            // 준비 문구 순환 · 최소 표시 시간에 막혀 미뤄진 단계 문구를 매 프레임 다시 맞춘다(같으면 대입 안 함).
            ApplyPhaseText();

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

    private void ApplyPhaseText(bool force = false)
    {
        if (statusText == null)
        {
            return;
        }

        string next = _phase == NetworkLoadingPhase.Ready ? GetReadyMessage() : PhaseText(_phase);
        if (next == statusText.text)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (!force && !LoadingStatusTextRule.CanSwitch(_phase, now - _statusShownAt, minimumStatusSeconds))
        {
            return;
        }

        statusText.text = next;
        _statusShownAt = now;
    }

    /// <summary>준비(Ready) 밖 단계의 상태 문구 — <c>UI.Loading.*</c>. 완료면 빈 글자.</summary>
    public static string PhaseText(NetworkLoadingPhase phase)
    {
        switch (phase)
        {
            case NetworkLoadingPhase.LoadingScene: return UiStrings.LoadingPreparing.Text;
            case NetworkLoadingPhase.LoadingGame: return UiStrings.LoadingLoading.Text;
            case NetworkLoadingPhase.WaitingForPlayers: return UiStrings.LoadingWaitingForPlayers.Text;
            case NetworkLoadingPhase.Activating: return UiStrings.LoadingActivating.Text;
            case NetworkLoadingPhase.Completed: return string.Empty;
            default: return UiStrings.LoadingStandBy.Text;
        }
    }

    private string GetReadyMessage()
    {
        if (readyMessages == null || readyMessages.Length == 0)
        {
            return UiStrings.LoadingReady1.Text;
        }

        var index = LoadingStatusTextRule.ReadyIndex(Time.unscaledTime - _readyStartedAt, readyMessageInterval, readyMessages.Length);
        return TableText(UiStrings.LoadingReadyMessages, readyMessages, index);
    }

    /// <summary>
    /// 씬 직렬화 문구 표의 index 칸 — 키 목록(<see cref="UiStrings.LoadingTips"/> 등)에 같은 칸이 있으면 그 키(행이 없으면 씬 값),
    /// 키 목록보다 긴 칸은 씬 값 그대로(키 없음).
    /// </summary>
    public static string TableText(System.Collections.Generic.IReadOnlyList<UiString> keys, string[] serialized, int index)
    {
        string authored = serialized[index] ?? string.Empty;
        return index < keys.Count ? keys[index].TextOr(authored) : authored;
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

        tooltipText.text = TableText(UiStrings.LoadingTips, tooltipTable, Mathf.Clamp(_tooltipIndex, 0, tooltipTable.Length - 1));
    }

    private void ApplyImage()
    {
        if (centerImage == null)
        {
            return;
        }

        // 표가 비면 가운데 이미지를 끈다 — sprite 없는 Image 는 흰 사각형으로 그려져 전체 화면 배경(275)을 가린다.
        bool hasImage = imageTable != null && imageTable.Length > 0;
        centerImage.enabled = hasImage;
        if (!hasImage)
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
