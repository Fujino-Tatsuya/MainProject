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

    private void OnEnable() => Active = this;

    private void Start()
    {
        if (TitleCrtFx.Active != null) TitleCrtFx.Active.Register(_uiMaterial);
    }

    private void OnDisable()
    {
        if (Active == this) Active = null;
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
