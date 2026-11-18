using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [전 피어·표시 전용] 간파 훈련 봇 머리 우상단 "우클릭!" 표시(할 일 303) — 그 봇의 간파 창이 열린 동안, 그리고 T32·T34 에서 넉백을 넣을 차례에 마우스 아이콘의 오른쪽 버튼이 빨갛게 빛나고 문구가 뜬다.
/// 판정 입력은 복제값만 읽는다(<see cref="IInterruptWindowReplica.ReplicatedWindowOpen"/>·봇 단계·봇 반응 중·진행기 현재 행) — 규칙은 <see cref="LobbyCounterPromptRules"/>.
/// 봇이 스폰될 때 붙고(<see cref="Attach"/>) 디스폰 때 UI 와 함께 사라진다. UI 는 머리 위 체력바 공용 캔버스(<see cref="OverheadHealthBarCanvas"/>)에 그리고,
/// 위치 계산·카메라 뒤/화면 밖 처리도 체력바와 같다. 수치·이미지 = <see cref="LobbyCounterPromptSettings"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyCounterPrompt : MonoBehaviour
{
    private LobbyCounterPromptSettings _settings;
    private TrainingSparringBot _bot;
    private IInterruptWindowReplica _window;
    private Canvas _canvas;
    private RectTransform _root;
    private CanvasGroup _group;
    private Image _glow;
    private TMP_Text _label;
    private bool _wasActive;
    private float _pulseStartedAt;

    /// <summary>봇에 표시를 붙인다. 설정·이미지가 비면 경고만 — 훈련 진행에는 영향 없다.</summary>
    public static LobbyCounterPrompt Attach(TrainingSparringBot bot)
    {
        var settings = Resources.Load<LobbyCounterPromptSettings>(LobbyCounterPromptSettings.ResourcePath);
        if (settings == null || settings.MouseIcon == null || settings.RightButtonGlow == null)
        {
            Debug.LogWarning($"[LobbyCounterPrompt] Resources/{LobbyCounterPromptSettings.ResourcePath} 또는 이미지가 비었다 — 우클릭 표시 없이 진행한다.", bot);
            return null;
        }

        var prompt = bot.gameObject.AddComponent<LobbyCounterPrompt>();
        prompt.Build(settings, bot);
        return prompt;
    }

    private void Build(LobbyCounterPromptSettings settings, TrainingSparringBot bot)
    {
        _settings = settings;
        _bot = bot;
        _window = bot;
        _canvas = OverheadHealthBarCanvas.Instance;

        var icon = settings.MouseIcon;
        var iconSize = new Vector2(settings.IconHeight * icon.rect.width / icon.rect.height, settings.IconHeight);

        var rootObject = new GameObject($"CounterPrompt ({bot.name})", typeof(RectTransform), typeof(CanvasGroup));
        _root = (RectTransform)rootObject.transform;
        _root.SetParent(_canvas.transform, false);
        _root.anchorMin = _root.anchorMax = Vector2.zero;
        _root.pivot = new Vector2(0.5f, 0.5f);
        _root.sizeDelta = iconSize;
        _group = rootObject.GetComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable = false;

        var iconImage = CreateImage("Icon", icon, iconSize);
        iconImage.color = Color.white;

        // 빛 스프라이트는 번질 여백만큼 아이콘보다 크다 — 월드 크기 비율(Pixels Per Unit 반영)로 맞추면 버튼 모양이 아이콘과 겹친다.
        var glow = settings.RightButtonGlow;
        var glowSize = Vector2.Scale(iconSize, new Vector2(glow.bounds.size.x / icon.bounds.size.x, glow.bounds.size.y / icon.bounds.size.y));
        _glow = CreateImage("RightButtonGlow", glow, glowSize);

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var labelRect = (RectTransform)labelObject.transform;
        labelRect.SetParent(_root, false);
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(1f, 0.5f);
        labelRect.pivot = new Vector2(0f, 0.5f);
        labelRect.anchoredPosition = new Vector2(settings.LabelGap, 0f);
        labelRect.sizeDelta = new Vector2(200f, settings.IconHeight);
        _label = labelObject.GetComponent<TextMeshProUGUI>();
        if (settings.Font != null)
            _label.font = settings.Font;
        if (settings.FontMaterial != null)
            _label.fontSharedMaterial = settings.FontMaterial;
        _label.fontSize = settings.LabelFontSize;
        _label.color = settings.LabelColor;
        _label.alignment = TextAlignmentOptions.MidlineLeft;
        _label.textWrappingMode = TextWrappingModes.NoWrap;
        _label.raycastTarget = false;

        rootObject.SetActive(false);
    }

    private Image CreateImage(string objectName, Sprite sprite, Vector2 size)
    {
        var imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)imageObject.transform;
        rect.SetParent(_root, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        var image = imageObject.GetComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }

    private void OnEnable() => Canvas.willRenderCanvases += ApplyScreenPosition;

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= ApplyScreenPosition;
        if (_root != null)
            _root.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_root != null)
            Destroy(_root.gameObject);
    }

    private void LateUpdate()
    {
        if (_root == null)
            return;

        var director = LobbyTrainingDirector.Active;
        var activity = director != null ? director.CurrentActivity : LobbyTutorialActivity.None;
        var active = LobbyCounterPromptRules.PromptActive(activity, _bot.SparringStage, _window.ReplicatedWindowOpen, _bot.IsCrowdControlReacting);

        // 시간은 unscaled — 간파 슬로우에 펄스가 늘어지지 않게.
        if (LobbyCounterPromptRules.PulseStarts(_wasActive, active))
        {
            _pulseStartedAt = Time.unscaledTime;
            _label.text = LobbyTutorialUiStrings.CounterPromptText;
        }
        _wasActive = active;

        var visible = LobbyCounterPromptRules.Visible(active, InterruptProductionDirector.CombatUiHidden);
        if (_root.gameObject.activeSelf != visible)
            _root.gameObject.SetActive(visible);
        if (!visible)
            return;

        var pulse = LobbyCounterPromptRules.Pulse(Time.unscaledTime - _pulseStartedAt, _settings.PulseSeconds);
        var color = _settings.GlowColor;
        color.a = Mathf.Lerp(_settings.GlowRestAlpha, 1f, pulse);
        _glow.color = color;
        _root.localScale = Vector3.one * Mathf.Lerp(1f, _settings.PulseScale, pulse);
    }

    // 머리 위 체력바와 같은 시점·계산 — 카메라가 LateUpdate 에서 움직인 뒤라 봇에서 밀리지 않는다. 카메라 없음·뒤·화면 밖은 투명.
    private void ApplyScreenPosition()
    {
        if (_root == null || !_root.gameObject.activeSelf)
            return;

        var cam = Camera.main;
        var onScreen = false;
        if (cam != null)
        {
            var anchor = OverheadHealthBarScreenPlacement.AnchorWorld(transform.position, _settings.WorldHeight);
            onScreen = OverheadHealthBarScreenPlacement.TryGetCanvasPosition(cam.WorldToScreenPoint(anchor),
                new Vector2(Screen.width, Screen.height), _canvas.scaleFactor, _settings.ScreenOffset, _root.sizeDelta * 0.5f, out var position);
            if (onScreen)
                _root.anchoredPosition = position;
        }

        _group.alpha = onScreen ? 1f : 0f;
    }
}
