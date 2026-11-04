using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public enum TitleFlowState
{
    Idle,
    Restoring,
    Approaching,
    Starting,
}

/// <summary>
/// 타이틀 연출의 단일 진입점. Press 문구(화면은 블러·흑백) → 아무 키 → 블러·흑백 원복(1초, 218) → 카메라가 모니터로 다가감 → 모니터 CRT 꺼짐 → 검정 페이드 → 로비.
/// 다가가는 중 ESC = 꺼짐 없이 바로 검정 페이드 → 로비.
/// </summary>
/// <remarks>
/// 10-26(경석 — PLAN-title-start-settings-tabs) 모니터 안 메뉴(시작·설정·나가기)와 타이틀 설정창을 지웠다.
/// 설정·종료는 로비 ESC 메뉴(<see cref="GameMenuController"/>)에서 한다.
/// 🔴 모든 입력은 여기를 거친다 — <see cref="TitleSceneManager"/> 는 씬 전환·페이드만 한다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TitleFlowDirector : MonoBehaviour
{
    [Header("카메라")]
    [SerializeField] private CinemachineBrain _brain;
    [SerializeField] private CinemachineCamera _vcamFar;
    [SerializeField] private CinemachineCamera _vcamNear;

    [Tooltip("활성 vcam 에 줄 우선순위.")]
    [SerializeField] private int _activePriority = 30;

    [Tooltip("비활성 vcam 에 줄 우선순위.")]
    [SerializeField] private int _idlePriority = 10;

    [Tooltip("Far→Near 이동 시간(초). 🔴 이 값이 유일한 원본 — 시작할 때 Brain 의 Default Blend 시간을 이 값으로 덮는다.")]
    [SerializeField, Min(0.1f)] private float _approachDuration = 3f;

    [Header("중앙 모니터")]
    [Tooltip("중앙 CRT 화면 표시(로고 · 꺼짐). 비면 꺼짐 없이 바로 페이드.")]
    [SerializeField] private TitleMonitorDisplay _monitorDisplay;

    [Tooltip("도착한 뒤 모니터 화면이 CRT 처럼 꺼지는 시간(초). 끝나면 검정 페이드 → 로비.")]
    [SerializeField, Min(0.05f)] private float _powerOffDuration = 1.1f;

    [Header("CRT 연출 (계획서 §0 · Codex 연출 검토 09-23)")]
    [Tooltip("상시 CRT 시간·찢김·글리치 버스트 구동.")]
    [SerializeField] private TitleCrtFx _crtFx;

    // 화면 전환 순간의 지직거림(팀장 10-13 — 인스펙터에서 조절). x = 세기(0~1), y = 길이(초).
    // 상시 지직거림·찢김은 TitleCrtFx, CRT 화면 효과는 TitleMonitorDisplay 인스펙터.
    [Header("전환 지직거림 (x = 세기 0~1, y = 길이 초)")]
    [Tooltip("Press 뒤 모니터로 다가갈 때.")]
    [SerializeField] private Vector2 _approachBurst = new(0.8f, 0.25f);
    [Tooltip("도착해 모니터가 꺼지기 시작할 때.")]
    [SerializeField] private Vector2 _startBurst = new(0.35f, 0.1f);

    [Header("UI 루트")]
    [Tooltip("Press Any Button 문구 — 화면 앞 오버레이(모니터 안이 아니다). 이 문구가 있는 캔버스는 대기 블러에서 빠져 선명하다.")]
    [SerializeField] private GameObject _pressAnyKeyRoot;

    // 218 — Press 대기 동안 문구 캔버스 밖(월드)을 블러·흑백으로, 키 입력 뒤 선형으로 원복한 다음 카메라 진입.
    // 블러는 201 공용 경로(PanelBackdropBlur.Drive → PC_Renderer PanelBackdropBlurFeature). 렌더러에 피처가 없으면 원복 시간만 기다린다.
    [Header("Press 대기 블러·흑백 (218)")]
    [Tooltip("끄면 블러·흑백 없이 예전처럼 키 입력 즉시 카메라 진입.")]
    [SerializeField] private bool _idleBlurEnabled = true;

    [Tooltip("대기 중 화면 모양. 채도 0 = 완전 흑백. 원복 동안 셋 다 원본 쪽으로 선형으로 줄어든다.")]
    [SerializeField] private PanelBackdropBlurLook _idleBlurLook = new(1.42f, 0f, 0f, true);

    [Tooltip("키 입력 뒤 블러 0·색 원복까지 걸리는 시간(초, unscaled, 선형). 끝나면 카메라 진입 연출.")]
    [SerializeField, Min(0f)] private float _restoreDuration = 1f;

    [Header("오버레이 (전체화면)")]
    [Tooltip("우측 하단 'Esc 건너뛰기' 안내(TitleSkipHint — 보스 스킵 위젯 모양, 누르면 바로). 접근 구간에만 켠다.")]
    [SerializeField] private GameObject _skipHintRoot;

    [Header("연동")]
    [SerializeField] private TitleSceneManager _sceneManager;

    [Tooltip("키 입력 순간의 글리치 버스트용 드라이버.")]
    [SerializeField] private CrtFxDriver _burstFx;

    [Tooltip("접근하는 동안 진행도로 왜곡을 끌어올리는 드라이버. 버스트와 같은 드라이버를 쓰면 서로 덮어쓴다.")]
    [SerializeField] private CrtFxDriver _sustainFx;

    [SerializeField] private string _enterBurstId = "glitch";
    [SerializeField] private string _approachSustainId = "approach";

    // 블렌드가 Brain 에 등록되기 전(첫 프레임)에는 "블렌드 끝"으로 보지 않는다.
    private const float BlendGuardSeconds = 0.05f;

    private TitleFlowState _state = TitleFlowState.Idle;
    private float _approachElapsed;
    private float _restoreElapsed;
    private bool _blurHeld;

    public TitleFlowState State => _state;

    private void Awake()
    {
        if (_brain == null && Camera.main != null)
            _brain = Camera.main.GetComponent<CinemachineBrain>();

        if (_brain != null)
        {
            CinemachineBlendDefinition blend = _brain.DefaultBlend;
            blend.Time = _approachDuration;
            _brain.DefaultBlend = blend;
        }

        if (_sceneManager == null)
            _sceneManager = FindAnyObjectByType<TitleSceneManager>();
    }

    private void Start()
    {
        EnterIdle();
    }

    // 🔴 렌즈(FOV 등)의 원본은 VCam_Far/Near 의 **Lens** 다. Main Camera 의 FOV 칸은 CinemachineBrain 이
    //    에디트 모드에서도([ExecuteAlways]) 활성 vcam 값으로 덮어써서 바꿔도 안 먹는다 — 정상 동작.
    //    심도(F값·초점 거리·초점 길이)는 카메라가 아니라 볼륨의 Depth Of Field 가 정한다(URP 규칙).

    private void Update()
    {
        switch (_state)
        {
            case TitleFlowState.Idle:
                if (AnyStartInput())
                    EnterRestoring();
                break;

            case TitleFlowState.Restoring:
                TickRestoring();
                break;

            case TitleFlowState.Approaching:
                TickApproaching();
                break;
        }
    }

    // ── 상태 전이 ──────────────────────────────────────────────────────────

    private void EnterIdle()
    {
        _state = TitleFlowState.Idle;
        ActivateCamera(_vcamFar);

        SetActive(_pressAnyKeyRoot, true);
        SetActive(_skipHintRoot, false);

        if (_monitorDisplay != null)
            _monitorDisplay.ShowLogo();
        if (_crtFx != null)
            _crtFx.AmbientBursts = true; // 로고·Press 캡슐이 지직거린다

        HoldIdleBlur();

        Debug.Log("[TitleFlow] Idle");
    }

    // 키 입력 → Press 문구는 바로 끄고(예전 키 입력 순간과 같음), 블러·흑백을 _restoreDuration 동안 선형으로 0 까지 → 카메라 진입.
    private void EnterRestoring()
    {
        SetActive(_pressAnyKeyRoot, false);

        if (!_blurHeld || _restoreDuration <= 0f)
        {
            ReleaseIdleBlur();
            EnterApproaching();
            return;
        }

        _state = TitleFlowState.Restoring;
        _restoreElapsed = 0f;
        Debug.Log("[TitleFlow] Restoring");
    }

    private void TickRestoring()
    {
        _restoreElapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(_restoreElapsed / _restoreDuration);
        PanelBackdropBlur.Drive(this, 1f - t, _idleBlurLook);

        if (t >= 1f)
        {
            ReleaseIdleBlur();
            EnterApproaching();
        }
    }

    private void EnterApproaching()
    {
        _state = TitleFlowState.Approaching;
        _approachElapsed = 0f;

        // 🔴 캔버스를 통째로 끈다 — 연출 중 Press 캡슐이 남지 않게.
        SetActive(_pressAnyKeyRoot, false);
        SetActive(_skipHintRoot, true);

        Burst(_approachBurst);
        // 접근하는 동안엔 로고가 계속 지직거리고, 도착하면 꺼진다(EnterStarting).

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

        // ESC = 꺼짐 없이 바로 검정 페이드 → 로비(경석 10-26). 카메라는 페이드 아래에서 계속 다가간다.
        if (CancelPressed())
        {
            Debug.Log("[TitleFlow] 접근 스킵 → 바로 페이드");
            EnterStarting(powerOff: false);
            return;
        }

        bool blendFinished = _approachElapsed >= BlendGuardSeconds &&
                             (_brain == null || _brain.ActiveBlend == null);
        bool timedOut = _approachElapsed >= _approachDuration + 0.5f;

        if (blendFinished || timedOut)
            EnterStarting(powerOff: true);
    }

    private void EnterStarting(bool powerOff)
    {
        _state = TitleFlowState.Starting;

        if (_sustainFx != null)
            _sustainFx.Stop();
        SetActive(_skipHintRoot, false);

        Debug.Log($"[TitleFlow] Starting (모니터 꺼짐 {powerOff})");

        if (_sceneManager == null)
        {
            Debug.LogError("[TitleFlow] TitleSceneManager 가 없다 — 로비로 갈 수 없다.", this);
            return;
        }

        if (powerOff && _monitorDisplay != null && _monitorDisplay.isActiveAndEnabled)
        {
            Burst(_startBurst);
            StartCoroutine(PowerOffThenLobby());
        }
        else
        {
            _sceneManager.StartGame();
        }
    }

    // 🔴 로비 전환은 TitleSceneManager.StartGame 하나로 — 그게 검정 페이드 완료 후 로비로 간다(로비는 진입 시 페이드 인).
    private IEnumerator PowerOffThenLobby()
    {
        yield return _monitorDisplay.PowerOff(_powerOffDuration);
        _sceneManager.StartGame();
    }

    // ── Press 대기 블러(218) ──────────────────────────────────────────────

    // 문구가 있는 캔버스를 "패널"로 잡는다 → 그 캔버스(Overlay)는 후처리 뒤에 그려져 선명, 월드만 흐림·흑백.
    private void HoldIdleBlur()
    {
        if (!_idleBlurEnabled || _blurHeld)
            return;

        Canvas sharpCanvas = _pressAnyKeyRoot != null ? _pressAnyKeyRoot.GetComponentInParent<Canvas>(true) : null;
        if (sharpCanvas == null)
        {
            // 캔버스 없이 잡으면 모든 Overlay 캔버스가 흐림 대상이 된다 — 문구까지 흐려지니 블러를 건너뛴다.
            Debug.LogWarning("[TitleFlow] Press 문구(_pressAnyKeyRoot)·캔버스가 없다 — 대기 블러 없이 진행.", this);
            return;
        }

        PanelBackdropBlur.Acquire(this, sharpCanvas);
        PanelBackdropBlur.Drive(this, 1f, _idleBlurLook);
        _blurHeld = true;
    }

    private void ReleaseIdleBlur()
    {
        if (!_blurHeld)
            return;

        PanelBackdropBlur.Release(this);
        _blurHeld = false;
    }

    private void OnDisable()
    {
        ReleaseIdleBlur();
    }

    // x = 세기, y = 길이(초) — 인스펙터 「전환 지직거림」.
    private void Burst(Vector2 strengthDuration)
    {
        if (_crtFx != null)
            _crtFx.Burst(strengthDuration.x, strengthDuration.y);
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

    /// <summary>ESC 또는 패드 B.</summary>
    private static bool CancelPressed()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            return true;

        Gamepad pad = Gamepad.current;
        return pad != null && pad.buttonEast.wasPressedThisFrame;
    }

    // ── 유틸 ───────────────────────────────────────────────────────────────

    private void ActivateCamera(CinemachineCamera target)
    {
        SetPriority(_vcamFar, target == _vcamFar);
        SetPriority(_vcamNear, target == _vcamNear);
    }

    private void SetPriority(CinemachineCamera vcam, bool active)
    {
        if (vcam != null)
            vcam.Priority = active ? _activePriority : _idlePriority;
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }
}
