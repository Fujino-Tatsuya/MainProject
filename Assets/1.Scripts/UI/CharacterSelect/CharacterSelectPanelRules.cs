using System.Collections.Generic;

/// <summary>캐릭터 선택 패널이 닫힌 이유.</summary>
public enum CharacterSelectCloseReason
{
    Api = 0,       // 호출 측 Hide()
    Button = 1,    // 그냥 닫기 — 뒤로가기 버튼, 또는 지금 캐릭터를 다시 골랐다(CharacterChosen 없음)
    Escape = 2,    // ESC
    Chosen = 3     // 다른 캐릭터를 골랐다(CharacterChosen 이 먼저 한 번 나간다)
}

/// <summary>선택 확정(Enter·Space·가운데 카드 클릭) 판단 결과.</summary>
public enum CharacterSelectConfirmResult
{
    NotOpen = 0,     // 닫혀 있다 — 아무것도 안 한다
    Empty = 1,       // 카드가 없다(로스터 비었음)
    Locked = 2,      // 준비 중 — 거부, 잠금 문구 강조
    Unavailable = 3, // 로스터 Available=false — 거부
    SameAsCurrent = 4, // 지금 캐릭터 — CharacterChosen 없이 그냥 닫는다(Closed(Button))
    Chosen = 5,      // 다른 캐릭터 — CharacterChosen(id) 한 번 내고 닫는다
    PopupOpen = 6    // 기본/코어 팝업이 위에 열려 있다 — 아무것도 안 한다(할 일 162)
}

/// <summary>오른쪽 위 정보 버튼(할 일 162). 지금은 둘 다 "미구현" 팝업만 띄운다.</summary>
public enum CharacterSelectInfoTab
{
    None = 0,
    Basic = 1, // 기본
    Core = 2   // 코어
}

/// <summary>ESC 한 번이 한 일.</summary>
public enum CharacterSelectEscapeResult
{
    Ignored = 0,     // 닫혀 있거나 다른 패널(결과 패널)이 이 프레임 ESC 를 썼다
    ClosedPopup = 1, // 기본/코어 팝업만 닫았다 — 패널은 그대로
    ClosedPanel = 2  // 패널을 닫았다(Closed(Escape))
}

/// <summary>
/// 캐릭터 선택 패널(할 일 48.2)의 열고 닫기·포커스 이동·선택 확정·잠금·ESC 판단(MonoBehaviour 밖 — EditMode 테스트 대상).
/// 로스터·네트워크는 모른다 — 카드마다 "고를 수 있는가"만 받는다. 카드 index = 로스터 index = 캐릭터 id.
/// </summary>
public sealed class CharacterSelectPanelRules
{
    private readonly List<bool> _available = new List<bool>();

    public bool IsOpen { get; private set; }

    /// <summary>준비 상태 — 잠겨 있으면 포커스 이동(구경)만 되고 확정은 거부된다.</summary>
    public bool IsLocked { get; private set; }

    /// <summary>열 때 받은 지금 캐릭터 id(범위 밖일 수 있다).</summary>
    public int CurrentId { get; private set; } = -1;

    /// <summary>포커스된 카드 index. 카드가 없으면 -1.</summary>
    public int Focus { get; private set; } = -1;

    public int Count => _available.Count;

    /// <summary>위에 열린 기본/코어 팝업(할 일 162). None = 없음. 열려 있으면 ESC 는 팝업만 닫고 포커스 이동·확정은 막힌다.</summary>
    public CharacterSelectInfoTab InfoPopup { get; private set; }

    public bool IsPopupOpen => InfoPopup != CharacterSelectInfoTab.None;

    /// <summary>
    /// 연다. 포커스 = 지금 캐릭터(없거나 범위 밖이면 0번). 이미 열려 있으면 값만 바꿔 다시 연 것으로 친다(팝업은 닫힌다).
    /// <paramref name="available"/> = 카드마다 고를 수 있는가(로스터 순서).
    /// </summary>
    public void Open(int currentId, bool isLocked, IReadOnlyList<bool> available)
    {
        _available.Clear();
        if (available != null)
            _available.AddRange(available);

        IsOpen = true;
        IsLocked = isLocked;
        CurrentId = currentId;
        Focus = InitialFocus(currentId, Count);
        InfoPopup = CharacterSelectInfoTab.None;
    }

    /// <summary>닫는다(팝업도). 열려 있었으면 true(닫힘 이벤트를 낼지 판단용).</summary>
    public bool Close()
    {
        if (!IsOpen)
            return false;

        IsOpen = false;
        InfoPopup = CharacterSelectInfoTab.None;
        return true;
    }

    /// <summary>기본/코어 버튼 — 패널이 열려 있으면 그 팝업을 연다(이미 열려 있으면 바꾼다). 열었으면 true.</summary>
    public bool OpenInfoPopup(CharacterSelectInfoTab tab)
    {
        if (!IsOpen || tab == CharacterSelectInfoTab.None)
            return false;

        InfoPopup = tab;
        return true;
    }

    /// <summary>팝업 닫기(X 버튼). 열려 있었으면 true.</summary>
    public bool CloseInfoPopup()
    {
        if (!IsPopupOpen)
            return false;

        InfoPopup = CharacterSelectInfoTab.None;
        return true;
    }

    /// <summary>
    /// ESC — 다른 패널(로비 결과 패널)이 이 프레임 ESC 를 썼으면 무시, 팝업이 열려 있으면 팝업만 닫고, 아니면 패널을 닫는다.
    /// </summary>
    public CharacterSelectEscapeResult Escape(bool claimedByOther)
    {
        if (claimedByOther || !IsOpen)
            return CharacterSelectEscapeResult.Ignored;
        if (CloseInfoPopup())
            return CharacterSelectEscapeResult.ClosedPopup;
        Close();
        return CharacterSelectEscapeResult.ClosedPanel;
    }

    /// <summary>준비 상태가 바뀌었다(열린 채로). 값이 바뀌었으면 true.</summary>
    public bool SetLocked(bool isLocked)
    {
        if (IsLocked == isLocked)
            return false;

        IsLocked = isLocked;
        return true;
    }

    /// <summary>
    /// ESC 로 패널이 닫혔으면 true(<see cref="Escape"/> 의 ClosedPanel). 팝업이 열려 있으면 팝업만 닫고 false.
    /// </summary>
    public bool HandleEscape(bool claimedByOther) => Escape(claimedByOther) == CharacterSelectEscapeResult.ClosedPanel;

    /// <summary>뒤로가기 버튼. 열려 있으면 닫고 true.</summary>
    public bool HandleBackButton() => Close();

    /// <summary>포커스를 <paramref name="delta"/> 칸 옮긴다(끝에서 순환). 포커스가 바뀌었으면 true. 팝업이 열려 있으면 무시.</summary>
    public bool MoveFocus(int delta)
    {
        if (!IsOpen || IsPopupOpen || Count == 0 || delta == 0)
            return false;

        return SetFocus(Wrap(Focus + delta, Count));
    }

    /// <summary>포커스를 그 카드로(옆 카드 클릭). 범위 밖·팝업 열림이면 무시. 포커스가 바뀌었으면 true.</summary>
    public bool FocusCard(int index)
    {
        if (!IsOpen || IsPopupOpen || index < 0 || index >= Count)
            return false;

        return SetFocus(index);
    }

    public bool IsAvailable(int index) => index >= 0 && index < Count && _available[index];

    /// <summary>
    /// 포커스된 카드로 확정. <see cref="CharacterSelectConfirmResult.Chosen"/>·<see cref="CharacterSelectConfirmResult.SameAsCurrent"/> 면 닫힌다.
    /// 팝업이 열려 있으면 <see cref="CharacterSelectConfirmResult.PopupOpen"/>(아무것도 안 함).
    /// </summary>
    public CharacterSelectConfirmResult Confirm()
    {
        if (IsOpen && IsPopupOpen)
            return CharacterSelectConfirmResult.PopupOpen;
        CharacterSelectConfirmResult result = Evaluate(IsOpen, Count, Focus, CurrentId, IsLocked, IsAvailable(Focus));
        if (TryGetCloseReason(result, out _))
            IsOpen = false;
        return result;
    }

    /// <summary>
    /// 확정 결과가 패널을 닫으면 그 닫힘 사유. 다른 캐릭터 = Chosen, 지금 캐릭터 = Button(뒤로가기와 같은 그냥 닫기).
    /// </summary>
    public static bool TryGetCloseReason(CharacterSelectConfirmResult result, out CharacterSelectCloseReason reason)
    {
        switch (result)
        {
            case CharacterSelectConfirmResult.Chosen:
                reason = CharacterSelectCloseReason.Chosen;
                return true;
            case CharacterSelectConfirmResult.SameAsCurrent:
                reason = CharacterSelectCloseReason.Button;
                return true;
            default:
                reason = default;
                return false;
        }
    }

    /// <summary>
    /// 확정 판단 — 순서: 닫힘 → 카드 없음 → 잠금 → 고를 수 없음 → 지금 캐릭터 → 선택.
    /// 잠금을 먼저 본다(준비 중이면 무엇을 눌러도 "준비를 해제해야" 안내).
    /// </summary>
    public static CharacterSelectConfirmResult Evaluate(bool isOpen, int count, int focus, int currentId, bool isLocked, bool focusAvailable)
    {
        if (!isOpen)
            return CharacterSelectConfirmResult.NotOpen;
        if (count <= 0 || focus < 0 || focus >= count)
            return CharacterSelectConfirmResult.Empty;
        if (isLocked)
            return CharacterSelectConfirmResult.Locked;
        if (!focusAvailable)
            return CharacterSelectConfirmResult.Unavailable;
        if (focus == currentId)
            return CharacterSelectConfirmResult.SameAsCurrent;
        return CharacterSelectConfirmResult.Chosen;
    }

    /// <summary>처음 포커스 — 지금 캐릭터가 범위 안이면 그 카드, 아니면 0번. 카드가 없으면 -1.</summary>
    public static int InitialFocus(int currentId, int count)
    {
        if (count <= 0)
            return -1;
        return currentId >= 0 && currentId < count ? currentId : 0;
    }

    /// <summary>0..count-1 로 순환.</summary>
    public static int Wrap(int index, int count)
    {
        if (count <= 0)
            return -1;
        int wrapped = index % count;
        return wrapped < 0 ? wrapped + count : wrapped;
    }

    /// <summary>
    /// 카드 자리 — 포커스 기준 순환 거리. 0 = 가운데(포커스), -1 = 위(이전), +1 = 아래(다음), 그 밖은 화면에 없다.
    /// 2장이면 다른 카드는 +1(아래). 3장이면 나머지 둘이 -1·+1.
    /// </summary>
    public static int SlotOffset(int index, int focus, int count)
    {
        if (count <= 0)
            return 0;
        int offset = Wrap(index - focus, count);
        return offset > count / 2 ? offset - count : offset;
    }

    private bool SetFocus(int index)
    {
        if (Focus == index)
            return false;

        Focus = index;
        return true;
    }
}
