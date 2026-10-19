using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 로비 캐릭터 선택 패널(할 일 48.2) — 로스터 엔트리마다 카드를 만들고, 오른쪽 비스듬한 세로 스택(위 = 이전, 가운데 = 포커스, 아래 = 다음)으로 보여 준다.
/// 판단은 <see cref="CharacterSelectPanelRules"/>. 🔴 네트워크를 모른다 — 고르면 <see cref="CharacterChosen"/> 만 내고,
/// 서버 요청(RequestCharacterServerRpc)·준비 상태 갱신(<see cref="SetLocked"/>)은 붙이는 쪽(48 C12) 책임. 사용법 = Docs/tech/character-select-panel.md.
///
/// - 열려 있는 동안 <c>content</c> 의 <see cref="UiModalBlocker"/> 가 로컬 플레이어 입력을 막고, 불투명 배경이 뒤 클릭을 막는다.
/// - 닫기 = 뒤로가기 버튼·ESC. <see cref="LobbyResultPanel.IsEscapeClaimed"/> 면 ESC·키 입력을 무시한다(결과 패널이 위에 있다).
/// </summary>
[DisallowMultipleComponent]
public sealed class CharacterSelectPanel : MonoBehaviour
{
    [Tooltip("켜고 끄는 본체. UiModalBlocker 가 붙어 있다. 프리팹 루트(Canvas)는 늘 켜 둔다.")]
    [SerializeField] private GameObject content;
    [SerializeField] private CharacterRoster roster;
    [SerializeField] private CharacterSelectCard cardPrefab;
    [Tooltip("카드 부모(화면 가운데 기준). 카드는 런타임에 로스터 수만큼 복제한다 — 디자인용 카드를 두지 말 것.")]
    [SerializeField] private RectTransform cardContainer;
    [SerializeField] private Button backButton;

    [Header("Nameplate (포커스된 캐릭터)")]
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
    private CharacterSelectPanelRules _rules;
    private bool _countedOpen;
    private float _lockedPulseUntil = -1f;

    /// <summary>다른 캐릭터를 골랐다(캐릭터 id = 로스터 index). 곧이어 <see cref="Closed"/>(<see cref="CharacterSelectCloseReason.Chosen"/>).</summary>
    public event Action<int> CharacterChosen;

    /// <summary>닫힐 때 한 번(Api·Button·Escape·Chosen·Unchanged).</summary>
    public event Action<CharacterSelectCloseReason> Closed;

    public bool IsOpen => Rules.IsOpen;

    public bool IsLocked => Rules.IsLocked;

    /// <summary>포커스된 카드 index(= 캐릭터 id). 카드가 없으면 -1.</summary>
    public int FocusedCharacterId => Rules.Focus;

    /// <summary>
    /// 이 프레임 ESC 를 선택 패널이 쓰는가 — 열려 있거나, 이 프레임 ESC 로 닫혔다.
    /// 로비에서 ESC 를 쓰는 다른 메뉴는 이 값이 true 면 무시해야 같은 키로 두 개가 같이 움직이지 않는다.
    /// </summary>
    public static bool IsEscapeClaimed => s_openCount > 0 || s_escapeClaimedFrame == Time.frameCount;

    private CharacterSelectPanelRules Rules => _rules ??= new CharacterSelectPanelRules();

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

        if (!Rules.IsOpen && content != null)
            content.SetActive(false);
    }

    private void OnDestroy()
    {
        if (backButton != null)
            backButton.onClick.RemoveListener(OnBackButton);
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

        // 결과 패널이 위에 열려 있거나 이 프레임 ESC 로 닫혔다 — 키 입력은 그쪽 것.
        if (LobbyResultPanel.IsEscapeClaimed)
            return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (closeWithEscape && keyboard.escapeKey.wasPressedThisFrame)
            {
                s_escapeClaimedFrame = Time.frameCount;
                if (Rules.HandleEscape(false))
                    ApplyClosed(CharacterSelectCloseReason.Escape);
                return;
            }

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame ||
                keyboard.spaceKey.wasPressedThisFrame)
            {
                Confirm();
                return;
            }

            if (keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame ||
                keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame)
            {
                MoveFocus(-1);
                return;
            }

            if (keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame ||
                keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
            {
                MoveFocus(1);
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

        if (content != null)
            content.SetActive(true);
        if (hintText != null)
            hintText.text = CharacterSelectUiStrings.Hint.TextOr(hintText.text);
        Redraw();
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
        }
    }

    /// <summary>포커스를 옮긴다(끝에서 순환). 키·휠과 같은 길.</summary>
    public void MoveFocus(int delta)
    {
        if (Rules.MoveFocus(delta))
            Redraw();
    }

    /// <summary>포커스된 카드로 확정(Enter·Space·가운데 카드 클릭과 같은 길).</summary>
    public CharacterSelectConfirmResult Confirm()
    {
        CharacterSelectConfirmResult result = Rules.Confirm();
        switch (result)
        {
            case CharacterSelectConfirmResult.Chosen:
                int id = Rules.Focus;
                CharacterChosen?.Invoke(id);
                ApplyClosed(CharacterSelectCloseReason.Chosen);
                break;
            case CharacterSelectConfirmResult.Unchanged:
                ApplyClosed(CharacterSelectCloseReason.Unchanged);
                break;
            case CharacterSelectConfirmResult.Locked:
                _lockedPulseUntil = Time.unscaledTime + lockedPulseSeconds;
                break;
        }

        return result;
    }

    private void BuildCards()
    {
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

        if (cardPrefab == null || cardContainer == null)
        {
            if (count > 0)
                Debug.LogError("[CharacterSelectPanel] cardPrefab·cardContainer 가 비어 있다 — 저작 메뉴로 프리팹을 다시 만들 것.", this);
            _available.Clear();
            return;
        }

        for (int i = 0; i < count; i++)
        {
            CharacterSelectCard card = Instantiate(cardPrefab, cardContainer, false);
            card.Clicked += OnCardClicked;
            _cards.Add(card);
        }

        RebindCards();
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

    private void Redraw()
    {
        int count = _cards.Count;
        int focus = Rules.Focus;

        for (int i = 0; i < count; i++)
        {
            CharacterSelectCard card = _cards[i];
            int offset = CharacterSelectPanelRules.SlotOffset(i, focus, count);
            bool visible = offset >= -1 && offset <= 1;
            card.gameObject.SetActive(visible);
            if (!visible)
                continue;

            var rect = (RectTransform)card.transform;
            rect.anchoredPosition = offset == 0 ? focusedSlot : offset < 0 ? previousSlot : nextSlot;
            rect.localScale = Vector3.one * (offset == 0 ? 1f : sideScale);
            card.SetState(offset, i == Rules.CurrentId, Rules.IsLocked);
        }

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

    private void OnCardClicked(int index)
    {
        if (!Rules.IsOpen || LobbyResultPanel.IsEscapeClaimed)
            return;

        // 가운데(포커스) 카드 = 확정, 위·아래 카드 = 그 카드로 포커스 이동.
        if (index == Rules.Focus)
            Confirm();
        else if (Rules.FocusCard(index))
            Redraw();
    }

    private void OnBackButton()
    {
        if (Rules.HandleBackButton())
            ApplyClosed(CharacterSelectCloseReason.Button);
    }

    private void ApplyClosed(CharacterSelectCloseReason reason)
    {
        if (content != null)
            content.SetActive(false); // UiModalBlocker.OnDisable 가 입력 차단을 푼다
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
