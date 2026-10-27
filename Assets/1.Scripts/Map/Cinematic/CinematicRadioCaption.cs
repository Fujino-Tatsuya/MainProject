using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무전 한 줄 캡션(로컬 표시) — SVN r504(이지원) "진행 도움 말풍선" 3장을 겹친다:
/// ① 캡슐 배경(progress_hint_background_long — 양 끝 반원을 9-슬라이스로 잘라 글자 길이에 맞춘다) ② 원형 배지 ③ 무전기, 위에 TMP 한 줄.
/// 아트 README: "Unity 에서 1 → 2 → 3 순서로 겹쳐 사용, 텍스트는 별도 TMP 한 줄, 배경은 원하는 길이로 잘라 사용".
/// 줄바꿈은 하지 않는다 — 긴 대사는 호출자가 <see cref="CinematicLineSplitter"/> 로 한 줄씩 나눠 넘긴다(<see cref="Measure"/> 로 폭을 잰다).
/// 그림이 비어 있으면 반투명 검정 캡슐로 대신한다(글자는 그대로 읽힌다).
/// </summary>
public sealed class CinematicRadioCaption
{
    private const int CanvasSortingOrder = 440;   // 연출 오버레이(450) 아래, HUD 위
    // 아트 실측(handoff.json): 배경 3848×148px(도형 3840×140, 여백 4) · 배지 188×186 · 무전기 144×216 · 모두 2배 해상도 PPU 200.
    private const float CapsuleHeight = 70f;
    private const float CapsuleBorderPx = 76f;    // 반원 반지름 70px + 여백 4px + 2
    private const float BadgeSize = 94f;
    private const float WalkieWidth = 72f;
    private const float WalkieHeight = 108f;
    private const float TextPadLeft = 34f;
    private const float TextPadRight = 20f;        // 배지 앞까지

    private readonly string _name;
    private Canvas _canvas;
    private RectTransform _group;
    private RectTransform _capsule;
    private Image _capsuleImage;
    private RectTransform _badge;
    private RectTransform _walkie;
    private TMP_Text _text;
    private CanvasGroup _alpha;
    private float _scale = 1f;
    private bool _fontApplied;
    private Vector2 _baseOffset;
    private float _riseDistance;
    private float _riseSeconds = 0.25f;
    private float _pulseScale;
    private float _pulseSeconds = 0.35f;

    public CinematicRadioCaption(string name)
    {
        _name = name;
    }

    public bool Owns(Canvas canvas) => canvas != null && canvas == _canvas;

    /// <summary>
    /// 모양·위치를 정한다(처음 한 번 만들고, 값은 매번 덮어써도 된다).
    /// <paramref name="anchor"/> = 캡션 중심의 화면 비율 위치(0~1, y 0 = 아래), <paramref name="offset"/> = 거기서 더 옮기는 UI 단위.
    /// </summary>
    public void Configure(Sprite background, Sprite badge, Sprite walkie, TMP_FontAsset font, Vector2 anchor, Vector2 offset,
                          float scale, float fontSize, Color textColor)
    {
        Ensure(background, badge, walkie);
        _scale = Mathf.Max(0.1f, scale);
        _group.anchorMin = _group.anchorMax = new Vector2(Mathf.Clamp01(anchor.x), Mathf.Clamp01(anchor.y));
        _baseOffset = offset;
        _text.fontSize = fontSize * _scale;
        _text.color = textColor;
        if (!_fontApplied && font != null)
        {
            _text.font = font;
            _fontApplied = true;
        }
    }

    /// <summary>이 캡션 글자로 그렸을 때 한 줄 폭(UI 단위). 리치 태그는 폭에 안 들어간다.</summary>
    public float Measure(string text)
    {
        if (_text == null || string.IsNullOrEmpty(text)) return 0f;
        return _text.GetPreferredValues(text, float.PositiveInfinity, float.PositiveInfinity).x;
    }

    /// <summary>
    /// 등장 움직임(경석 10-27 — 아웃라인 대신): 나타날 때 <paramref name="riseDistance"/> 아래에서 <paramref name="riseSeconds"/> 동안 올라오고,
    /// 무전기 배지가 <paramref name="pulseSeconds"/> 동안 한 번 <paramref name="pulseScale"/> 만큼 커졌다 돌아온다. 0 이면 끈다.
    /// </summary>
    public void ConfigureMotion(float riseDistance, float riseSeconds, float pulseScale, float pulseSeconds)
    {
        _riseDistance = riseDistance;
        _riseSeconds = riseSeconds;
        _pulseScale = pulseScale;
        _pulseSeconds = pulseSeconds;
    }

    /// <summary>
    /// 한 줄을 보인다(<paramref name="alpha"/> 0 = 숨김). 캡슐 길이는 글자 폭에 맞춘다.
    /// <paramref name="sinceAppear"/> = 이 조각이 나타난 뒤 지난 초(등장 움직임용 — 모든 피어가 같은 시각으로 계산한다).
    /// </summary>
    public void Show(string text, float alpha, float sinceAppear)
    {
        if (_canvas == null) return;
        bool visible = alpha > 0f && !string.IsNullOrEmpty(text);
        if (_canvas.gameObject.activeSelf != visible) _canvas.gameObject.SetActive(visible);
        if (!visible) return;

        if (_text.text != text)
        {
            _text.text = text;
            Layout(Measure(text));
        }
        _alpha.alpha = Mathf.Clamp01(alpha);

        float s = _scale;
        _group.anchoredPosition = _baseOffset + new Vector2(0f, CinematicCaptionMotion.RiseOffset(sinceAppear, _riseDistance * s, _riseSeconds));
        float pulse = CinematicCaptionMotion.PulseScale(sinceAppear, _pulseScale, _pulseSeconds);
        _badge.localScale = _walkie.localScale = new Vector3(pulse, pulse, 1f);
    }

    public void Hide()
    {
        if (_canvas != null && _canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(false);
    }

    public void Dispose()
    {
        if (_canvas != null) Object.Destroy(_canvas.gameObject);
        _canvas = null;
    }

    // 캡슐 = [왼쪽 여백 | 글자 | 오른쪽 여백] + 오른쪽 끝에 배지(캡슐 끝에 반쯤 걸친다) + 배지 위 무전기. 묶음 전체를 가운데 정렬.
    private void Layout(float textWidth)
    {
        float s = _scale;
        float h = CapsuleHeight * s;
        float badge = BadgeSize * s;
        float capsuleWidth = Mathf.Max(h * 2f, (TextPadLeft + TextPadRight) * s + textWidth + badge * 0.5f);
        float total = capsuleWidth + badge * 0.5f;

        _group.sizeDelta = new Vector2(total, Mathf.Max(h, WalkieHeight * s));

        _capsule.anchoredPosition = new Vector2(-total * 0.5f, 0f);
        _capsule.sizeDelta = new Vector2(capsuleWidth, h);
        if (_capsuleImage.type == Image.Type.Sliced) _capsuleImage.pixelsPerUnitMultiplier = 1f / s;   // 반원 끝을 높이에 맞춘다

        RectTransform textRect = _text.rectTransform;
        textRect.offsetMin = new Vector2(TextPadLeft * s, 0f);
        textRect.offsetMax = new Vector2(-(TextPadRight * s + badge * 0.5f), 0f);

        float badgeCenterX = -total * 0.5f + capsuleWidth;   // 캡슐 오른쪽 끝
        _badge.anchoredPosition = new Vector2(badgeCenterX, 0f);
        _badge.sizeDelta = new Vector2(badge, badge);
        _walkie.anchoredPosition = new Vector2(badgeCenterX, 4f * s);
        _walkie.sizeDelta = new Vector2(WalkieWidth * s, WalkieHeight * s);
    }

    private void Ensure(Sprite background, Sprite badge, Sprite walkie)
    {
        if (_canvas != null) return;

        var root = new GameObject(_name + "RadioCaption", typeof(RectTransform));
        _canvas = root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = CanvasSortingOrder;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var group = new GameObject("Group", typeof(RectTransform), typeof(CanvasGroup));
        group.transform.SetParent(root.transform, false);
        _group = (RectTransform)group.transform;
        _group.anchorMin = _group.anchorMax = new Vector2(0.5f, 0.28f);   // 실제 값은 Configure(화면 비율 — 데이터)
        _group.pivot = new Vector2(0.5f, 0.5f);                          // 캡션 중심이 그 비율 지점에 온다
        _alpha = group.GetComponent<CanvasGroup>();
        _alpha.blocksRaycasts = false;
        _alpha.interactable = false;

        _capsuleImage = CinematicOverlay.NewImage(_group, "Capsule", SlicedCapsule(background), background != null ? Color.white : new Color(0f, 0f, 0f, 0.72f));
        if (background != null) _capsuleImage.type = Image.Type.Sliced;
        _capsule = _capsuleImage.rectTransform;
        _capsule.anchorMin = _capsule.anchorMax = new Vector2(0.5f, 0.5f);
        _capsule.pivot = new Vector2(0f, 0.5f);

        _text = CinematicOverlay.Label(_capsule, "Text", 30f, TextAlignmentOptions.Left);
        CinematicOverlay.Stretch(_text.rectTransform);
        _text.overflowMode = TextOverflowModes.Overflow;

        Image badgeImage = CinematicOverlay.NewImage(_group, "Badge", badge, badge != null ? Color.white : new Color(0f, 0f, 0f, 0f));
        _badge = badgeImage.rectTransform;
        _badge.anchorMin = _badge.anchorMax = new Vector2(0.5f, 0.5f);
        _badge.pivot = new Vector2(0.5f, 0.5f);

        Image walkieImage = CinematicOverlay.NewImage(_group, "Walkie", walkie, walkie != null ? Color.white : new Color(0f, 0f, 0f, 0f));
        walkieImage.preserveAspect = true;
        _walkie = walkieImage.rectTransform;
        _walkie.anchorMin = _walkie.anchorMax = new Vector2(0.5f, 0.5f);
        _walkie.pivot = new Vector2(0.5f, 0.5f);

        root.SetActive(false);
    }

    // 배경 PNG 는 테두리(border) 0 으로 임포트돼 있다 — 같은 텍스처로 양 끝 반원을 테두리로 잡은 스프라이트를 런타임에 만든다(에셋 무수정).
    private static Sprite SlicedCapsule(Sprite source)
    {
        if (source == null) return null;
        Rect r = source.rect;
        float border = Mathf.Min(CapsuleBorderPx, r.width * 0.5f - 1f);
        Sprite sliced = Sprite.Create(source.texture, r, new Vector2(0.5f, 0.5f), source.pixelsPerUnit, 0, SpriteMeshType.FullRect,
                                      new Vector4(border, 0f, border, 0f));
        sliced.name = source.name + " (sliced)";
        return sliced;
    }
}
