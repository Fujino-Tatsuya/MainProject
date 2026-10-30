using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// 로비 캐릭터 선택 패널(할 일 48.2) — 로스터 엔트리마다 카드를 만들고, 오른쪽 비스듬한 세로 스택(위 = 이전, 가운데 = 포커스, 아래 = 다음)으로 보여 준다.
/// 판단은 <see cref="CharacterSelectPanelRules"/>. 🔴 네트워크를 모른다 — 고르면 <see cref="CharacterChosen"/> 만 내고,
/// 서버 요청(RequestCharacterServerRpc)·준비 상태 갱신(<see cref="SetLocked"/>)은 붙이는 쪽(48 C12) 책임. 사용법 = Docs/tech/character-select-panel.md.
///
/// - 열려 있는 동안 <c>content</c> 의 <see cref="UiModalBlocker"/> 가 로컬 플레이어 입력을 막고, 불투명 배경이 뒤 클릭을 막는다.
/// - 닫기 = 뒤로가기 버튼·ESC. <see cref="LobbyResultPanel.IsEscapeClaimed"/> 면 ESC·키 입력을 무시한다(결과 패널이 위에 있다).
/// - 기본/코어 버튼(할 일 162) = "미구현" 팝업. 팝업이 열린 동안 ESC 는 팝업만 닫고, 확정·이동·휠·카드 클릭은 막힌다.
/// </summary>
[DisallowMultipleComponent]
public sealed class CharacterSelectPanel : MonoBehaviour
{
    [Tooltip("켜고 끄는 본체. UiModalBlocker 가 붙어 있다. 프리팹 루트(Canvas)는 늘 켜 둔다.")]
    [SerializeField] private GameObject content;
    [SerializeField] private CharacterRoster roster;
    [Tooltip("카드 원본 — cardTemplate 이 비었을 때만 쓴다.")]
    [SerializeField] private CharacterSelectCard cardPrefab;
    [Tooltip("가운데 카드 모양 원본(Content/Cards/Card_Template — 카드 프리팹 인스턴스, 가운데 자리에 놓여 있다). " +
             "초상화(Clip/Image_Portrait) 크기·회전, 선택 테두리(Ring_Focus) 크기·색·스프라이트를 여기서 고치면 " +
             "런타임 카드가 이걸 로스터 수만큼 복제한다(옆 카드도 같은 원본, 배율만 작다). 자신은 열 때 꺼진다. 비면 cardPrefab.")]
    [SerializeField] private CharacterSelectCard cardTemplate;
    [Tooltip("카드 부모(화면 가운데 기준). 카드는 런타임에 로스터 수만큼 복제한다 — cardTemplate 말고 디자인용 카드를 두지 말 것.")]
    [SerializeField] private RectTransform cardContainer;
    [SerializeField] private Button backButton;
    [Tooltip("마우스 확정 — SELECT 셰브론(Content/Chevron)의 Button, Space 와 같은 Confirm(할 일 109). " +
             "Navigation None 이어야 클릭 뒤 EventSystem 선택이 남지 않는다. 클릭 영역 = UiTriangleRaycastFilter.")]
    [SerializeField] private Button confirmButton;

    [Header("기본/코어 정보 버튼 + 팝업 (할 일 162 — 지금은 둘 다 '미구현' 팝업)")]
    [SerializeField] private Button basicInfoButton;
    [SerializeField] private Button coreInfoButton;
    [Tooltip("Content 맨 위 팝업(어두운 막 = 뒤 클릭 막기 + 몸체 + X + 본문). 기본 꺼짐. 닫기 = X 버튼·ESC(설정 창 관례).")]
    [SerializeField] private GameObject infoPopup;
    [SerializeField] private Button infoPopupCloseButton;

    [Header("Nameplate (포커스된 캐릭터 — 할 일 162 로 프리팹에서 꺼 둠, 칸은 연결된 채)")]
    [SerializeField] private Image nameplatePortrait;
    [SerializeField] private TMP_Text nameplateName;

    [Header("Texts")]
    [SerializeField] private TMP_Text lockedText;
    [SerializeField] private TMP_Text hintText;

    [Header("Card Slots (cardContainer 기준, 1920×1080 화면 가운데 원점)")]
    [SerializeField] private Vector2 focusedSlot = new Vector2(523f, -72f);
    [SerializeField] private Vector2 previousSlot = new Vector2(779f, 408f);
    [SerializeField] private Vector2 nextSlot = new Vector2(255f, -550f);
    [Tooltip("위·아래 카드 배율(card_unselected 549 / card_selected 654).")]
    [SerializeField] private float sideScale = 0.84f;

    [Header("Card Motion (UI 연출, 초 — unscaled, 할 일 60)")]
    [Tooltip("포커스가 바뀌면 카드가 새 자리로 미끄러지는 시간(EaseOutCubic).")]
    [SerializeField] private float moveSeconds = 0.18f;
    [Tooltip("가운데 자리 금색 테두리가 이동 시작 때 꺼지고, 이동이 끝나면 켜지는 페이드 시간.")]
    [SerializeField] private float ringFadeSeconds = 0.1f;
    [Tooltip("이동이 끝난 뒤 가운데 카드가 커지는 시간(EaseOutCubic).")]
    [SerializeField] private float focusScaleSeconds = 0.12f;
    [Tooltip("다 선 가운데 카드 배율(1 = 확대 없음).")]
    [SerializeField] private float focusScale = 1.08f;

    [Header("Key Repeat (이동 키 길게 누르기, 초 — unscaled, 할 일 60·60.1)")]
    [Tooltip("처음 1칸 뒤 첫 반복까지 기다리는 시간.")]
    [SerializeField] private float keyRepeatDelaySeconds = 0.25f;
    [Tooltip("첫 반복 직후 간격. 홀드 시간에 비례해 최소 간격까지 줄어든다(한 프레임 최대 1칸).")]
    [SerializeField] private float keyRepeatStartIntervalSeconds = 0.25f;
    [Tooltip("가장 짧은 반복 간격 — 램프가 끝나면 이 간격을 유지한다.")]
    [FormerlySerializedAs("keyRepeatIntervalSeconds")]
    [SerializeField] private float keyRepeatMinIntervalSeconds = 0.1f;
    [Tooltip("간격이 시작 간격에서 최소 간격까지 줄어드는 시간(첫 반복 시점부터).")]
    [SerializeField] private float keyRepeatRampSeconds = 0.75f;

    [Header("Behaviour")]
    [SerializeField] private bool closeWithEscape = true;
    [Tooltip("잠금 중 확정을 누르면 잠금 문구를 강조하는 시간(초, unscaled).")]
    [SerializeField] private float lockedPulseSeconds = 0.6f;
    [SerializeField] private float lockedPulseScale = 1.12f;
    [SerializeField] private Color lockedTextColor = new Color(1f, 0.78f, 0.3f, 1f);
    [SerializeField] private Color lockedPulseColor = new Color(1f, 0.38f, 0.32f, 1f);

    private static int s_openCount;
    private static int s_escapeClaimedFrame = -1;

    private readonly List<CharacterSelectCard> _cards = new List<CharacterSelectCard>();
    private readonly List<bool> _available = new List<bool>();
    private readonly List<CharacterSelectCardTrack> _tracks = new List<CharacterSelectCardTrack>();
    private readonly CharacterSelectCardMotion _motion = new CharacterSelectCardMotion();
    private readonly CharacterSelectKeyRepeat _keyRepeat = new CharacterSelectKeyRepeat();
    private CharacterSelectPanelRules _rules;
    private bool _countedOpen;
    private float _lockedPulseUntil = -1f;
    private int _ringCard = -1; // 테두리를 보여 주는 카드 — 이동 중엔 꺼지는 옛 가운데, 정착하면 새 가운데
    private Func<float> _previewClock;

    /// <summary>다른 캐릭터를 골랐다(캐릭터 id = 로스터 index). 곧이어 <see cref="Closed"/>(<see cref="CharacterSelectCloseReason.Chosen"/>).</summary>
    public event Action<int> CharacterChosen;

    /// <summary>닫힐 때 한 번(Api·Button·Escape·Chosen). 지금 캐릭터를 다시 고르면 Button.</summary>
    public event Action<CharacterSelectCloseReason> Closed;

    public bool IsOpen => Rules.IsOpen;

    public bool IsLocked => Rules.IsLocked;

    /// <summary>포커스된 카드 index(= 캐릭터 id). 카드가 없으면 -1.</summary>
    public int FocusedCharacterId => Rules.Focus;

    /// <summary>위에 열린 기본/코어 팝업(할 일 162). None = 없음.</summary>
    public CharacterSelectInfoTab InfoPopup => Rules.InfoPopup;

    /// <summary>
    /// 이 프레임 ESC 를 선택 패널이 쓰는가 — 열려 있거나, 이 프레임 ESC 로 닫혔다.
    /// 로비에서 ESC 를 쓰는 다른 메뉴는 이 값이 true 면 무시해야 같은 키로 두 개가 같이 움직이지 않는다.
    /// </summary>
    public static bool IsEscapeClaimed => s_openCount > 0 || s_escapeClaimedFrame == Time.frameCount;

    private CharacterSelectPanelRules Rules => _rules ??= new CharacterSelectPanelRules();

    private float Now => _previewClock?.Invoke() ?? Time.unscaledTime;

    private CharacterSelectSlotLayout Layout => new CharacterSelectSlotLayout(focusedSlot, previousSlot, nextSlot, sideScale);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_openCount = 0;
        s_escapeClaimedFrame = -1;
    }

    private void Awake()
    {
        if (backButton != null)
            backButton.onClick.AddListener(OnBackButton);
        if (confirmButton != null)
            confirmButton.onClick.AddListener(OnConfirmButton);
        if (basicInfoButton != null)
            basicInfoButton.onClick.AddListener(OnBasicInfoButton);
        if (coreInfoButton != null)
            coreInfoButton.onClick.AddListener(OnCoreInfoButton);
        if (infoPopupCloseButton != null)
            infoPopupCloseButton.onClick.AddListener(OnInfoPopupCloseButton);
        HideCardTemplate();
        ApplyInfoPopup();

        if (!Rules.IsOpen && content != null)
            content.SetActive(false);
    }

    private void OnDestroy()
    {
        if (backButton != null)
            backButton.onClick.RemoveListener(OnBackButton);
        if (confirmButton != null)
            confirmButton.onClick.RemoveListener(OnConfirmButton);
        if (basicInfoButton != null)
            basicInfoButton.onClick.RemoveListener(OnBasicInfoButton);
        if (coreInfoButton != null)
            coreInfoButton.onClick.RemoveListener(OnCoreInfoButton);
        if (infoPopupCloseButton != null)
            infoPopupCloseButton.onClick.RemoveListener(OnInfoPopupCloseButton);
        foreach (CharacterSelectCard card in _cards)
            if (card != null)
                card.Clicked -= OnCardClicked;
        SetCountedOpen(false);
    }

    private void Update()
    {
        if (!Rules.IsOpen)
            return;

        UpdateLockedPulse();
        UpdateMotion();

        // 결과 패널이 위에 열려 있거나 이 프레임 ESC 로 닫혔다 — 키 입력은 그쪽 것.
        if (LobbyResultPanel.IsEscapeClaimed)
        {
            _keyRepeat.Reset(); // 반복도 멈춘다 — 눌려 있던 이동 키는 새로 누를 때까지 무시
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            // 팝업이 열려 있으면 ESC 는 팝업만 닫는다(closeWithEscape 와 무관 — 팝업 닫기 관례).
            if ((closeWithEscape || Rules.IsPopupOpen) && keyboard.escapeKey.wasPressedThisFrame)
            {
                s_escapeClaimedFrame = Time.frameCount;
                switch (Rules.Escape(false))
                {
                    case CharacterSelectEscapeResult.ClosedPopup:
                        ApplyInfoPopup();
                        break;
                    case CharacterSelectEscapeResult.ClosedPanel:
                        ApplyClosed(CharacterSelectCloseReason.Escape);
                        break;
                }
                return;
            }

            // 팝업 뒤 입력(확정·이동·휠)은 막는다 — 닫은 뒤 눌려 있던 이동 키는 새로 누를 때까지 무시.
            if (Rules.IsPopupOpen)
            {
                _keyRepeat.Reset();
                return;
            }

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame ||
                keyboard.spaceKey.wasPressedThisFrame)
            {
                Confirm();
                return;
            }

            // 이동 키 — 누른 순간 1칸, 길게 누르면 대기 뒤 점점 짧아지는 간격마다 1칸(CharacterSelectKeyRepeat).
            bool backHeld = keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ||
                            keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed;
            bool forwardHeld = keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ||
                               keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed;
            _keyRepeat.DelaySeconds = keyRepeatDelaySeconds;
            _keyRepeat.StartIntervalSeconds = keyRepeatStartIntervalSeconds;
            _keyRepeat.MinIntervalSeconds = keyRepeatMinIntervalSeconds;
            _keyRepeat.RampSeconds = keyRepeatRampSeconds;
            int step = _keyRepeat.Tick(backHeld, forwardHeld, Time.unscaledTime);
            if (step != 0)
            {
                MoveFocus(step);
                return;
            }
        }

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            float wheel = mouse.scroll.ReadValue().y;
            if (wheel > 0f)
                MoveFocus(-1);
            else if (wheel < 0f)
                MoveFocus(1);
        }
    }

    /// <summary>
    /// 연다. 포커스 = <paramref name="currentCharacterId"/>(없거나 범위 밖이면 0번). <paramref name="isLocked"/> = 준비 중(구경만, 확정 거부).
    /// 이미 열려 있으면 값만 바꿔 다시 그린다. 카드는 로스터 수가 바뀌었으면 다시 만든다.
    /// </summary>
    public void Show(int currentCharacterId, bool isLocked)
    {
        BuildCards();
        Rules.Open(currentCharacterId, isLocked, _available);
        _lockedPulseUntil = -1f;
        _keyRepeat.Reset(); // 열 때 이미 눌려 있던 이동 키는 새로 누를 때까지 무시

        if (content != null)
            content.SetActive(true);
        ApplyInfoPopup(); // 다시 열면 팝업은 닫힌 채
        if (hintText != null)
            hintText.text = CharacterSelectUiStrings.Hint.TextOr(hintText.text);
        Redraw();
        SnapLayout();
        SetCountedOpen(true);
    }

    /// <summary>닫는다(Closed(Api)). 닫혀 있으면 아무것도 안 한다.</summary>
    public void Hide()
    {
        if (Rules.Close())
            ApplyClosed(CharacterSelectCloseReason.Api);
    }

    /// <summary>열린 채로 준비 상태가 바뀌었다 — 잠금 문구·확정 가능 여부를 다시 그린다. 닫혀 있으면 다음 Show 값이 이긴다.</summary>
    public void SetLocked(bool isLocked)
    {
        if (!Rules.IsOpen)
            return;
        if (Rules.SetLocked(isLocked))
        {
            _lockedPulseUntil = -1f;
            Redraw();
            SnapLayout();
        }
    }

    /// <summary>포커스를 옮긴다(끝에서 순환). 키·휠과 같은 길. 카드는 지금 보이는 자리에서 새 자리로 미끄러진다(이동 중이면 바로 새 목표로).</summary>
    public void MoveFocus(int delta)
    {
        int previous = Rules.Focus;
        if (Rules.MoveFocus(delta))
        {
            Redraw();
            AnimateLayout(previous);
        }
    }

    /// <summary>포커스된 카드로 확정(Enter·Space·가운데 카드 클릭·SELECT 셰브론 클릭과 같은 길). 이동 중에도 받는다 — 대상은 논리상 가운데(<see cref="FocusedCharacterId"/>).</summary>
    public CharacterSelectConfirmResult Confirm()
    {
        CharacterSelectConfirmResult result = Rules.Confirm();
        if (result == CharacterSelectConfirmResult.Chosen)
            CharacterChosen?.Invoke(Rules.Focus);
        else if (result == CharacterSelectConfirmResult.Locked)
            _lockedPulseUntil = Time.unscaledTime + lockedPulseSeconds;

        // 다른 캐릭터 = Chosen, 지금 캐릭터 = Button(뒤로가기와 같은 그냥 닫기).
        if (CharacterSelectPanelRules.TryGetCloseReason(result, out CharacterSelectCloseReason reason))
            ApplyClosed(reason);

        return result;
    }

    private void BuildCards()
    {
        HideCardTemplate(); // EditMode(프리뷰 씬)는 Awake 가 돌지 않는다
        int count = roster != null ? roster.Count : 0;
        if (_cards.Count == count && _cards.TrueForAll(c => c != null))
        {
            RebindCards();
            return;
        }

        foreach (CharacterSelectCard card in _cards)
        {
            if (card == null)
                continue;
            card.Clicked -= OnCardClicked;
            DestroyCard(card.gameObject);
        }
        _cards.Clear();

        CharacterSelectCard source = cardTemplate != null ? cardTemplate : cardPrefab;
        if (source == null || cardContainer == null)
        {
            if (count > 0)
                Debug.LogError("[CharacterSelectPanel] cardTemplate·cardPrefab·cardContainer 가 비어 있다 — 저작 메뉴로 프리팹을 다시 만들 것.", this);
            _available.Clear();
            return;
        }

        for (int i = 0; i < count; i++)
        {
            CharacterSelectCard card = Instantiate(source, cardContainer, false);
            card.Clicked += OnCardClicked;
            _cards.Add(card);
        }

        RebindCards();
    }

    /// <summary>프리팹에서 보이게 둔 가운데 카드 원본은 Play 에서 끈다 — 복제본과 겹쳐 보이지 않게.</summary>
    private void HideCardTemplate()
    {
        if (cardTemplate != null && cardTemplate.gameObject.activeSelf)
            cardTemplate.gameObject.SetActive(false);
    }

    private void RebindCards()
    {
        _available.Clear();
        for (int i = 0; i < _cards.Count; i++)
        {
            CharacterRoster.Entry entry = roster.Entries[i];
            bool available = roster.TryGetAvailableCharacter(i, out _);
            _available.Add(available);
            _cards[i].Bind(i, entry?.Portrait, entry?.DisplayName, available);
        }
    }

    /// <summary>겉모습(카드 모양·이름판·잠금 문구·셰브론 hover 아웃라인 잠금)을 논리 포커스에 맞춘다. 자리·크기·테두리는 SnapLayout/AnimateLayout.</summary>
    private void Redraw()
    {
        int count = _cards.Count;
        int focus = Rules.Focus;

        for (int i = 0; i < count; i++)
            _cards[i].SetState(CharacterSelectPanelRules.SlotOffset(i, focus, count), i == Rules.CurrentId, Rules.IsLocked);

        // 그리기 순서 — 포커스 카드를 맨 위로.
        if (focus >= 0 && focus < count)
            _cards[focus].transform.SetAsLastSibling();

        CharacterRoster.Entry focused = focus >= 0 && roster != null && focus < roster.Count ? roster.Entries[focus] : null;
        if (nameplatePortrait != null)
        {
            nameplatePortrait.sprite = focused?.Portrait;
            nameplatePortrait.enabled = focused?.Portrait != null;
        }
        if (nameplateName != null)
            nameplateName.text = focused?.DisplayName ?? string.Empty;

        if (lockedText != null)
        {
            lockedText.gameObject.SetActive(Rules.IsLocked);
            lockedText.text = CharacterSelectUiStrings.LockedNotice.TextOr(lockedText.text);
            lockedText.color = lockedTextColor;
            lockedText.rectTransform.localScale = Vector3.one;
        }

        // 잠금 중엔 셰브론 hover 아웃라인을 끈다 — 버튼은 interactable 그대로(클릭 = Confirm → Locked → 잠금 문구 강조).
        if (confirmButton != null && confirmButton.TryGetComponent(out UiHoverOutline confirmHover))
            confirmHover.Blocked = Rules.IsLocked;
    }

    private void UpdateLockedPulse()
    {
        if (lockedText == null || _lockedPulseUntil < 0f)
            return;

        float remaining = _lockedPulseUntil - Time.unscaledTime;
        if (remaining <= 0f || lockedPulseSeconds <= 0f)
        {
            _lockedPulseUntil = -1f;
            lockedText.color = lockedTextColor;
            lockedText.rectTransform.localScale = Vector3.one;
            return;
        }

        float t = remaining / lockedPulseSeconds; // 1 → 0
        lockedText.color = Color.Lerp(lockedTextColor, lockedPulseColor, t);
        lockedText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, lockedPulseScale, t);
    }

    // ── 카드 이동·확대 연출(할 일 60) — 시간 규칙은 CharacterSelectCardMotion ──

    private void ConfigureMotion()
    {
        _motion.MoveSeconds = moveSeconds;
        _motion.RingFadeSeconds = ringFadeSeconds;
        _motion.ScaleSeconds = focusScaleSeconds;
    }

    /// <summary>애니메이션 없이 다 선 상태로(테두리 켜짐·확대). 열 때·잠금 변경.</summary>
    private void SnapLayout()
    {
        ConfigureMotion();
        _motion.Snap();
        _ringCard = Rules.Focus;

        CharacterSelectSlotLayout layout = Layout;
        _tracks.Clear();
        for (int i = 0; i < _cards.Count; i++)
            _tracks.Add(CharacterSelectCardMotion.Snap(CharacterSelectPanelRules.SlotOffset(i, Rules.Focus, _cards.Count), layout));

        ApplyLayout(Now);
    }

    /// <summary>포커스가 <paramref name="previousFocus"/> 에서 바뀌었다 — 지금 보이는 자리에서 새 자리로 다시 출발(큐잉 없음).</summary>
    private void AnimateLayout(int previousFocus)
    {
        int count = _cards.Count;
        if (_tracks.Count != count)
        {
            SnapLayout();
            return;
        }

        ConfigureMotion();
        float now = Now;
        TickMotion(now);

        float progress = _motion.MoveProgress(now);
        float scale = _motion.FocusScale(now, focusScale);
        int focus = Rules.Focus;
        int direction = Math.Sign(CharacterSelectPanelRules.SlotOffset(focus, previousFocus, count));
        CharacterSelectSlotLayout layout = Layout;

        for (int i = 0; i < count; i++)
        {
            CharacterSelectCardTrack track = _tracks[i];
            _tracks[i] = CharacterSelectCardMotion.Retarget(track, track.Position(progress), track.Scale(progress, scale),
                CharacterSelectPanelRules.SlotOffset(i, focus, count), direction, layout);
        }

        _motion.Retarget(now); // 테두리는 지금 보여 주는 카드(_ringCard)에서 꺼진다
        ApplyLayout(now);
    }

    private void UpdateMotion()
    {
        if (_motion.Phase == CharacterSelectMotionPhase.Idle)
            return;

        float now = Now;
        TickMotion(now);
        ApplyLayout(now);
    }

    /// <summary>시간을 흘린다. 이동이 막 끝났으면 카드를 도착 자리에 고정(나가던 카드 끔)하고 테두리를 새 가운데로 옮긴다.</summary>
    private void TickMotion(float now)
    {
        CharacterSelectMotionPhase before = _motion.Phase;
        if (before == _motion.Tick(now) || before != CharacterSelectMotionPhase.Moving)
            return;

        for (int i = 0; i < _tracks.Count; i++)
            _tracks[i] = CharacterSelectCardMotion.Arrive(_tracks[i]);
        _ringCard = Rules.Focus;
    }

    private void ApplyLayout(float now)
    {
        float progress = _motion.MoveProgress(now);
        float scale = _motion.FocusScale(now, focusScale);
        float ring = _motion.RingAlpha(now);

        for (int i = 0; i < _cards.Count && i < _tracks.Count; i++)
        {
            CharacterSelectCard card = _cards[i];
            CharacterSelectCardTrack track = _tracks[i];
            if (card.gameObject.activeSelf != track.Active)
                card.gameObject.SetActive(track.Active);
            if (!track.Active)
                continue;

            var rect = (RectTransform)card.transform;
            rect.anchoredPosition = track.Position(progress);
            rect.localScale = Vector3.one * track.Scale(progress, scale);
            card.SetRingAlpha(i == _ringCard ? ring : 0f);
        }
    }

#if UNITY_EDITOR
    /// <summary>에디터 캡처 전용 — 연출 시계를 바꿔 끼우고(null = unscaledTime) 그 시각으로 한 번 그린다. 프리뷰 씬은 Update 가 돌지 않는다.</summary>
    public void SamplePreviewMotion(Func<float> clock)
    {
        _previewClock = clock;
        if (Rules.IsOpen && clock != null)
        {
            float now = Now;
            TickMotion(now);
            ApplyLayout(now);
        }
    }
#endif

    private void OnCardClicked(int index)
    {
        if (!Rules.IsOpen || LobbyResultPanel.IsEscapeClaimed)
            return;

        // 가운데(포커스) 카드 = 확정, 위·아래 카드 = 그 카드로 포커스 이동.
        int previous = Rules.Focus;
        if (index == previous)
            Confirm();
        else if (Rules.FocusCard(index))
        {
            Redraw();
            AnimateLayout(previous);
        }
    }

    /// <summary>SELECT 셰브론 버튼 — Space 와 같은 Confirm. 키 입력과 같은 조건(열림·결과 패널 위 아님)에서만 받는다.</summary>
    private void OnConfirmButton()
    {
        ClearSelection(confirmButton);

        if (!Rules.IsOpen || LobbyResultPanel.IsEscapeClaimed)
            return;

        Confirm();
    }

    private void OnBackButton()
    {
        if (Rules.HandleBackButton())
            ApplyClosed(CharacterSelectCloseReason.Button);
    }

    // ── 기본/코어 정보 팝업(할 일 162) — 판단은 Rules(OpenInfoPopup·CloseInfoPopup·Escape) ──

    private void OnBasicInfoButton() => OpenInfoPopup(CharacterSelectInfoTab.Basic);

    private void OnCoreInfoButton() => OpenInfoPopup(CharacterSelectInfoTab.Core);

    /// <summary>기본/코어 버튼과 같은 길(테스트·게임패드 연결용). 키 입력과 같은 조건(열림·결과 패널 위 아님)에서만 연다.</summary>
    public void OpenInfoPopup(CharacterSelectInfoTab tab)
    {
        ClearSelection(tab == CharacterSelectInfoTab.Core ? coreInfoButton : basicInfoButton);
        if (LobbyResultPanel.IsEscapeClaimed || !Rules.OpenInfoPopup(tab))
            return;

        _keyRepeat.Reset();
        ApplyInfoPopup();
    }

    /// <summary>팝업 X 버튼과 같은 길. 팝업만 닫고 패널은 그대로.</summary>
    public void CloseInfoPopup()
    {
        ClearSelection(infoPopupCloseButton);
        if (Rules.CloseInfoPopup())
            ApplyInfoPopup();
    }

    private void OnInfoPopupCloseButton() => CloseInfoPopup();

    private void ApplyInfoPopup()
    {
        if (infoPopup != null && infoPopup.activeSelf != Rules.IsPopupOpen)
            infoPopup.SetActive(Rules.IsPopupOpen);
    }

    /// <summary>Navigation None 이라 클릭으로 선택되지 않지만, 혹시 남아 있으면 지운다 — 남으면 다음 Enter(Submit) 가 한 번 더 누른다.</summary>
    private static void ClearSelection(Button button)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null && button != null && eventSystem.currentSelectedGameObject == button.gameObject)
            eventSystem.SetSelectedGameObject(null);
    }

    private void ApplyClosed(CharacterSelectCloseReason reason)
    {
        ApplyInfoPopup(); // Rules 가 닫히며 팝업도 닫았다
        if (content != null)
            content.SetActive(false); // UiModalBlocker.OnDisable 가 입력 차단을 푼다
        _keyRepeat.Reset();
        SetCountedOpen(false);
        Closed?.Invoke(reason);
    }

    private void SetCountedOpen(bool open)
    {
        if (_countedOpen == open)
            return;

        _countedOpen = open;
        s_openCount += open ? 1 : -1;
        if (s_openCount < 0)
            s_openCount = 0;
    }

    private static void DestroyCard(GameObject card)
    {
        if (Application.isPlaying)
            Destroy(card);
        else
            DestroyImmediate(card); // 에디터 미리보기(프리뷰 씬)
    }
}
