using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public enum TitleFlowState
{
    Idle,
    Approaching,
    Menu,
    Settings,
    Starting,
    Exiting,
}

/// <summary>
/// 타이틀 연출의 단일 진입점. PRESS ANY KEY → 카메라 인 → 모니터 안 메뉴 → 설정/시작/종료.
/// </summary>
/// <remarks>
/// 🔴 <b>모든 입력과 버튼은 여기를 거친다.</b> <see cref="TitleSceneManager"/> 의 ESC 처리와
/// 씬에 박힌 영구 UnityEvent 를 그대로 두면 상태 검사를 우회해 연출 중에 옵션창이 열린다.
/// 씬의 버튼 콜백은 이 컴포넌트의 메서드로 재지정할 것.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TitleFlowDirector : MonoBehaviour
{
    [Header("카메라")]
    [SerializeField] private CinemachineBrain _brain;
    [SerializeField] private CinemachineCamera _vcamFar;
    [SerializeField] private CinemachineCamera _vcamNear;
    [SerializeField] private CinemachineCamera _vcamCloseup;

    [Tooltip("활성 vcam 에 줄 우선순위.")]
    [SerializeField] private int _activePriority = 30;

    [Tooltip("비활성 vcam 에 줄 우선순위.")]
    [SerializeField] private int _idlePriority = 10;

    [Tooltip("Far→Near 블렌드에 걸리는 시간(초). Brain 의 기본 블렌드와 맞춰 둘 것. 진행도·타임아웃 계산에 쓴다.")]
    [SerializeField, Min(0.1f)] private float _approachDuration = 2f;

    [Header("중앙 모니터")]
    [Tooltip("중앙 CRT 화면 표시(로고 ↔ UI 렌더텍스처). 비면 화면 전환 없이 기존처럼 동작.")]
    [SerializeField] private TitleMonitorDisplay _monitorDisplay;

    [Header("CRT 연출 (계획서 §0 · Codex 연출 검토 09-23)")]
    [Tooltip("상시 CRT 시간·찢김·글리치 버스트 구동.")]
    [SerializeField] private TitleCrtFx _crtFx;

    [Tooltip("Start/Exit 공통 전체 화면 CRT 꺼짐.")]
    [SerializeField] private TitlePowerOff _powerOff;

    [Header("UI 루트")]
    [Tooltip("PRESS ANY KEY — 화면 앞 오버레이(모니터 안이 아니다).")]
    [SerializeField] private GameObject _pressAnyKeyRoot;
    [SerializeField] private GameObject _menuRoot;
    [SerializeField] private GameObject _settingsRoot;

    [Header("오버레이 (전체화면)")]
    [Tooltip("우측 하단 'ESC 로 건너뛰기' 안내. 접근 구간에만 켠다.")]
    [SerializeField] private GameObject _skipHintRoot;

    [Header("버튼")]
    [SerializeField] private Button _startButton;
    [SerializeField] private Button _optionButton;
    [SerializeField] private Button _exitButton;
    [SerializeField] private Button _settingsCloseButton;

    [Header("연동")]
    [SerializeField] private TitleSceneManager _sceneManager;

    [Tooltip("키 입력 순간의 글리치 버스트용 드라이버.")]
    [SerializeField] private CrtFxDriver _burstFx;

    [Tooltip("접근하는 동안 진행도로 왜곡을 끌어올리는 드라이버. 버스트와 같은 드라이버를 쓰면 서로 덮어쓴다.")]
    [SerializeField] private CrtFxDriver _sustainFx;

    [SerializeField] private string _enterBurstId = "glitch";
    [SerializeField] private string _approachSustainId = "approach";

    // 블렌드가 Brain 에 등록되기 전(첫 프레임)의 ESC 는 무시한다. 그 전에 끊으면 스냅이 안 된다.
    private const float SkipGuardSeconds = 0.05f;

    private TitleFlowState _state = TitleFlowState.Idle;
    private float _approachElapsed;
    private GameObject _lastSelected;

    public TitleFlowState State => _state;

    /// <summary>연출·전환 중이라 UI 입력을 받으면 안 되는 구간.</summary>
    public bool InputLocked =>
        _state is TitleFlowState.Approaching or TitleFlowState.Starting or TitleFlowState.Exiting;

    private void Awake()
    {
        if (_brain == null && Camera.main != null)
            _brain = Camera.main.GetComponent<CinemachineBrain>();

        if (_sceneManager == null)
            _sceneManager = FindAnyObjectByType<TitleSceneManager>();

        BindButtons();
    }

    private void Start()
    {
        EnterIdle();
    }

    private void Update()
    {
        switch (_state)
        {
            case TitleFlowState.Idle:
                if (AnyStartInput())
                    EnterApproaching();
                break;

            case TitleFlowState.Approaching:
                TickApproaching();
                break;

            case TitleFlowState.Menu:
                RestoreSelectionIfNavigating(_startButton);
                break;

            case TitleFlowState.Settings:
                if (CancelPressed())
                    CloseSettings();
                else
                    RestoreSelectionIfNavigating(_settingsCloseButton);
                break;
        }
    }

    // ── 상태 전이 ──────────────────────────────────────────────────────────

    private void EnterIdle()
    {
        _state = TitleFlowState.Idle;
        ActivateCamera(_vcamFar);

        SetActive(_pressAnyKeyRoot, true);
        SetActive(_menuRoot, false);
        SetActive(_settingsRoot, false);
        SetActive(_skipHintRoot, false);

        if (_monitorDisplay != null)
            _monitorDisplay.ShowLogo();
        if (_crtFx != null)
            _crtFx.AmbientBursts = true; // 로고가 지직거린다

        Debug.Log("[TitleFlow] Idle");
    }

    private void EnterApproaching()
    {
        _state = TitleFlowState.Approaching;
        _approachElapsed = 0f;

        // 🔴 캔버스를 통째로 끈다. GraphicRaycaster 까지 죽어야 연출 중 클릭이 확실히 무시된다.
        SetActive(_pressAnyKeyRoot, false);
        SetActive(_menuRoot, false);
        SetActive(_settingsRoot, false);
        SetActive(_skipHintRoot, true);

        Burst(0.8f, 0.25f);

        // 접근하는 동안엔 로고가 계속 지직거리고, 도착하는 순간 메뉴로 바뀐다(팀장 09-23 — EnterMenu 에서 ShowUI).

        ClearSelection();
        ActivateCamera(_vcamNear);

        if (_burstFx != null)
            _burstFx.Play(_enterBurstId);

        Debug.Log("[TitleFlow] Approaching");
    }

    private void TickApproaching()
    {
        _approachElapsed += Time.unscaledDeltaTime;

        if (_sustainFx != null)
            _sustainFx.DriveSustain(_approachSustainId, Mathf.Clamp01(_approachElapsed / _approachDuration));

        // 첫 프레임에는 Brain 이 아직 블렌드를 만들지 않았다. 그 구간의 ESC 는 보류한다.
        if (_approachElapsed >= SkipGuardSeconds && CancelPressed())
        {
            // 진행 중 블렌드를 즉시 완료시킨다. DefaultBlend 를 바꾸는 것은 소급 적용되지 않는다.
            if (_brain != null)
                _brain.ActiveBlend = null;

            Debug.Log("[TitleFlow] 접근 스킵");
        }

        bool blendFinished = _approachElapsed >= SkipGuardSeconds &&
                             (_brain == null || _brain.ActiveBlend == null);
        bool timedOut = _approachElapsed >= _approachDuration + 0.5f;

        if (blendFinished || timedOut)
            EnterMenu();
    }

    private void EnterMenu()
    {
        _state = TitleFlowState.Menu;

        if (_sustainFx != null)
            _sustainFx.Stop();

        SetActive(_skipHintRoot, false);
        SetActive(_settingsRoot, false);
        SetActive(_menuRoot, true);
        if (_monitorDisplay != null)
            _monitorDisplay.ShowUI(); // 도착 — 로고 → START / SETTING / EXIT
        if (_crtFx != null)
            _crtFx.AmbientBursts = false; // 메뉴에선 끈다 — 클릭 판정이 흔들리지 않게
        Burst(0.9f, 0.28f); // 화면 전환은 강한 버스트 한 번으로

        Select(_startButton);

        Debug.Log("[TitleFlow] Menu");
    }

    // ── 버튼 진입점 (씬의 UnityEvent 는 이쪽으로 재지정한다) ──────────────

    public void OpenSettings()
    {
        if (InputLocked || _state == TitleFlowState.Settings)
            return;

        _state = TitleFlowState.Settings;

        SetActive(_menuRoot, false);
        SetActive(_settingsRoot, true);
        Burst(0.5f, 0.16f);
        // 🔴 줌하지 않는다(팀장 09-23) — 메뉴가 보이던 Near 뷰 그대로 모니터 내용만 설정창으로 바뀐다.
        //    예전엔 VCam_Closeup 으로 한 번 더 들어가 설정창이 화면 밖으로 잘렸다. Closeup vcam 은 남겨 두되 안 쓴다.

        Select(_settingsCloseButton);

        Debug.Log("[TitleFlow] Settings");
    }

    public void CloseSettings()
    {
        if (_state != TitleFlowState.Settings)
            return;

        EnterMenu(); // 카메라는 이미 Near — 건드리지 않는다
    }

    public void StartGame()
    {
        if (InputLocked || _sceneManager == null)
            return;

        _state = TitleFlowState.Starting;
        ClearSelection();
        Burst(0.35f, 0.1f);
        Debug.Log("[TitleFlow] Starting");

        // Start: 모니터처럼 딱 꺼짐 → (검은 화면에서) 페이드 없이 로비. 꺼짐이 없으면 기존 페이드 경로.
        // (Flow 혜성 트레일은 넣었다가 뺐다 — 팀장 09-23 "무지개빛 말고 그냥 모니터처럼 딱 꺼지게")
        PowerOffThenLobby();
    }

    public void ExitGame()
    {
        if (InputLocked || _sceneManager == null)
            return;

        _state = TitleFlowState.Exiting;
        ClearSelection();
        Burst(0.35f, 0.1f);
        Debug.Log("[TitleFlow] Exiting");

        // Exit 도 같은 꺼짐(팀장 09-23).
        if (_powerOff != null)
            _powerOff.Play(_sceneManager.ExitGameImmediate);
        else
            _sceneManager.ExitGame();
    }

    private void PowerOffThenLobby()
    {
        if (_powerOff != null)
            _powerOff.Play(_sceneManager.StartGameImmediate);
        else
            _sceneManager.StartGame();
    }

    private void Burst(float strength, float duration)
    {
        if (_crtFx != null)
            _crtFx.Burst(strength, duration);
    }

    // ── 입력 ───────────────────────────────────────────────────────────────

    /// <summary>아무 키 / 좌클릭 / 패드 버튼. 🔴 마우스 이동만으로는 발동하지 않는다.</summary>
    private static bool AnyStartInput()
    {
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
            return true;

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            return true;

        Gamepad pad = Gamepad.current;
        if (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame))
            return true;

        return false;
    }

    /// <summary>ESC 또는 패드 B. 🔴 uGUI Button 은 ICancelHandler 를 구현하지 않아 직접 읽어야 한다.</summary>
    private static bool CancelPressed()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            return true;

        Gamepad pad = Gamepad.current;
        return pad != null && pad.buttonEast.wasPressedThisFrame;
    }

    private static bool NavigationPressed()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame ||
                           kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame))
        {
            return true;
        }

        Gamepad pad = Gamepad.current;
        if (pad == null)
            return false;

        return pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame ||
               pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame ||
               pad.leftStick.up.wasPressedThisFrame || pad.leftStick.down.wasPressedThisFrame;
    }

    // ── 선택 관리 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 BootStrap 의 EventSystem 이 <c>m_DeselectOnBackgroundClick: 1</c> 이라
    /// 빈 곳을 클릭하면 선택이 풀리고 그 뒤 방향키·패드가 먹통이 된다.
    /// 마우스 사용자를 방해하지 않도록 <b>네비게이션 입력이 들어온 순간에만</b> 복구한다.
    /// </summary>
    private void RestoreSelectionIfNavigating(Button fallback)
    {
        EventSystem es = EventSystem.current;
        if (es == null)
            return;

        if (es.currentSelectedGameObject != null)
        {
            _lastSelected = es.currentSelectedGameObject;
            return;
        }

        if (!NavigationPressed())
            return;

        if (_lastSelected != null && _lastSelected.activeInHierarchy)
            es.SetSelectedGameObject(_lastSelected);
        else
            Select(fallback);
    }

    private void Select(Button button)
    {
        EventSystem es = EventSystem.current;
        if (es == null || button == null)
            return;

        es.SetSelectedGameObject(button.gameObject);
        _lastSelected = button.gameObject;
    }

    private void ClearSelection()
    {
        EventSystem es = EventSystem.current;
        if (es != null)
            es.SetSelectedGameObject(null);

        _lastSelected = null;
    }

    // ── 유틸 ───────────────────────────────────────────────────────────────

    private void ActivateCamera(CinemachineCamera target)
    {
        SetPriority(_vcamFar, target == _vcamFar);
        SetPriority(_vcamNear, target == _vcamNear);
        SetPriority(_vcamCloseup, target == _vcamCloseup);
    }

    private void SetPriority(CinemachineCamera vcam, bool active)
    {
        if (vcam != null)
            vcam.Priority = active ? _activePriority : _idlePriority;
    }

    private void BindButtons()
    {
        Bind(_startButton, StartGame);
        Bind(_optionButton, OpenSettings);
        Bind(_exitButton, ExitGame);
        Bind(_settingsCloseButton, CloseSettings);
    }

    private static void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
            return;

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }
}
