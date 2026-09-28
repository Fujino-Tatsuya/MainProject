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

    [Header("월드 캔버스")]
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
        ActivateCamera(_vcamCloseup);

        Select(_settingsCloseButton);

        Debug.Log("[TitleFlow] Settings");
    }

    public void CloseSettings()
    {
        if (_state != TitleFlowState.Settings)
            return;

        ActivateCamera(_vcamNear);
        EnterMenu();
    }

    public void StartGame()
    {
        if (InputLocked || _sceneManager == null)
            return;

        _state = TitleFlowState.Starting;
        ClearSelection();

        // CRT 는 Title 소유 컨트롤러이므로 Single 씬 로드와 함께 자동 해제된다.
        // (RetroCRTController.OnDisable 이 s_active 를 비우고, Feature 가 null 이면 즉시 리턴한다)
        _sceneManager.StartGame();

        Debug.Log("[TitleFlow] Starting");
    }

    public void ExitGame()
    {
        if (InputLocked || _sceneManager == null)
            return;

        _state = TitleFlowState.Exiting;
        ClearSelection();
        _sceneManager.ExitGame();

        Debug.Log("[TitleFlow] Exiting");
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
