using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// ESC 메뉴 + 그만두기 확인 팝업(PLAN-pause-menu — SVN r466 03_PauseMenu · 02_ExitConfirm 아트).
/// 화면 전환(메뉴 ↔ 설정 ↔ 팝업)과 나가기 처리는 <see cref="GameMenuController"/> 가 정한다(PLAN-settings-ddol) —
/// 이 컴포넌트는 버튼을 신호로 바꾸고 팝업 선택 상태만 가진다.
/// <para>
/// 팝업 = <b>바탕화면</b>(게임 종료) / <b>타이틀</b>(타이틀로 돌아가기) 두 버튼 — 누르면 **바로** 실행한다(경석 10-22 Play: 다시 묻지 않음).
/// 아트의 ✗/✓ 는 숨긴다. 취소는 ESC(→ 메뉴). 로비·인게임 같다.
/// 프리팹의 오른쪽 버튼은 이름이 <c>Lobby</c> 지만 글자를 "타이틀"로 바꿔 쓴다.
/// </para>
/// </summary>
public sealed class PauseMenuView : MonoBehaviour
{
    [Header("메뉴")]
    [SerializeField] private GameObject menuRoot;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;

    [Header("그만두기 팝업")]
    [SerializeField] private GameObject exitRoot;
    [SerializeField] private Button desktopButton;    // 바탕화면 — 게임 종료
    [SerializeField] private Button lobbyButton;      // 오른쪽 — "타이틀"
    [SerializeField] private Button cancelButton;     // ✗ — 숨김(취소 = ESC)
    [SerializeField] private Button confirmButton;    // ✓ — 숨김(버튼이 바로 실행)
    [SerializeField] private GameObject desktopSelectedRing;
    [SerializeField] private GameObject lobbySelectedRing;

    /// <summary>계속하기 — 메뉴를 닫아 게임으로.</summary>
    public event Action ContinueRequested;
    /// <summary>설정 — 설정창 열기.</summary>
    public event Action SettingsRequested;
    /// <summary>그만두기 — 확인 팝업을 띄운다(문구는 받는 쪽이 <see cref="ShowExitConfirm"/> 로 정한다).</summary>
    public event Action QuitRequested;
    /// <summary>바탕화면 — 게임 종료.</summary>
    public event Action ExitToDesktopRequested;
    /// <summary>타이틀 — 세션을 떠나 타이틀로.</summary>
    public event Action ExitToTitleRequested;

    public bool IsExitConfirmOpen => exitRoot != null && exitRoot.activeSelf;

    // 코드가 넣는 팝업 문구(호스트 경고 등) — 언어가 바뀌면 다시 넣는다(LocalizedText 는 키를 비워 손을 떼게 한다).
    private UiString? _confirmMessage;

    private void Awake()
    {
        Bind(continueButton, () => ContinueRequested?.Invoke());
        Bind(settingsButton, () => SettingsRequested?.Invoke());
        Bind(quitButton, () => QuitRequested?.Invoke());
        Bind(desktopButton, () => ExitToDesktopRequested?.Invoke());
        Bind(lobbyButton, () => ExitToTitleRequested?.Invoke());
    }

    private void OnEnable() => Loc.Changed += ApplyTexts;
    private void OnDisable() => Loc.Changed -= ApplyTexts;

    /// <summary>메뉴 화면(팝업 닫힘). ESC 로 처음 열 때·팝업 ✗·설정창 닫기 뒤.</summary>
    public void ShowMenu()
    {
        SetActive(menuRoot, true);
        SetActive(exitRoot, false);
    }

    /// <summary>메뉴·팝업 둘 다 숨김 — 설정창이 떠 있는 동안.</summary>
    public void HideAll()
    {
        SetActive(menuRoot, false);
        SetActive(exitRoot, false);
    }

    /// <summary>그만두기 팝업 — 바탕화면 / 타이틀 두 버튼만(✗/✓·선택 링 숨김). <paramref name="message"/> = 가운데 문구(호스트 경고 등).</summary>
    public void ShowExitConfirm(UiString message)
    {
        _confirmMessage = message;
        SetActive(menuRoot, false);
        SetActive(exitRoot, true);
        SetActive(cancelButton != null ? cancelButton.gameObject : null, false);
        SetActive(confirmButton != null ? confirmButton.gameObject : null, false);
        SetActive(desktopSelectedRing, false);
        SetActive(lobbySelectedRing, false);
        ApplyTexts();   // 활성화 뒤 — LocalizedText.OnEnable 이 저작 글자로 덮은 다음에 넣는다
    }

    private void ApplyTexts()
    {
        if (lobbyButton != null)
            SetText(lobbyButton.GetComponentInChildren<TMP_Text>(true), UiStrings.ExitTitleChoice);

        if (exitRoot == null || !_confirmMessage.HasValue) return;
        Transform t = exitRoot.transform.Find("Message");
        TMP_Text message = t != null ? t.GetComponent<TMP_Text>() : null;
        SetText(message, _confirmMessage.Value);
        if (message != null)
        {
            // 호스트 경고는 "정말 종료하시겠습니까?" 보다 길다 — 칸(700) 안에 줄여 넣는다.
            message.enableAutoSizing = true;
            message.fontSizeMax = 60f;
            message.fontSizeMin = 30f;
        }
    }

    private static void SetText(TMP_Text text, UiString value)
    {
        if (text == null) return;
        LocalizedText localized = text.GetComponent<LocalizedText>();
        if (localized != null && !string.IsNullOrEmpty(localized.Key)) localized.Key = string.Empty;   // 저작 키가 다시 덮지 않게
        text.text = value.Text;
    }

    private static void Bind(Button button, UnityAction action)
    {
        if (button == null) return;
        button.onClick.AddListener(action);
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }
}
