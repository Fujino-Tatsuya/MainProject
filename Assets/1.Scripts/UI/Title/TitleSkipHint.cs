using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 타이틀 접근 연출의 "Esc 건너뛰기" 안내 — 보스 등장 연출 스킵 위젯(<see cref="BossIntroPresentation"/>)의 키캡·문구와 같은 모양.
/// 오른쪽 아래 키캡(Esc) + "건너뛰기" 만 — 알약 배경·외곽선은 없다(할 일 227). 보스방은 Space 1초 홀드로 둘레가 채워지지만
/// 타이틀은 <b>누르면 바로</b>라 채움·동의 수는 없다(경석 10-26). 켜고 끄기는 <see cref="TitleFlowDirector"/> 가 이 오브젝트를 켜고 끈다.
/// 위치·크기 = 씬의 이 RectTransform 값(코드가 덮지 않는다). 에디터에서 오브젝트를 켜면 키캡·문구 미리보기가 생겨(저장 안 됨) 보면서 옮길 수 있다.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class TitleSkipHint : MonoBehaviour
{
    [Tooltip("글꼴 — 보스 스킵 위젯과 같은 서울알림 Medium SDF.")]
    [SerializeField] private TMP_FontAsset font;

    private TMP_Text _label;
    private bool _built;
    private float _builtHeight = -1f;

    private void Awake()
    {
        if (Application.isPlaying) Build();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            RebuildPreview();
            return;
        }

        Build();
        _label.text = UiStrings.BossIntroSkip.Text;   // 현재 언어 — 켤 때마다(설정에서 언어를 바꿨을 수 있다)
    }

    private void OnDisable()
    {
        if (!Application.isPlaying) ClearPreview();
    }

#if UNITY_EDITOR
    // 에디터에서 루트 크기를 바꾸면 미리보기를 새 배율로 다시 만든다(레이아웃 콜백 안에서 지우지 않게 한 박자 뒤).
    private void OnRectTransformDimensionsChange()
    {
        if (Application.isPlaying || !isActiveAndEnabled) return;
        if (Mathf.Approximately(((RectTransform)transform).rect.height, _builtHeight)) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && isActiveAndEnabled && !Application.isPlaying) RebuildPreview();
        };
    }
#endif

    // 에디터 미리보기 — 저장 안 되는 자식(DontSave)으로 만들고, 끌 때·다시 켤 때 지운다(도메인 리로드 뒤 중복 방지).
    private void RebuildPreview()
    {
        ClearPreview();
        _built = false;
        Build();
        _label.text = "건너뛰기";
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
            if (child != transform) child.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
    }

    private void ClearPreview()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if ((child.hideFlags & HideFlags.DontSave) != 0) DestroyImmediate(child);
        }
        _built = false;
    }

    private void Build()
    {
        if (_built) return;
        _built = true;

        var root = (RectTransform)transform;

        // 키캡·글자 크기 = 루트 높이 비례(기준 높이 = 보스 스킵 위젯 알약 높이 72 → 배율 1).
        float s = Mathf.Max(0.1f, root.rect.height / BossIntroPresentation.SkipPillHeight);
        _builtHeight = root.rect.height;

        float kw = BossIntroPresentation.SkipKeycapWidth * s;
        float kh = BossIntroPresentation.SkipKeycapHeight * s;
        Image keycap = NewImage(root, "Keycap",
            BossIntroPresentation.KeycapSprite(Mathf.RoundToInt(kw), Mathf.RoundToInt(kh)), Color.white);
        RectTransform k = keycap.rectTransform;
        k.anchorMin = k.anchorMax = k.pivot = new Vector2(0f, 0.5f);
        k.sizeDelta = new Vector2(kw, kh);
        k.anchoredPosition = new Vector2(22f * s, 1f * s);
        TMP_Text key = Label(keycap.transform, "Key", 21f * s, TextAlignmentOptions.Center);
        Stretch((RectTransform)key.transform);
        ((RectTransform)key.transform).offsetMin = new Vector2(0f, 3f * s);   // 키캡 아랫단(그림자) 위 가운데
        key.text = "Esc";
        key.color = BossIntroPresentation.KeycapTextColor;
        key.fontStyle = FontStyles.Bold;

        _label = Label(root, "Label", 28f * s, TextAlignmentOptions.Right);
        Stretch((RectTransform)_label.transform);
        ((RectTransform)_label.transform).offsetMax = new Vector2(-30f * s, 0f);
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
