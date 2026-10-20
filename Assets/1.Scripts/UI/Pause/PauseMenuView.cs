using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 인게임 ESC 일시정지 메뉴 + 종료 확인 팝업(PLAN-pause-menu — SVN r466 03_PauseMenu · 02_ExitConfirm 아트).
/// 화면 전환(메뉴 ↔ 설정 ↔ 팝업)은 <see cref="MapSceneManager"/> 가 정한다 — 이 컴포넌트는 버튼을 신호로 바꾸고 팝업 선택 상태만 가진다.
///
/// 🔴 <b>로비로 나가기·게임 종료의 실제 처리는 여기서 하지 않는다</b>(경석 10-20 — 은희가 만들어 연결).
///    ✓ 를 누르면 <see cref="ExitToDesktopRequested"/> / <see cref="ExitToLobbyRequested"/> 만 낸다(인스펙터 UnityEvent 도 함께).
///    아무것도 연결 안 됐으면 경고 로그만 남긴다. 연결 방법: <c>Docs/tech/handoff-pause-menu.md</c>.
/// </summary>
public sealed class PauseMenuView : MonoBehaviour
{
    public enum ExitChoice { None, Desktop, Lobby }

    [Header("메뉴")]
    [SerializeField] private GameObject menuRoot;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private Button pressAnyButton;   // 아래 "Press Any Button" 캡슐 — 계속하기와 같다

    [Header("종료 확인 팝업")]
    [SerializeField] private GameObject exitRoot;
    [SerializeField] private Button desktopButton;
    [SerializeField] private Button lobbyButton;
    [SerializeField] private Button cancelButton;     // ✗ — 메뉴로
    [SerializeField] private Button confirmButton;    // ✓ — 고른 쪽 신호
    [SerializeField] private Color choiceTextNormal = new Color(0.75f, 0.77f, 0.77f, 1f);   // 목업 C25 M20 Y20
    [SerializeField] private Color choiceTextSelected = Color.white;
    [SerializeField] private GameObject desktopSelectedRing;
    [SerializeField] private GameObject lobbySelectedRing;

    [Header("나가기 연결(은희) — 코드 이벤트와 같이 불린다")]
    [SerializeField] private UnityEvent onExitToDesktop = new UnityEvent();
    [SerializeField] private UnityEvent onExitToLobby = new UnityEvent();

    /// <summary>계속하기(또는 아래 캡슐) — 메뉴를 닫아 게임으로.</summary>
    public event Action ContinueRequested;
    /// <summary>설정 — 설정창 열기.</summary>
    public event Action SettingsRequested;
    /// <summary>바탕화면 + ✓. 처리(게임 종료·호스트/클라 예외)는 연결하는 쪽이 한다.</summary>
    public event Action ExitToDesktopRequested;
    /// <summary>로비 + ✓. 처리(호스트 = 전원 로비 / 클라 = 혼자 세션 구성 화면)는 연결하는 쪽이 한다.</summary>
    public event Action ExitToLobbyRequested;

    public UnityEvent OnExitToDesktop => onExitToDesktop;
    public UnityEvent OnExitToLobby => onExitToLobby;

    public ExitChoice Choice { get; private set; }
    public bool IsExitConfirmOpen => exitRoot != null && exitRoot.activeSelf;

    private void Awake()
    {
        Bind(continueButton, () => ContinueRequested?.Invoke());
        Bind(pressAnyButton, () => ContinueRequested?.Invoke());
        Bind(settingsButton, () => SettingsRequested?.Invoke());
        Bind(quitButton, ShowExitConfirm);
        Bind(desktopButton, () => Select(ExitChoice.Desktop));
        Bind(lobbyButton, () => Select(ExitChoice.Lobby));
        Bind(cancelButton, ShowMenu);
        Bind(confirmButton, Confirm);
    }

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

    /// <summary>종료 확인 팝업. 열 때마다 선택을 비운다 — ✓ 는 고르기 전까지 비활성.</summary>
    public void ShowExitConfirm()
    {
        SetActive(menuRoot, false);
        SetActive(exitRoot, true);
        Select(ExitChoice.None);
    }

    private void Select(ExitChoice choice)
    {
        Choice = choice;
        Paint(desktopButton, desktopSelectedRing, choice == ExitChoice.Desktop);
        Paint(lobbyButton, lobbySelectedRing, choice == ExitChoice.Lobby);
        if (confirmButton != null) confirmButton.interactable = choice != ExitChoice.None;
    }

    private void Paint(Button button, GameObject ring, bool selected)
    {
        SetActive(ring, selected);
        if (button == null) return;
        foreach (TMP_Text t in button.GetComponentsInChildren<TMP_Text>(true))
            t.color = selected ? choiceTextSelected : choiceTextNormal;
    }

    private void Confirm()
    {
        switch (Choice)
        {
            case ExitChoice.Desktop:
                Raise("바탕화면", ExitToDesktopRequested, onExitToDesktop);
                break;
            case ExitChoice.Lobby:
                Raise("로비", ExitToLobbyRequested, onExitToLobby);
                break;
        }
    }

    private static void Raise(string label, Action action, UnityEvent unityEvent)
    {
        bool wired = action != null || unityEvent.GetPersistentEventCount() > 0;
        action?.Invoke();
        unityEvent.Invoke();
        if (!wired)
            Debug.LogWarning($"[PauseMenu] '{label}' 나가기가 아직 연결되지 않았다 — Docs/tech/handoff-pause-menu.md 참고.");
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
