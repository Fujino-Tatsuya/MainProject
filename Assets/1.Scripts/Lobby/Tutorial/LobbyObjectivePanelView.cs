using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 좌상단 목표 패널(할 일 158, 명세 3.1) — 챕터·지시문·설명·체크·구분선·진행을 한 패널에 그린다.
/// 배경 = TextBox.png(Sliced). 폭은 설명 줄이 있으면 넓게(<see cref="LobbyTutorialUiRules.PanelWidth"/>), 높이는 줄바꿈으로 늘어난다(ContentSizeFitter 세로).
/// 글자 축소 안 함(autoSize 끔). 포인터 입력을 막지 않는다(모든 Graphic raycastTarget 끔 — 저작 메뉴가 맞춘다).
/// 표시·숨김은 <see cref="LobbyTutorialUI"/> 가 정한다(<see cref="SetVisible"/>).
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyObjectivePanelView : MonoBehaviour
{
    [Header("구성")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform panel;
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text chapterText;
    [SerializeField] private TMP_Text objectiveText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private TMP_Text checklistText;
    [SerializeField] private Image divider;
    [SerializeField] private TMP_Text progressText;

    [Header("폭 (1920×1080 기준 px)")]
    [SerializeField, Min(100f)] private float basicWidth = 560f;
    [SerializeField, Min(100f)] private float wideWidth = 680f;

    [Header("색 (승인 결정 — 테두리 #E3B95A, 제목·숫자 #F0C870)")]
    [Tooltip("구분선 색. 배경 TextBox 의 금테는 아트 색 그대로다.")]
    [SerializeField] private Color borderGold = new Color32(0xE3, 0xB9, 0x5A, 0xFF);
    [Tooltip("챕터 글자와 진행 줄 숫자.")]
    [SerializeField] private Color titleGold = new Color32(0xF0, 0xC8, 0x70, 0xFF);
    [SerializeField] private Color objectiveColor = Color.white;
    [SerializeField] private Color bodyColor = new Color32(0xE6, 0xE6, 0xE6, 0xFF);
    [Tooltip("체크 항목 글자색. 완료 행은 이 색을 doneDarken 만큼 어둡게 + 취소선.")]
    [SerializeField] private Color checkColor = new Color32(0xE6, 0xE6, 0xE6, 0xFF);
    [SerializeField, Range(0f, 1f)] private float doneDarken = 0.5f;
    [SerializeField] private Color progressColor = Color.white;
    [Tooltip("배경 알파(명세 검정 α≈0.8 — 금테도 같이 옅어진다). Play 에서 판단.")]
    [SerializeField, Range(0f, 1f)] private float backgroundAlpha = 0.8f;

    [Header("체크 글리프 (정적 아틀라스에 있는 글자)")]
    [SerializeField] private string pendingMark = "□";
    [SerializeField] private string doneMark = "■";

    [Header("태그 표기 ({tag:ID})")]
    [Tooltip("Knockback·Stun 색 원본 — 스킬 툴팁과 같은 에셋.")]
    [SerializeField] private SkillTooltipTagStyles tagStyles;
    [Tooltip("스킬 툴팁 태그에 없는 ID(Suppress) 의 글자색.")]
    [SerializeField] private Color fallbackTagText = new Color32(0xF0, 0xC8, 0x70, 0xFF);
    [SerializeField] private Color fallbackTagBackground = new Color32(0x3D, 0x2A, 0x12, 0xE6);

    private readonly StringBuilder _builder = new StringBuilder();
    private LobbyObjectiveView _view;

    public LobbyObjectiveView Current => _view;

    private void Awake()
    {
        ApplyStaticColors();
        SetVisible(false);
    }

    /// <summary>내용을 바꾼다(표시 여부는 건드리지 않음). null = 비움.</summary>
    public void Render(LobbyObjectiveView view)
    {
        _view = view;
        ApplyStaticColors();
        if (view == null)
        {
            SetText(chapterText, null);
            SetText(objectiveText, null);
            SetText(bodyText, null);
            SetText(checklistText, null);
            SetActive(divider, false);
            SetText(progressText, null);
            return;
        }

        if (panel != null)
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, LobbyTutorialUiRules.PanelWidth(view, basicWidth, wideWidth));

        SetText(chapterText, Resolve(view.ChapterKey));
        SetText(objectiveText, Resolve(view.ObjectiveKey));
        SetText(bodyText, JoinLines(view.BodyKeys));
        SetText(checklistText, BuildChecklist(view.Checklist));

        bool showProgress = LobbyTutorialUiRules.ShowProgress(view.Progress);
        SetActive(divider, showProgress);
        SetText(progressText, showProgress && LobbyTutorialUiStrings.TryGetProgress(view.Progress.Kind, out UiString key)
            ? key.Format(LobbyTutorialUiRules.ProgressArgs(view.Progress, titleGold))
            : null);
    }

    /// <summary>언어가 바뀌었을 때 같은 내용으로 다시 그린다.</summary>
    public void Refresh() => Render(_view);

    public void SetVisible(bool visible)
    {
        if (group == null)
            return;

        group.alpha = visible ? 1f : 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
    }

    private void ApplyStaticColors()
    {
        if (background != null)
        {
            Color c = background.color;
            c.a = backgroundAlpha;
            background.color = c;
        }

        if (divider != null) divider.color = borderGold;
        if (chapterText != null) chapterText.color = titleGold;
        if (objectiveText != null) objectiveText.color = objectiveColor;
        if (bodyText != null) bodyText.color = bodyColor;
        if (checklistText != null) checklistText.color = checkColor;
        if (progressText != null) progressText.color = progressColor;
    }

    private string Resolve(string key) =>
        string.IsNullOrEmpty(key) ? string.Empty : LobbyTutorialUiRules.ReplaceTags(LobbyTutorialUiStrings.Resolve(key), RenderTag);

    private string JoinLines(string[] keys)
    {
        _builder.Clear();
        if (keys != null)
        {
            foreach (string key in keys)
            {
                if (string.IsNullOrEmpty(key))
                    continue;
                if (_builder.Length > 0)
                    _builder.Append('\n');
                _builder.Append(Resolve(key));
            }
        }

        return _builder.ToString();
    }

    private string BuildChecklist(LobbyTutorialCheckItem[] items)
    {
        _builder.Clear();
        if (items == null)
            return string.Empty;

        Color done = LobbyTutorialUiRules.Darken(checkColor, doneDarken);
        foreach (LobbyTutorialCheckItem item in items)
        {
            if (string.IsNullOrEmpty(item.LabelKey))
                continue;
            if (_builder.Length > 0)
                _builder.Append('\n');
            _builder.Append(LobbyTutorialUiRules.FormatCheckRow(Resolve(item.LabelKey), item.Done, pendingMark, doneMark, done));
        }

        return _builder.ToString();
    }

    private string RenderTag(string id)
    {
        if (!LobbyTutorialUiStrings.TryGetTagLabel(id, out UiString label))
            return null;

        Color32 text = fallbackTagText;
        Color32 back = fallbackTagBackground;
        if (tagStyles != null && SkillTooltipTags.TryParseOne(id, out SkillTooltipTag tag, out _)
            && tagStyles.TryGetColors(tag, out Color32 styleBack, out Color32 styleText))
        {
            back = styleBack;
            text = styleText;
        }

        return LobbyTutorialUiRules.RenderTag(label.Text, text, back);
    }

    // 빈 줄은 오브젝트를 꺼서 레이아웃 간격도 같이 없앤다.
    private static void SetText(TMP_Text text, string value)
    {
        if (text == null)
            return;

        bool has = !string.IsNullOrEmpty(value);
        text.text = has ? value : string.Empty;
        if (text.gameObject.activeSelf != has)
            text.gameObject.SetActive(has);
    }

    private static void SetActive(Graphic graphic, bool active)
    {
        if (graphic != null && graphic.gameObject.activeSelf != active)
            graphic.gameObject.SetActive(active);
    }
}
