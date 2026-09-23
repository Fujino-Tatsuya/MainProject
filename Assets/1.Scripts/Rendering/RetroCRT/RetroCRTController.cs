using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

// PC Renderer를 공유하는 다른 씬까지 CRT가 켜지지 않도록 맵에서 수명을 관리한다.
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class RetroCRTController : MonoBehaviour
{
    [Header("Cyanilux CRT")]
    [Tooltip("Cyanilux CRT만 켜고 끈다. F7도 이 항목만 전환한다. 아래 두 효과는 독립적으로 동작한다.")]
    [SerializeField] private bool _effectEnabled = true;

    [Tooltip("검은 CRT 베젤 크기. 0=없음, 1=기존 크기, 클수록 테두리가 넓어진다. 화면 곡률과 별도로 조절한다.")]
    [SerializeField, Range(0f, 3f)] private float _bezelSize = 1f;

    [Header("기존 픽셀레이트")]
    [SerializeField] private bool _pixelateEnabled;
    [Tooltip("픽셀 블록 한 변의 크기(렌더 픽셀). 1이면 픽셀화 없음.")]
    [SerializeField, Range(1, 128)] private int _pixelBlockSize = 4;

    [Header("기존 스캔라인")]
    [Tooltip("기존 가로줄 효과. Cyanilux 머티리얼의 Scanline Strength와 별도이므로 함께 켜면 중첩된다.")]
    [SerializeField] private bool _scanlineEnabled;
    [Tooltip("색 띠 한 줄의 두께(렌더 픽셀). 픽셀 블록 크기와 독립적이다.")]
    [SerializeField, Range(1, 64)] private int _scanlineThicknessPx = 2;
    [Tooltip("색 띠 사이의 원본 화면 간격(렌더 픽셀).")]
    [SerializeField, Range(0, 128)] private int _scanlineSpacingPx = 4;
    [Tooltip("스캔라인 RGB 색상. 강도는 아래 불투명도로 조절한다.")]
    [SerializeField, ColorUsage(false, false)]
    private Color _scanlineColor = new Color(0.032484863f, 0.09433961f, 0.06783043f, 1f);
    [Tooltip("0=원본 화면, 1=지정 색상.")]
    [SerializeField, Range(0f, 1f)] private float _scanlineOpacity = 0.2f;

    [Header("머티리얼 오버라이드 (선택)")]
    [Tooltip("비워 두면 Renderer Feature 의 공유 머티리얼을 쓴다. " +
             "타이틀처럼 STATIC·DISTORT 같은 키워드를 따로 켜야 하면 복제본을 만들어 여기 꽂는다. " +
             "키워드는 MaterialPropertyBlock 으로 못 바꾸므로 이 길밖에 없다.")]
    [SerializeField] private Material _materialOverride;

    private static RetroCRTController s_active;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private InputAction _toggleAction;
#endif

    // 런타임 오버라이드. 공유 머티리얼의 저작값은 건드리지 않고, 활성화된 것만 MPB 로 덮는다.
    // 비어 있으면 아무것도 밀지 않으므로 맵 씬의 기존 룩이 그대로 유지된다.
    private readonly float[] _overrideValues = new float[CrtParamInfo.Count];
    private int _overrideMask;

    public static RetroCRTController ActiveController =>
        s_active != null && s_active.isActiveAndEnabled ? s_active : null;

    public static bool IsEffectActive => ActiveController != null && s_active._effectEnabled;

    internal float BezelSize => Mathf.Clamp(_bezelSize, 0f, 3f);

    internal bool HasPixelScanlineEffect =>
        (_pixelateEnabled && _pixelBlockSize > 1) || (_scanlineEnabled && _scanlineOpacity > 0f);

    internal Vector4 PixelParameters =>
        new Vector4(Mathf.Clamp(_pixelBlockSize, 1, 128), _pixelateEnabled ? 1f : 0f, 0f, 0f);

    internal Vector4 ScanlineParameters =>
        new Vector4(Mathf.Clamp(_scanlineThicknessPx, 1, 64), Mathf.Clamp(_scanlineSpacingPx, 0, 128),
            _scanlineEnabled ? 1f : 0f, Mathf.Clamp01(_scanlineOpacity));

    internal Color ScanlineColor => new Color(_scanlineColor.r, _scanlineColor.g, _scanlineColor.b, 1f);

    public bool EffectEnabled
    {
        get => _effectEnabled;
        set => _effectEnabled = value;
    }

    /// <summary>비었으면 Renderer Feature 의 공유 머티리얼을 쓴다.</summary>
    internal Material MaterialOverride => _materialOverride;

    /// <summary>오버라이드가 하나라도 걸려 있는지. 없으면 Feature 는 MPB 에 아무것도 담지 않는다.</summary>
    internal bool HasOverrides => _overrideMask != 0;

    /// <summary>
    /// 한 파라미터를 런타임 값으로 덮는다. 해제 전까지 유지되므로 연출이 끝나면 반드시 지운다.
    /// </summary>
    public void SetOverride(CrtParam param, float value)
    {
        _overrideValues[(int)param] = value;
        _overrideMask |= 1 << (int)param;
    }

    /// <summary>한 파라미터의 오버라이드를 풀어 머티리얼 저작값으로 되돌린다.</summary>
    public void ClearOverride(CrtParam param)
    {
        _overrideMask &= ~(1 << (int)param);
    }

    /// <summary>전부 풀어 머티리얼 저작값으로 되돌린다.</summary>
    public void ClearAllOverrides()
    {
        _overrideMask = 0;
    }

    internal bool TryGetOverride(CrtParam param, out float value)
    {
        var bit = 1 << (int)param;
        if ((_overrideMask & bit) == 0)
        {
            value = 0f;
            return false;
        }

        value = _overrideValues[(int)param];
        return true;
    }

    private void OnValidate()
    {
        _bezelSize = Mathf.Clamp(_bezelSize, 0f, 3f);
        _pixelBlockSize = Mathf.Clamp(_pixelBlockSize, 1, 128);
        _scanlineThicknessPx = Mathf.Clamp(_scanlineThicknessPx, 1, 64);
        _scanlineSpacingPx = Mathf.Clamp(_scanlineSpacingPx, 0, 128);
        _scanlineOpacity = Mathf.Clamp01(_scanlineOpacity);
    }

    private void OnEnable()
    {
        if (s_active != null && s_active != this)
            Debug.LogWarning("[RetroCRT] 컨트롤러가 둘 이상이다. 맵 씬에 하나만 둘 것.", this);

        s_active = this;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Application.isPlaying)
        {
            _toggleAction = new InputAction("Toggle CRT", InputActionType.Button, "<Keyboard>/f7");
            _toggleAction.performed += OnTogglePerformed;
            _toggleAction.Enable();
        }
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        _toggleAction?.Dispose();
        _toggleAction = null;
#endif
        // 연출 도중 씬이 바뀌어도 오버라이드가 남지 않게 한다.
        ClearAllOverrides();

        if (s_active == this)
            s_active = null;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void OnTogglePerformed(InputAction.CallbackContext context)
    {
        if (Application.isPlaying && s_active == this)
            ToggleEffect();
    }
#endif

    [ContextMenu("Toggle CRT")]
    public void ToggleEffect()
    {
        _effectEnabled = !_effectEnabled;
        Debug.Log($"[RetroCRT] Cyanilux CRT {(_effectEnabled ? "ON" : "OFF")}", this);
    }
}
