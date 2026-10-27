using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 타이틀 접근 연출의 "Esc 건너뛰기" 안내 — 보스 등장 연출 스킵 위젯(<see cref="BossIntroPresentation"/>)과 같은 모양.
/// 오른쪽 아래 알약 + 키캡(Esc) + "건너뛰기". 보스방은 Space 1초 홀드로 둘레가 채워지지만 타이틀은 <b>누르면 바로</b>라
/// 채움·동의 수는 없다(경석 10-26). 켜고 끄기는 <see cref="TitleFlowDirector"/> 가 이 오브젝트를 켜고 끈다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class TitleSkipHint : MonoBehaviour
{
    [Tooltip("알약 배경 — 보스 스킵 위젯과 같은 button_pill_neutral.")]
    [SerializeField] private Sprite pillSprite;
    [Tooltip("글꼴 — 보스 스킵 위젯과 같은 서울알림 Medium SDF.")]
    [SerializeField] private TMP_FontAsset font;

    private TMP_Text _label;
    private bool _built;

    private void Awake() => Build();

    private void OnEnable()
    {
        Build();
        _label.text = UiStrings.BossIntroSkip.Text;   // 현재 언어 — 켤 때마다(설정에서 언어를 바꿨을 수 있다)
    }

    private void Build()
    {
        if (_built) return;
        _built = true;

        const float w = BossIntroPresentation.SkipPillWidth;
        const float h = BossIntroPresentation.SkipPillHeight;

        var root = (RectTransform)transform;
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 0f);
        root.anchoredPosition = new Vector2(-56f, 72f);   // 보스 스킵 위젯 자리
        root.sizeDelta = new Vector2(w, h);

        if (pillSprite != null)
        {
            Image bg = NewImage(root, "Background", pillSprite, Color.white);
            Stretch(bg.rectTransform);
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 2f;   // 보스 위젯과 같다 — 72 높이에 양 끝 반원
        }

        Image outline = NewImage(root, "Outline",
            BossIntroPresentation.RoundedOutlineSprite((int)w, (int)h, h * 0.5f - 1f, 4f), BossIntroPresentation.SkipIdleColor);
        Stretch(outline.rectTransform);

        const float kw = BossIntroPresentation.SkipKeycapWidth;
        const float kh = BossIntroPresentation.SkipKeycapHeight;
        Image keycap = NewImage(root, "Keycap", BossIntroPresentation.KeycapSprite((int)kw, (int)kh), Color.white);
        RectTransform k = keycap.rectTransform;
        k.anchorMin = k.anchorMax = k.pivot = new Vector2(0f, 0.5f);
        k.sizeDelta = new Vector2(kw, kh);
        k.anchoredPosition = new Vector2(22f, 1f);
        TMP_Text key = Label(keycap.transform, "Key", 21f, TextAlignmentOptions.Center);
        Stretch((RectTransform)key.transform);
        ((RectTransform)key.transform).offsetMin = new Vector2(0f, 3f);   // 키캡 아랫단(그림자) 위 가운데
        key.text = "Esc";
        key.color = BossIntroPresentation.KeycapTextColor;
        key.fontStyle = FontStyles.Bold;

        _label = Label(root, "Label", 28f, TextAlignmentOptions.Right);
        Stretch((RectTransform)_label.transform);
        ((RectTransform)_label.transform).offsetMax = new Vector2(-30f, 0f);
        _label.color = BossIntroPresentation.SkipTextColor;
    }

    private static Image NewImage(Transform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private TMP_Text Label(Transform parent, string name, float size, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.fontSize = size;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
