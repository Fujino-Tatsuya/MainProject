using UnityEngine;

/// <summary>
/// 중앙 CRT 화면 — Re:C 로고를 CRT 셰이더로 띄우고(<see cref="ShowLogo"/>), 게임 시작 때 월드에서 끈다(<see cref="PowerOff"/>).
/// 중앙 모니터 화면 렌더러의 머티리얼을 런타임 인스턴스로 바꿔 쓴다.
/// </summary>
/// <remarks>
/// 🔴 원본 화면 셰이더는 Re:C 로고를 **노드 안 텍스처**로 박아 둬서 바깥에서 못 바꾼다 → 머티리얼을 통째로 교체한다.
/// ⚠️ 10-26(PLAN-title-start-settings-tabs) 타이틀 메뉴·설정창이 없어져 모니터 UI 카메라·RT·화면 클릭 변환을 지웠다.
/// 🔴 벽 모니터 13대는 이름이 같다(<c>monitor_screen</c>). 대상은 **인스펙터 참조**로만 정한다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TitleMonitorDisplay : MonoBehaviour
{
    public static TitleMonitorDisplay Active { get; private set; }

    [Header("대상")]
    [Tooltip("중앙 모니터의 화면 렌더러(TitleOffice/monitor/monitor_screen). 벽 모니터와 이름이 같으니 반드시 직접 지정.")]
    [SerializeField] private Renderer _screenRenderer;

    [Tooltip("화면 머티리얼 원본(Title/CRTScreen). 런타임에 복제해 쓴다 — 애셋은 건드리지 않는다.")]
    [SerializeField] private Material _uiMaterialSource;

    [Tooltip("Idle 에 띄울 로고(Re:C). 원본 화면 머티리얼 대신 CRT 셰이더로 보여 준다 — 원본은 유리 반사에 스카이박스가 비쳤다(팀장 09-23).")]
    [SerializeField] private Texture _logoTexture;

    [Header("화면 비율")]
    [Tooltip("화면 가로/세로(꺼짐 연출의 둥근 사각 실루엣). 0 이면 화면 메시 bounds 로 자동 결정.")]
    [SerializeField, Min(0f)] private float _aspectOverride;

    [Header("UV 보정 (1단계 실측값)")]
    [Tooltip("화면 메시 UV 가 뒤집혀 있으면 켠다.")]
    [SerializeField] private bool _flipX;
    [SerializeField] private bool _flipY;

    private Material _uiMaterial;
    private Material[] _originalMaterials;

    /// <summary>중앙 화면 렌더러 — 시작 연출이 화면 중심(bounds)·방향을 잡는 데 쓴다. FBX 피벗은 메시 중심이 아니다.</summary>
    public Renderer ScreenRenderer => _screenRenderer;

    /// <summary>화면에 실제로 쓰이는 인스턴스 — FX(TitleCrtFx)는 반드시 이걸 갱신한다.</summary>
    public Material ScreenMaterial => _uiMaterial;

    private void Awake()
    {
        if (_screenRenderer == null || _uiMaterialSource == null)
        {
            Debug.LogError("[TitleMonitor] 화면 렌더러 / 화면 머티리얼 참조가 비었다 — 로고·꺼짐 연출이 안 나온다.", this);
            enabled = false;
            return;
        }

        float aspect = _aspectOverride > 0f ? _aspectOverride : MeasureScreenAspect();

        _uiMaterial = new Material(_uiMaterialSource) { name = "TitleMonitorScreen (Instance)" };
        _uiMaterial.SetFloat(IdPower, 1f);
        ApplyCrtLook();   // CRT 값은 머티리얼이 아니라 이 컴포넌트 인스펙터가 정본(10-13)
        _uiMaterial.SetFloat(IdScreenAspect, aspect);   // 꺼짐 연출(둥근 사각 실루엣) 비율 — 화면 메시 기준
        Vector2 scale = new(_flipX ? -1f : 1f, _flipY ? -1f : 1f);
        Vector2 offset = new(_flipX ? 1f : 0f, _flipY ? 1f : 0f);
        _uiMaterial.mainTextureScale = scale;
        _uiMaterial.mainTextureOffset = offset;

        _originalMaterials = _screenRenderer.sharedMaterials;

        Debug.Log($"[TitleMonitor] aspect {aspect:F3} flip=({_flipX},{_flipY})", this);
    }

    // 타이틀에 있는 동안 3D 렌더 스케일 100% — 모니터 글씨가 렌더 스케일 설정에 깨지지 않게(팀장 10-13).
    private void OnEnable()
    {
        Active = this;
        GraphicsQualityConfig.SetForceFullRenderScale(true);
    }

    private void Start()
    {
        if (TitleCrtFx.Active != null) TitleCrtFx.Active.Register(_uiMaterial);
    }

    private void OnDisable()
    {
        if (Active == this) Active = null;
        GraphicsQualityConfig.SetForceFullRenderScale(false);   // 타이틀을 떠나면 저장된 렌더 스케일로
    }

    private void OnDestroy()
    {
        if (_screenRenderer != null && _originalMaterials != null)
            _screenRenderer.sharedMaterials = _originalMaterials;
        if (_uiMaterial != null) Destroy(_uiMaterial);
    }

    // ─── CRT 화면 효과 (인스펙터 — Play 중 바로 반영) ─────────────────────
    // 🔴 화면에 쓰이는 건 머티리얼 원본의 런타임 복제본이라 Play 중 원본(.mat)을 고쳐도 안 바뀌었다 → 값을 여기로 뺐다
    //    (팀장 10-13: 아트가 스크립트를 열지 않고 이 컴포넌트 인스펙터에서 바로 조절·확인). 머티리얼 값은 더 이상 안 쓴다.
    //    기본값 = MA_TitleMonitorUI 저작값(10-13 시점) — 화면이 바뀌지 않게 맞췄다.
    //    찢김·상시 지직거림 = TitleCrtFx, 전환 지직거림 = TitleFlowDirector 인스펙터.
    [Header("CRT 화면 효과 (Play 중 바로 반영)")]
    [Tooltip("색수차 — RGB 가 좌우로 갈라지는 폭(텍셀). 글씨 번짐의 주원인.")]
    [SerializeField, Range(0f, 8f)] private float _chromaPx = 2f;
    [Tooltip("주사선 개수(화면 세로).")]
    [SerializeField, Min(0f)] private float _scanCount = 300f;
    [Tooltip("주사선 어둡기.")]
    [SerializeField, Range(0f, 1f)] private float _scanStrength = 0.28f;
    [Tooltip("글로우(밝은 곳 번짐) 세기.")]
    [SerializeField, Range(0f, 2f)] private float _glow = 0.55f;
    [Tooltip("글로우 반경(텍셀).")]
    [SerializeField, Range(0f, 12f)] private float _glowRadiusPx = 4f;
    [Tooltip("노이즈 입자.")]
    [SerializeField, Range(0f, 0.3f)] private float _grain = 0.05f;
    [Tooltip("가로줄 떨림.")]
    [SerializeField, Range(0f, 0.01f)] private float _jitter = 0.0012f;
    [Tooltip("밝기 배율(1 = 원본). 1 보다 크면 흰 면이 포화돼 번진다.")]
    [SerializeField, Range(0f, 3f)] private float _brightness = 1.25f;
    [Tooltip("가장자리 어둡게.")]
    [SerializeField, Range(0f, 2f)] private float _vignette = 0.6f;
    [Tooltip("화면 색조.")]
    [SerializeField] private Color _tint = new(0.86f, 0.96f, 1f, 1f);

    private static readonly int IdChromaPx = Shader.PropertyToID("_ChromaPx");
    private static readonly int IdScanCount = Shader.PropertyToID("_ScanCount");
    private static readonly int IdScanStrength = Shader.PropertyToID("_ScanStrength");
    private static readonly int IdGlow = Shader.PropertyToID("_Glow");
    private static readonly int IdGlowRadiusPx = Shader.PropertyToID("_GlowRadiusPx");
    private static readonly int IdGrain = Shader.PropertyToID("_Grain");
    private static readonly int IdJitter = Shader.PropertyToID("_Jitter");
    private static readonly int IdBrightness = Shader.PropertyToID("_Brightness");
    private static readonly int IdVignette = Shader.PropertyToID("_Vignette");
    private static readonly int IdTint = Shader.PropertyToID("_Tint");

    // 인스펙터에서 값을 바꾸면 Play 중 바로 화면에 반영한다.
    private void OnValidate()
    {
        if (Application.isPlaying && _uiMaterial != null) ApplyCrtLook();
    }

    private void ApplyCrtLook()
    {
        _uiMaterial.SetFloat(IdChromaPx, _chromaPx);
        _uiMaterial.SetFloat(IdScanCount, _scanCount);
        _uiMaterial.SetFloat(IdScanStrength, _scanStrength);
        _uiMaterial.SetFloat(IdGlow, _glow);
        _uiMaterial.SetFloat(IdGlowRadiusPx, _glowRadiusPx);
        _uiMaterial.SetFloat(IdGrain, _grain);
        _uiMaterial.SetFloat(IdJitter, _jitter);
        _uiMaterial.SetFloat(IdBrightness, _brightness);
        _uiMaterial.SetFloat(IdVignette, _vignette);
        _uiMaterial.SetColor(IdTint, _tint);
    }

    /// <summary>Idle·접근 — Re:C 로고를 CRT 셰이더로(지직거림은 TitleCrtFx 버스트). 로고가 없으면 원본 머티리얼.</summary>
    public void ShowLogo()
    {
        if (!enabled) return;
        if (_logoTexture == null)
        {
            _screenRenderer.sharedMaterials = _originalMaterials;
            return;
        }

        _uiMaterial.mainTexture = _logoTexture;
        ApplyScreenMaterial();
    }

    /// <summary>
    /// 게임 시작 — 화면을 **월드에서** CRT 처럼 끈다(<c>_Power</c> 1→0 = Title/CRTOff 와 같은 연출을 화면 메시 안에서, 끝 = 검정).
    /// 끝나면 불투명 검정이 남는다 — 그 위로 검정 페이드 → 로비.
    /// </summary>
    public System.Collections.IEnumerator PowerOff(float duration)
    {
        if (!enabled || _uiMaterial == null) yield break;

        ApplyScreenMaterial();   // 로고 상태(원본 머티리얼)였어도 꺼짐 셰이더가 걸리게

        float t = 0f;
        duration = Mathf.Max(0.01f, duration);
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            // 선형 — 원본 TitlePowerOff 와 같다(곡선은 셰이더 안 구간 이징이 만든다).
            _uiMaterial.SetFloat(IdPower, 1f - Mathf.Clamp01(t / duration));
            yield return null;
        }
        _uiMaterial.SetFloat(IdPower, 0f);
    }

    private static readonly int IdPower = Shader.PropertyToID("_Power");
    private static readonly int IdScreenAspect = Shader.PropertyToID("_ScreenAspect");

    private void ApplyScreenMaterial()
    {
        var mats = (Material[])_originalMaterials.Clone();
        for (int i = 0; i < mats.Length; i++) mats[i] = _uiMaterial;
        _screenRenderer.sharedMaterials = mats;
    }

    private float MeasureScreenAspect()
    {
        var mf = _screenRenderer.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return 4f / 3f;
        Vector3 s = mf.sharedMesh.bounds.size; // 비읽기(isReadable=0) 메시도 bounds 는 읽힌다
        float w = Mathf.Max(s.x, s.z), h = s.y;
        return h > 1e-4f ? Mathf.Clamp(w / h, 0.5f, 3f) : 4f / 3f;
    }
}
