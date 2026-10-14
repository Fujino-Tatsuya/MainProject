using UnityEngine;

/// <summary>
/// 중앙 CRT 화면에 **UI 렌더텍스처**를 띄운다(계획서 PLAN-title-flow §0.4-A).
/// UI 카메라(<see cref="_uiCamera"/>)가 메뉴·설정 캔버스를 RT 에 그리고, 중앙 모니터 화면 렌더러의
/// 머티리얼을 런타임 인스턴스로 바꿔 그 RT 를 보여 준다. 클릭은 <see cref="TryScreenToUIPixel"/> 로
/// 화면 좌표 → 화면 메시 UV → RT 픽셀로 되돌린다(<see cref="TitleScreenRaycaster"/>·<see cref="TitleScreenSlider"/>).
/// </summary>
/// <remarks>
/// 🔴 원본 화면 셰이더는 Re:C 로고를 **노드 안 텍스처**로 박아 둬서 바깥에서 못 바꾼다 → 머티리얼을 통째로 교체한다.
///    Idle 동안은 원본 머티리얼(로고)을 그대로 두고, 입력 이후에만 UI 머티리얼로 바꾼다.
/// 🔴 벽 모니터 13대는 이름이 같다(<c>monitor_screen</c>). 대상은 **인스펙터 참조**로만 정한다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TitleMonitorDisplay : MonoBehaviour
{
    public static TitleMonitorDisplay Active { get; private set; }

    [Header("대상")]
    [Tooltip("중앙 모니터의 화면 렌더러(TitleOffice/monitor/monitor_screen). 벽 모니터와 이름이 같으니 반드시 직접 지정.")]
    [SerializeField] private Renderer _screenRenderer;

    [Tooltip("메뉴·설정 캔버스를 그리는 UI 전용 카메라(UI 레이어만, 직교).")]
    [SerializeField] private Camera _uiCamera;

    [Tooltip("RT 를 보여 줄 머티리얼 원본(Title/CRTScreen). 런타임에 복제해 쓴다 — 애셋은 건드리지 않는다.")]
    [SerializeField] private Material _uiMaterialSource;

    [Tooltip("Idle 에 띄울 로고(Re:C). 원본 화면 머티리얼 대신 CRT 셰이더로 보여 준다 — 원본은 유리 반사에 스카이박스가 비쳤다(팀장 09-23).")]
    [SerializeField] private Texture _logoTexture;

    [Header("해상도")]
    [SerializeField, Min(64)] private int _rtHeight = 1080;

    [Tooltip("RT 가로/세로. 0 이면 화면 메시 bounds 로 자동 결정.")]
    [SerializeField, Min(0f)] private float _aspectOverride;

    [Header("UV 보정 (1단계 실측값)")]
    [Tooltip("화면 메시 UV 가 뒤집혀 있으면 켠다. 머티리얼과 클릭 변환에 **같이** 적용된다.")]
    [SerializeField] private bool _flipX;
    [SerializeField] private bool _flipY;

    [Tooltip("🔧 켜면 RT 대신 방향 판정용 격자를 띄운다(좌하=빨강 · 우하=초록 · 좌상=파랑 · 우상=노랑).")]
    [SerializeField] private bool _showCalibrationGrid;

    private RenderTexture _rt;
    private Material _uiMaterial;
    private Material[] _originalMaterials;
    private MeshCollider _collider;
    private Camera _mainCamera;
    private Texture2D _grid;
    private bool _showingUI;

    public RenderTexture Texture => _rt;

    /// <summary>중앙 화면 렌더러 — 시작 연출이 화면 중심(bounds)·방향을 잡는 데 쓴다. FBX 피벗은 메시 중심이 아니다.</summary>
    public Renderer ScreenRenderer => _screenRenderer;

    /// <summary>화면에 실제로 쓰이는 인스턴스 — FX(TitleCrtFx)는 반드시 이걸 갱신한다.</summary>
    public Material ScreenMaterial => _uiMaterial;
    public Camera UICamera => _uiCamera;

    private void Awake()
    {
        if (_screenRenderer == null || _uiCamera == null || _uiMaterialSource == null)
        {
            Debug.LogError("[TitleMonitor] 화면 렌더러 / UI 카메라 / UI 머티리얼 참조가 비었다 — 모니터 메뉴가 안 뜬다.", this);
            enabled = false;
            return;
        }

        float aspect = _aspectOverride > 0f ? _aspectOverride : MeasureScreenAspect();
        int w = Mathf.Max(64, Mathf.RoundToInt(_rtHeight * aspect));
        _rt = new RenderTexture(w, _rtHeight, 24, RenderTextureFormat.ARGB32) { name = "TitleMonitorUI", antiAliasing = 1 };
        _rt.Create();
        _uiCamera.targetTexture = _rt;

        _uiMaterial = new Material(_uiMaterialSource) { name = "TitleMonitorUI (Instance)" };
        _uiMaterial.mainTexture = _rt;
        _uiMaterial.SetFloat(IdPower, 1f);
        ApplyCrtLook();   // CRT 값은 머티리얼이 아니라 이 컴포넌트 인스펙터가 정본(10-13)
        _uiMaterial.SetFloat(IdScreenAspect, aspect);   // 꺼짐 연출(둥근 사각 실루엣) 비율 — 화면 메시 기준
        Vector2 scale = new(_flipX ? -1f : 1f, _flipY ? -1f : 1f);
        Vector2 offset = new(_flipX ? 1f : 0f, _flipY ? 1f : 0f);
        _uiMaterial.mainTextureScale = scale;
        _uiMaterial.mainTextureOffset = offset;

        _originalMaterials = _screenRenderer.sharedMaterials;

        // textureCoord 는 MeshCollider 에서만 나온다. 아트 프리팹을 수정하지 않도록 런타임에 붙인다.
        _collider = _screenRenderer.GetComponent<MeshCollider>();
        if (_collider == null)
        {
            _collider = _screenRenderer.gameObject.AddComponent<MeshCollider>();
            var mf = _screenRenderer.GetComponent<MeshFilter>();
            if (mf != null) _collider.sharedMesh = mf.sharedMesh;
        }

        Debug.Log($"[TitleMonitor] RT {w}×{_rtHeight} (aspect {aspect:F3}) flip=({_flipX},{_flipY})", this);
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
        if (_uiCamera != null) _uiCamera.targetTexture = null;
        if (_rt != null) { _rt.Release(); Destroy(_rt); }
        if (_uiMaterial != null) Destroy(_uiMaterial);
        if (_grid != null) Destroy(_grid);
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

    // 설정창은 글·드롭다운이 작아 CRT 에 묻혀 안 읽혔다 → 설정창이 열린 동안만 위 효과(주사선 개수·색조 제외)와
    // 찢김·글리치를 이 비율만큼 "효과 없음" 쪽으로 당긴다. 메뉴로 돌아오면 원래 값. 인게임 ESC 는 CRT 가 없어 해당 없음.
    // (10-12 0.7 → 10-13 팀장 0.75. ⚠️ 글씨 흐림의 진짜 원인은 렌더 스케일이었다 — GraphicsQualityConfig.SetForceFullRenderScale)
    [Header("설정창 가독성")]
    [Tooltip("설정창이 열린 동안 CRT 효과를 줄이는 비율. 0 = 그대로, 1 = 완전히 끔. 찢김·글리치도 같은 비율.")]
    [SerializeField, Range(0f, 1f)] private float _settingsCrtReduction = 0.75f;

    private bool _settingsReadable;

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

    /// <summary>설정창 열림/닫힘 — 중앙 모니터 CRT 효과를 <see cref="_settingsCrtReduction"/> 만큼 줄이거나 되돌린다. 찢김·글리치도 같은 비율(<see cref="TitleCrtFx"/>).</summary>
    public void SetSettingsReadable(bool readable)
    {
        _settingsReadable = readable;
        if (_uiMaterial != null) ApplyCrtLook();
    }

    private void ApplyCrtLook()
    {
        float keep = _settingsReadable ? 1f - _settingsCrtReduction : 1f;
        _uiMaterial.SetFloat(IdChromaPx, _chromaPx * keep);
        _uiMaterial.SetFloat(IdScanCount, _scanCount);
        _uiMaterial.SetFloat(IdScanStrength, _scanStrength * keep);
        _uiMaterial.SetFloat(IdGlow, _glow * keep);
        _uiMaterial.SetFloat(IdGlowRadiusPx, _glowRadiusPx * keep);
        _uiMaterial.SetFloat(IdGrain, _grain * keep);
        _uiMaterial.SetFloat(IdJitter, _jitter * keep);
        _uiMaterial.SetFloat(IdBrightness, Mathf.Lerp(1f, _brightness, keep));   // 밝기는 1(원본)이 "효과 없음"
        _uiMaterial.SetFloat(IdVignette, _vignette * keep);
        _uiMaterial.SetColor(IdTint, _tint);

        if (TitleCrtFx.Active != null) TitleCrtFx.Active.SetEffectScale(_uiMaterial, keep);
    }

    /// <summary>Idle·접근 — Re:C 로고를 CRT 셰이더로(지직거림은 TitleCrtFx 버스트). 로고가 없으면 원본 머티리얼.</summary>
    public void ShowLogo()
    {
        if (!enabled) return;
        _showingUI = false;
        if (_logoTexture == null)
        {
            _screenRenderer.sharedMaterials = _originalMaterials;
            return;
        }

        _uiMaterial.mainTexture = _logoTexture;
        ApplyScreenMaterial();
    }

    /// <summary>카메라 도착 — UI RT(메뉴·설정).</summary>
    public void ShowUI()
    {
        if (!enabled) return;
        if (_showCalibrationGrid)
        {
            if (_grid == null) _grid = BuildCalibrationGrid();
            _uiMaterial.mainTexture = _grid;
        }
        else
        {
            _uiMaterial.mainTexture = _rt;
        }

        ApplyScreenMaterial();
        _showingUI = true;
    }

    /// <summary>
    /// 게임 시작 — 화면을 **월드에서** CRT 처럼 끈다(<c>_Power</c> 1→0 = Title/CRTOff 와 같은 연출을 화면 메시 안에서, 끝 = 검정).
    /// 시작하는 순간 화면 클릭을 막는다(<see cref="TryScreenToUIPixel"/> 은 Starting 을 모른다 — Codex 10-03).
    /// 끝나면 불투명 검정이 남는다(깊이 기록 유지 — 카메라가 그 검은 화면으로 밀고 들어간다).
    /// </summary>
    public System.Collections.IEnumerator PowerOff(float duration)
    {
        if (!enabled || _uiMaterial == null) yield break;

        _showingUI = false;
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

    /// <summary>
    /// 실제 화면 좌표 → UI RT 픽셀. 화면 메시에 안 맞으면 false(🔴 Clamp 하지 않는다 — 화면 밖 클릭이 가장자리 버튼을 누른다).
    /// </summary>
    public bool TryScreenToUIPixel(Vector2 screenPos, out Vector2 pixel)
    {
        pixel = default;
        if (!enabled || !_showingUI || _collider == null || _rt == null) return false;

        if (_mainCamera == null) _mainCamera = Camera.main;
        if (_mainCamera == null) return false;

        Ray ray = _mainCamera.ScreenPointToRay(screenPos);
        if (!_collider.Raycast(ray, out RaycastHit hit, _mainCamera.farClipPlane)) return false;

        Vector2 uv = hit.textureCoord;
        if (_flipX) uv.x = 1f - uv.x;
        if (_flipY) uv.y = 1f - uv.y;
        pixel = new Vector2(uv.x * _rt.width, uv.y * _rt.height);
        return true;
    }

    private float MeasureScreenAspect()
    {
        var mf = _screenRenderer.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return 4f / 3f;
        Vector3 s = mf.sharedMesh.bounds.size; // 비읽기(isReadable=0) 메시도 bounds 는 읽힌다
        float w = Mathf.Max(s.x, s.z), h = s.y;
        return h > 1e-4f ? Mathf.Clamp(w / h, 0.5f, 3f) : 4f / 3f;
    }

    private static Texture2D BuildCalibrationGrid()
    {
        const int n = 256;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "TitleMonitorGrid", filterMode = FilterMode.Point };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            bool right = x >= n / 2, top = y >= n / 2;
            Color32 c = top ? (right ? new Color32(255, 230, 0, 255) : new Color32(40, 90, 255, 255))
                            : (right ? new Color32(0, 200, 60, 255) : new Color32(230, 30, 30, 255));
            bool line = x % 32 == 0 || y % 32 == 0 || x < 4 || y < 4 || x > n - 5 || y > n - 5;
            px[y * n + x] = line ? new Color32(255, 255, 255, 255) : c;
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }
}
