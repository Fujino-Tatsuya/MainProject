using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 튜토리얼 특성 선택 UI(할 일 15 T2) — 로컬 플레이어 전용 표시. 상태는 <see cref="TutorialTraitDirector"/> 복제값만 읽는다.
/// - 선택 창: 중앙 모달. 창 루트의 <see cref="UiModalBlocker"/> 가 켜져 있는 동안 로컬 Player 입력(이동·대시 포함)이 막힌다. 일시정지 없음.
/// - 열림/닫힘 판정은 <see cref="TutorialTraitWindowModel"/>(한 번 닫힌 라운드 재표시 금지·획득 토스트 판정).
/// - 획득 특성 다시 보기: 좌상단 아이콘 줄 + 호버 툴팁.
/// - 교착 방지 게이지(창 안): 값은 바깥(복도 흐름)이 <see cref="SetDeadline"/> 으로 넣는다 — 이 뷰는 튜토리얼 디렉터를 모른다.
/// - 특성 초기화 팝업(할 일 30): 내 팝업 행(<see cref="TutorialTraitDirector.TryGetResetPrompt"/>)이 있으면 연다. 질문 + [예]/[아니오], 루트에 <see cref="UiModalBlocker"/>.
///   프리팹(저작 메뉴 결과)을 다시 만들지 않으려고 창과 같은 캔버스에 <b>런타임 생성</b>한다. 한 세대에는 한 번만 답한다.
/// 씬에 하나 두면 된다(별도 캔버스 — CombatHUD 와 무관). 디렉터가 없으면 아무것도 보이지 않는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialTraitSelectionView : MonoBehaviour
{
    [Header("선택 창 (UiModalBlocker 가 붙은 루트, 기본 꺼짐)")]
    [SerializeField] private GameObject window;
    [SerializeField] private TMP_Text title;
    [SerializeField] private GameObject introRoot;
    [SerializeField] private TMP_Text introText;
    [SerializeField] private GameObject lastNoticeRoot;
    [SerializeField] private TMP_Text lastNoticeText;
    [SerializeField] private RectTransform cardContainer;
    [SerializeField] private TutorialTraitCardView cardTemplate;
    [Tooltip("같은 창에서 연속 클릭을 무시하는 시간(초). 서버가 거부하면 이 시간 뒤 다시 고를 수 있다.")]
    [SerializeField, Min(0f)] private float requestCooldown = 1f;

    [Header("교착 방지 게이지 (창 안 — 값은 바깥이 SetDeadline 으로 넣는다)")]
    [SerializeField] private GameObject deadlineRoot;
    [Tooltip("Filled·Horizontal Image — fillAmount = 남은 / 교착 방지 전체 초.")]
    [SerializeField] private Image deadlineFill;
    [SerializeField] private TMP_Text deadlineSecondsText;
    [SerializeField] private TMP_Text deadlineDoneText;
    [SerializeField] private Color deadlineColor = new Color(0.95f, 0.82f, 0.42f, 1f);
    [SerializeField] private Color deadlineTextColor = Color.white;
    [Tooltip("남은 시간이 TutorialTraitDeadlineModel.UrgentSeconds 이하일 때 게이지·텍스트 색.")]
    [SerializeField] private Color deadlineUrgentColor = new Color(1f, 0.36f, 0.30f, 1f);

    [Header("획득 토스트")]
    [SerializeField] private GameObject toast;
    [SerializeField] private TMP_Text toastText;
    [SerializeField, Min(0.5f)] private float toastSeconds = 4f;

    [Header("획득 특성 다시 보기")]
    [SerializeField] private RectTransform acquiredContainer;
    [SerializeField] private TutorialTraitIconView iconTemplate;
    [SerializeField] private GameObject tooltip;
    [SerializeField] private TMP_Text tooltipName;
    [SerializeField] private TMP_Text tooltipDescription;

    [Header("공용")]
    [Tooltip("문구 테이블(TutorialTraitTexts.asset). 비어 있거나 ID 행이 없으면 코드 상수 기본값.")]
    [SerializeField] private TutorialTraitTextTable texts;
    [Tooltip("특성 아이콘이 비었을 때 쓸 그림. 비어 있으면 빈 칸(흰 사각형).")]
    [SerializeField] private Sprite placeholderIcon;

    /// <summary>씬의 창(하나). 게이지 값을 넣는 쪽(복도 흐름)이 찾는다.</summary>
    public static TutorialTraitSelectionView Instance { get; private set; }

    private readonly TutorialTraitWindowModel model = new TutorialTraitWindowModel();
    private readonly TutorialTraitDeadlineModel deadline = new TutorialTraitDeadlineModel();
    private readonly List<int> offered = new List<int>();
    private readonly List<int> acquired = new List<int>();
    private readonly List<int> shownAcquired = new List<int>();
    private readonly List<TutorialTraitCardView> cards = new List<TutorialTraitCardView>();
    private readonly List<TutorialTraitIconView> icons = new List<TutorialTraitIconView>();

    private TutorialTraitDirector director;
    private bool dirty;
    private float nextRequestAt;
    private float toastHideAt;
    private TutorialTraitIconView hoveredIcon;
    private int shownDeadlineSeconds = -1;
    private int shownDeadlineDone = -1;
    private int shownDeadlineCount = -1;
    private bool shownDeadlineUrgent;

    // 특성 초기화 팝업(런타임 생성) — 열린 세대·답한 세대(한 세대에 한 번만 답한다).
    private GameObject resetPrompt;
    private TMP_Text resetPromptText;
    private TMP_Text resetYesText;
    private TMP_Text resetNoText;
    private int resetPromptGeneration;
    private int answeredResetGeneration;

    private static readonly Color PromptPanelColor = new Color(0.035f, 0.045f, 0.065f, 0.97f);
    private static readonly Color PromptOutlineColor = new Color(0.48f, 0.42f, 0.25f, 0.9f);
    private static readonly Color PromptAccentColor = new Color(0.95f, 0.82f, 0.42f, 1f);

    private void Awake()
    {
        if (cardTemplate != null)
            cardTemplate.gameObject.SetActive(false);
        if (iconTemplate != null)
            iconTemplate.gameObject.SetActive(false);
        BuildResetPrompt();
        SetActive(window, false);
        SetActive(toast, false);
        SetActive(tooltip, false);
        SetActive(deadlineRoot, false);
    }

    private void OnEnable()
    {
        Instance = this;
    }

    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;
        Bind(null);
        CloseWindow();
        CloseResetPrompt();
        SetActive(toast, false);
        SetDeadline(false, 0f, 0f, 0, 0);
    }

    /// <summary>
    /// 교착 방지 게이지 입력(할 일 15 후속) — 바깥(복도 흐름)이 대기 상태가 바뀔 때마다 서버 값으로 넣는다.
    /// 사이에는 실제 시간으로 로컬 감소한다. 게이지는 창 안에 있어 창이 닫혀 있으면 보이지 않는다.
    /// </summary>
    public void SetDeadline(bool visible, float remaining, float total, int done, int count)
    {
        deadline.Sync(visible, remaining, total, done, count);
        RefreshDeadline(force: true);
    }

    private void Update()
    {
        TutorialTraitDirector current = TutorialTraitDirector.Instance;
        if (current != null && !current.IsSpawned)
            current = null;
        if (current != director)
            Bind(current);

        // 같은 프레임의 복제 변경(서버가 행을 지우고 다시 쓰는 중간 상태 포함)을 다 받은 뒤 한 번만 평가한다.
        if (dirty)
        {
            dirty = false;
            Evaluate();
        }

        if (toast != null && toast.activeSelf && Time.unscaledTime >= toastHideAt)
            toast.SetActive(false);

        if (deadline.Visible)
        {
            deadline.Tick(Time.unscaledDeltaTime);
            RefreshDeadline(force: false);
        }
    }

    private void Bind(TutorialTraitDirector next)
    {
        if (director != null)
            director.ClientStateChanged -= MarkDirty;

        director = next;
        if (director != null)
            director.ClientStateChanged += MarkDirty;

        if (director == null)
        {
            if (model.ForceClose())
                CloseWindow();
            CloseResetPrompt();
            shownAcquired.Clear();
            RebuildIcons();
        }

        dirty = director != null;
    }

    private void MarkDirty() => dirty = true;

    private void Evaluate()
    {
        if (director == null || NetworkManager.Singleton == null)
            return;

        ulong localId = NetworkManager.Singleton.LocalClientId;
        director.GetOffered(localId, offered);
        director.GetAcquired(localId, acquired);

        TutorialTraitWindowUpdate update = model.Evaluate(director.Round, offered.Count > 0, acquired.Count);
        if (update.Closed && !update.Opened)
            CloseWindow();
        if (update.Opened)
            OpenWindow();
        if (model.IsOpen)
            ApplyRoundTexts(director.RoundDisplay); // 옵션 복제가 행보다 늦게 와도 맞춘다
        if (update.ShowPickedToast)
            ShowPickedToast();

        EvaluateResetPrompt(localId);

        if (!SameSequence(acquired, shownAcquired))
        {
            shownAcquired.Clear();
            shownAcquired.AddRange(acquired);
            RebuildIcons();
        }
    }

    // ── 선택 창 ────────────────────────────────────────────────────────────

    private void OpenWindow()
    {
        ClearCards();
        TutorialTraitPool pool = director.Pool;
        if (cardTemplate != null && cardContainer != null)
        {
            for (int i = 0; i < offered.Count; i++)
            {
                TutorialTraitCardView card = Instantiate(cardTemplate, cardContainer);
                card.gameObject.SetActive(true);
                card.Bind(pool != null ? pool.Get(offered[i]) : null, offered[i], placeholderIcon, HandleCardClicked);
                cards.Add(card);
            }
        }

        if (title != null)
            title.text = TextOf(TutorialTraitTexts.TitleId);
        nextRequestAt = 0f;
        SetActive(window, true);
    }

    private void CloseWindow()
    {
        SetActive(window, false);
        ClearCards();
    }

    private void ApplyRoundTexts(TutorialTraitRoundDisplay display)
    {
        bool showIntro = (display & TutorialTraitRoundDisplay.Intro) != 0;
        bool showLast = (display & TutorialTraitRoundDisplay.LastNotice) != 0;
        SetActive(introRoot, showIntro);
        SetActive(lastNoticeRoot, showLast);
        if (showIntro && introText != null)
            introText.text = TextOf(TutorialTraitTexts.IntroId);
        if (showLast && lastNoticeText != null)
            lastNoticeText.text = TextOf(TutorialTraitTexts.LastNoticeId);
    }

    private string TextOf(string id) => TutorialTraitTextTable.Resolve(texts, id);

    private void HandleCardClicked(int traitIndex)
    {
        if (director == null || !model.IsOpen || Time.unscaledTime < nextRequestAt)
            return;

        nextRequestAt = Time.unscaledTime + requestCooldown;
        director.RequestChoose(traitIndex); // 확정되면 제시 행이 사라져 Evaluate 가 창을 닫는다
    }

    private void ClearCards()
    {
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null)
                Destroy(cards[i].gameObject);
        }

        cards.Clear();
    }

    // ── 특성 초기화 팝업(할 일 30) ─────────────────────────────────────────

    // 내 팝업 행이 있고 아직 답하지 않은 세대면 연다. 행이 사라지면(서버 처리·이탈) 닫는다. 다시 전멸하면 새 세대로 다시 열린다.
    private void EvaluateResetPrompt(ulong localId)
    {
        bool prompted = director.TryGetResetPrompt(localId, out int generation);
        if (!prompted || generation == answeredResetGeneration)
        {
            CloseResetPrompt();
            return;
        }

        if (resetPromptGeneration == generation && resetPrompt != null && resetPrompt.activeSelf)
            return;

        resetPromptGeneration = generation;
        if (resetPromptText != null)
            resetPromptText.text = TextOf(TutorialTraitTexts.ResetPromptTitleId);
        if (resetYesText != null)
            resetYesText.text = TextOf(TutorialTraitTexts.ResetPromptYesId);
        if (resetNoText != null)
            resetNoText.text = TextOf(TutorialTraitTexts.ResetPromptNoId);
        if (resetPrompt != null)
            resetPrompt.transform.SetAsLastSibling(); // 특성 창 위
        SetActive(resetPrompt, true);
    }

    private void HandleResetAnswer(bool reset)
    {
        if (director == null || resetPrompt == null || !resetPrompt.activeSelf || resetPromptGeneration == 0)
            return;

        // 한 세대에 한 번 — 연속 클릭·서버 반영 전 재평가로 두 번 보내지 않는다(서버도 중복을 거부한다).
        answeredResetGeneration = resetPromptGeneration;
        CloseResetPrompt();
        director.RequestResetAnswer(answeredResetGeneration, reset);
    }

    private void CloseResetPrompt() => SetActive(resetPrompt, false);

    // 창과 같은 캔버스에 질문 + [예]/[아니오] 를 만든다. 꺼진 채 UiModalBlocker 를 붙여 켜질 때만 입력을 막는다.
    private void BuildResetPrompt()
    {
        if (resetPrompt != null)
            return;

        Transform parent = window != null && window.transform.parent != null ? window.transform.parent : transform;
        TMP_FontAsset font = title != null ? title.font : null;

        RectTransform root = NewUi("ResetPrompt", parent);
        root.gameObject.SetActive(false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        root.gameObject.AddComponent<UiModalBlocker>();
        Image dim = root.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true; // 창 밖 클릭이 뒤 UI 로 새지 않게

        RectTransform panel = NewUi("Panel", root);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(720f, 0f);
        Image panelBackground = panel.gameObject.AddComponent<Image>();
        panelBackground.color = PromptPanelColor;
        panelBackground.raycastTarget = false;
        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = PromptOutlineColor;
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        VerticalLayoutGroup panelLayout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(40, 40, 36, 36);
        panelLayout.spacing = 28f;
        panelLayout.childAlignment = TextAnchor.UpperCenter;
        panelLayout.childControlWidth = panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        resetPromptText = NewText("Question", panel, font, 30f, FontStyles.Bold, PromptAccentColor);

        RectTransform buttons = NewUi("Buttons", panel);
        HorizontalLayoutGroup buttonsLayout = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
        buttonsLayout.spacing = 32f;
        buttonsLayout.childAlignment = TextAnchor.MiddleCenter;
        buttonsLayout.childControlWidth = buttonsLayout.childControlHeight = false;
        buttonsLayout.childForceExpandWidth = buttonsLayout.childForceExpandHeight = false;
        LayoutElement buttonsElement = buttons.gameObject.AddComponent<LayoutElement>();
        buttonsElement.preferredHeight = buttonsElement.minHeight = 64f;

        resetYesText = NewButton("Yes", buttons, font, () => HandleResetAnswer(true));
        resetNoText = NewButton("No", buttons, font, () => HandleResetAnswer(false));
        resetPrompt = root.gameObject;
    }

    private static TMP_Text NewButton(string name, Transform parent, TMP_FontAsset font, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rect = NewUi(name, parent);
        rect.sizeDelta = new Vector2(220f, 64f);
        Image background = rect.gameObject.AddComponent<Image>();
        background.color = new Color(0.09f, 0.11f, 0.15f, 1f);
        Outline outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = PromptOutlineColor;
        outline.effectDistance = new Vector2(2f, -2f);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        button.colors = colors;
        button.onClick.AddListener(onClick);

        TMP_Text label = NewText("Label", rect, font, 26f, FontStyles.Bold, Color.white);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        return label;
    }

    private static TMP_Text NewText(string name, Transform parent, TMP_FontAsset font, float size, FontStyles style, Color color)
    {
        RectTransform rect = NewUi(name, parent);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null)
            text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.richText = true;
        return text;
    }

    private static RectTransform NewUi(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    // ── 교착 방지 게이지 ───────────────────────────────────────────────────

    // 게이지는 매 프레임, 텍스트·색은 표시값이 바뀔 때만.
    private void RefreshDeadline(bool force)
    {
        SetActive(deadlineRoot, deadline.Visible);
        if (!deadline.Visible)
        {
            shownDeadlineSeconds = shownDeadlineDone = shownDeadlineCount = -1;
            return;
        }

        bool urgent = deadline.IsUrgent;
        if (deadlineFill != null)
            deadlineFill.fillAmount = deadline.Fill;

        if (!force && urgent == shownDeadlineUrgent && deadline.DisplaySeconds == shownDeadlineSeconds
            && deadline.Done == shownDeadlineDone && deadline.Count == shownDeadlineCount)
            return;

        shownDeadlineUrgent = urgent;
        shownDeadlineSeconds = deadline.DisplaySeconds;
        shownDeadlineDone = deadline.Done;
        shownDeadlineCount = deadline.Count;

        if (deadlineFill != null)
            deadlineFill.color = urgent ? deadlineUrgentColor : deadlineColor;
        if (deadlineSecondsText != null)
        {
            deadlineSecondsText.text = deadline.FormatSeconds();
            deadlineSecondsText.color = urgent ? deadlineUrgentColor : deadlineTextColor;
        }
        if (deadlineDoneText != null)
            deadlineDoneText.text = deadline.FormatDone();
    }

    // ── 토스트 ─────────────────────────────────────────────────────────────

    private void ShowPickedToast()
    {
        if (toast == null)
            return;

        if (toastText != null)
            toastText.text = TutorialTraitTexts.FormatPicked(
                TextOf(TutorialTraitTexts.PickedId), TextOf(TutorialTraitTexts.PickedLocationId));
        toastHideAt = Time.unscaledTime + toastSeconds;
        toast.SetActive(true);
    }

    // ── 획득 특성 다시 보기 ────────────────────────────────────────────────

    private void RebuildIcons()
    {
        HandleIconHover(hoveredIcon, null);
        for (int i = 0; i < icons.Count; i++)
        {
            if (icons[i] != null)
                Destroy(icons[i].gameObject);
        }

        icons.Clear();
        if (iconTemplate == null || acquiredContainer == null || director == null)
            return;

        TutorialTraitPool pool = director.Pool;
        for (int i = 0; i < shownAcquired.Count; i++)
        {
            TutorialTraitIconView view = Instantiate(iconTemplate, acquiredContainer);
            view.gameObject.SetActive(true);
            view.Bind(pool != null ? pool.Get(shownAcquired[i]) : null, placeholderIcon, HandleIconHover);
            icons.Add(view);
        }
    }

    private void HandleIconHover(TutorialTraitIconView view, TutorialTraitDefinition trait)
    {
        if (trait == null)
        {
            if (view == hoveredIcon)
            {
                hoveredIcon = null;
                SetActive(tooltip, false);
            }
            return;
        }

        hoveredIcon = view;
        if (tooltipName != null)
            tooltipName.text = trait.DisplayName;
        if (tooltipDescription != null)
            tooltipDescription.text = trait.Description;
        SetActive(tooltip, true);
    }

    // ── 유틸 ───────────────────────────────────────────────────────────────

    private static bool SameSequence(List<int> a, List<int> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }
}
