using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 튜토리얼 특성 선택 UI(할 일 15 T2) — 로컬 플레이어 전용 표시. 상태는 <see cref="TutorialTraitDirector"/> 복제값만 읽는다.
/// - 선택 창: 중앙 모달. 창 루트의 <see cref="UiModalBlocker"/> 가 켜져 있는 동안 로컬 Player 입력(이동·대시 포함)이 막힌다. 일시정지 없음.
/// - 열림/닫힘 판정은 <see cref="TutorialTraitWindowModel"/>(한 번 닫힌 라운드 재표시 금지·획득 토스트 판정).
/// - 획득 특성 다시 보기: 좌상단 아이콘 줄 + 호버 툴팁.
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

    private readonly TutorialTraitWindowModel model = new TutorialTraitWindowModel();
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

    private void Awake()
    {
        if (cardTemplate != null)
            cardTemplate.gameObject.SetActive(false);
        if (iconTemplate != null)
            iconTemplate.gameObject.SetActive(false);
        SetActive(window, false);
        SetActive(toast, false);
        SetActive(tooltip, false);
    }

    private void OnDisable()
    {
        Bind(null);
        CloseWindow();
        SetActive(toast, false);
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
