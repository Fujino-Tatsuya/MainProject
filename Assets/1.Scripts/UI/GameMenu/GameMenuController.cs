using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 설정창 + ESC 메뉴 — 타이틀부터 끝까지 하나만 산다(DDOL, PLAN-settings-ddol). <c>GameMenu.prefab</c> 루트에 붙는다.
/// <para>
/// 루트는 항상 켜져 있고(ESC 를 받아야 한다) <see cref="content"/> 만 켜고 끈다. 씬 모드(<see cref="GameMenuRules.ResolveMode"/>):
/// 타이틀 = 열지 않음(10-26 타이틀 설정·나가기 삭제 — 설정·종료는 로비 ESC 메뉴) ·
/// 로비·인게임 = 계속·설정·그만두기(팝업의 바탕화면 / 타이틀을 누르면 바로 실행, ESC = 메뉴로) · 그 밖(로딩·데모 연결 대기) = 열지 않음.
/// </para>
/// 🔴 서버·씬 전환은 여기서 하지 않는다 — 타이틀로 = <see cref="GameManager.LeaveSessionToTitle"/>, 게임 종료 = <see cref="GameManager.QuitGame"/>.
/// 🔴 EventSystem 을 이 루트에 두지 말 것 — <see cref="PersistentEventSystem"/> 이 다른 EventSystem 의 GameObject 를 통째로 지운다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-1000)]   // Update 가 EventSystem 보다 먼저 — ESC 로 드롭다운이 닫히기 전에 "펼쳐져 있었다"를 잡는다
public sealed class GameMenuController : MonoBehaviour
{
    public static GameMenuController Instance { get; private set; }

    [SerializeField] private Canvas canvas;
    [SerializeField] private CanvasScaler scaler;
    [Tooltip("켜고 끄는 대상 — 반투명 검정 + UiModalBlocker(로컬 플레이어 입력 차단).")]
    [SerializeField] private GameObject content;
    [SerializeField] private PauseMenuView pauseMenu;
    [SerializeField] private SettingsBodyView settingsBody;
    [Tooltip("Overlay 정렬 — 캐릭터 선택 190 · 로비 결과 200 · CombatHUD 0 위, 로딩 1000 · 연결 안내 30000 아래.")]
    [SerializeField] private int overlaySortingOrder = 900;

    public GameMenuMode Mode { get; private set; } = GameMenuMode.None;
    public GameMenuScreen Screen { get; private set; } = GameMenuScreen.Closed;
    public bool IsOpen => Screen != GameMenuScreen.Closed;
    public Button SettingsCloseButton => settingsBody != null ? settingsBody.CloseButton : null;

    private TMP_Dropdown[] _dropdowns = Array.Empty<TMP_Dropdown>();
    private bool _dropdownOpenAtFrameStart;

    /// <summary>
    /// 이 프레임 시작 때 설정창 드롭다운 목록이 펼쳐져 있었다 — 이 프레임 ESC 는 목록 닫기 몫이다(TMP_Dropdown 이 EventSystem 에서 닫는다).
    /// </summary>
    public bool DropdownClaimsEscape => _dropdownOpenAtFrameStart;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instance = null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (transform.parent != null) transform.SetParent(null, false);   // DDOL 은 루트에만 걸린다
        DontDestroyOnLoad(gameObject);

        if (settingsBody != null)
            _dropdowns = settingsBody.GetComponentsInChildren<TMP_Dropdown>(true);

        if (pauseMenu != null)
        {
            pauseMenu.ContinueRequested += Close;
            pauseMenu.SettingsRequested += ShowSettings;
            pauseMenu.QuitRequested += HandleQuit;
            pauseMenu.ExitToTitleRequested += LeaveToTitle;
            pauseMenu.ExitToDesktopRequested += GameManager.QuitGame;
        }

        if (SettingsCloseButton != null) SettingsCloseButton.onClick.AddListener(ShowMenu);

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = overlaySortingOrder;
        // 메뉴·설정창 모든 누를 수 있는 것에 같은 반응·클릭 소리(경석 10-26 6차 — PLAN-title-start-settings-tabs).
        if (content != null) UiFeedbackStyle.Apply(content.transform);
        SetContent(false);
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        ApplyMode(GameMenuRules.ResolveMode(SceneManager.GetActiveScene().name));
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        Instance = null;
    }

    private void Update()
    {
        _dropdownOpenAtFrameStart = false;
        if (Screen != GameMenuScreen.Settings) return;
        foreach (TMP_Dropdown d in _dropdowns)
        {
            if (d != null && d.IsExpanded)
            {
                _dropdownOpenAtFrameStart = true;
                return;
            }
        }
    }

    // ESC 는 LateUpdate — 로비 결과·캐릭터 선택·스킬 조준이 Update 에서 "이 프레임 ESC 를 썼다"를 남긴 뒤에 본다(실행 순서 무관).
    private void LateUpdate()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
            return;

        bool claimed = LobbyResultPanel.IsEscapeClaimed || CharacterSelectPanel.IsEscapeClaimed || PlayerSkillTargeting.IsEscapeClaimed;
        switch (GameMenuRules.ResolveEscape(Mode, Screen, claimed, KeyRebindRow.IsRebinding, _dropdownOpenAtFrameStart))
        {
            case GameMenuEscape.Open: OpenMenu(); break;
            case GameMenuEscape.BackToMenu: ShowMenu(); break;
            case GameMenuEscape.Close: Close(); break;
        }
    }

    // ── 로비·인게임 ─────────────────────────────────────────────────────

    /// <summary>ESC 메뉴 열기(맵 톱니 버튼도 여기로). 메뉴가 없는 모드면 무시.</summary>
    public void OpenMenu()
    {
        if (!GameMenuRules.HasPauseMenu(Mode))
            return;

        SetContent(true);
        ShowMenu();
    }

    public void Close()
    {
        SetContent(false);
        Screen = GameMenuScreen.Closed;
    }

    private void ShowMenu()
    {
        SetSettings(false);
        if (pauseMenu != null) pauseMenu.ShowMenu();
        Screen = GameMenuScreen.Menu;
    }

    private void ShowSettings()
    {
        if (pauseMenu != null) pauseMenu.HideAll();
        SetSettings(true);
        Screen = GameMenuScreen.Settings;
    }

    // 그만두기 → 팝업(바탕화면 / 타이틀 — 누르면 바로). 호스트에게는 다른 플레이어도 끊긴다는 경고 문구.
    private void HandleQuit()
    {
        UiString message;
        switch (GameMenuRules.ResolveExitMessage(Mode, IsHost()))
        {
            case GameMenuExitMessage.HostDisconnectsOthers: message = UiStrings.ExitHostToTitleWarning; break;
            case GameMenuExitMessage.HostEndsOthersGame: message = UiStrings.ExitHostQuitWarning; break;
            default: message = UiStrings.ExitMessage; break;
        }

        pauseMenu.ShowExitConfirm(message);
        Screen = GameMenuScreen.Confirm;
    }

    private void LeaveToTitle()
    {
        Close();
        if (GameManager.Instance != null)
            GameManager.Instance.LeaveSessionToTitle();
        else
            Debug.LogWarning("[GameMenu] GameManager 가 없다 — 타이틀로 갈 수 없다(BootStrap 을 거치지 않은 Play).", this);
    }

    private static bool IsHost()
    {
        NetworkManager nm = NetworkManager.Singleton;
        return nm != null && nm.IsListening && nm.IsServer;
    }

    // ── 씬 전환 ─────────────────────────────────────────────────────────

    private void HandleActiveSceneChanged(Scene previous, Scene next)
    {
        // 🔴 Content 를 실제로 끈다 — 해상도 확인 되돌림·키 변경 취소가 설정 컨트롤러의 OnDisable 에서 돈다.
        Close();
        ApplyMode(GameMenuRules.ResolveMode(next.name));
    }

    private void ApplyMode(GameMenuMode mode) => Mode = mode;

    private void SetContent(bool active)
    {
        if (content != null && content.activeSelf != active) content.SetActive(active);
    }

    private void SetSettings(bool active)
    {
        if (settingsBody != null && settingsBody.gameObject.activeSelf != active) settingsBody.gameObject.SetActive(active);
    }
}
