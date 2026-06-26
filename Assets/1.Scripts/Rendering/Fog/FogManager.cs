// ----------------------------------------------------------------------------
//  FogManager.cs - 씬 포그 컨트롤러 (씬당 1개)
//  전역 설정(FogProfile)과 페인트 마스크를 글로벌 셰이더 프로퍼티로 푸시하고,
//  활성 FogVolume 들을 거리 컬링 + 개수 캡으로 수집해 셰이더 배열로 업로드한다.
//  [ExecuteAlways] 라 에디터에서도 실시간 미리보기.
// ----------------------------------------------------------------------------
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Rendering/Fog Manager")]
public sealed class FogManager : MonoBehaviour
{
    public const int MaxVolumes = 16;

    [Header("전역")]
    public bool fogEnabled = true;
    public FogProfile profile;

    [Header("층 디밍 (전장의 안개)")]
    [Tooltip("포그와 독립. 플레이어 y 기준 범위 밖 픽셀의 채도·명도를 낮춘다.")]
    public bool dimEnabled = true;
    [Tooltip("비우면 CameraTargetSwitcher.Active 의 현재 추적 타겟 → 'CameraFollowTarget' 태그 순으로 자동 탐색.")]
    public Transform dimPlayerOverride;

    [Header("시야 차폐 (LoS — 벽/노드 뒤)")]
    [Tooltip("벽/노드 뒤를 가려 어둡게. dimEnabled 가 켜져 있어야 적용됨.")]
    public bool losEnabled = false;
    [Tooltip("벽 등 차폐체 콜라이더 레이어. 이 레이어만 raycast 로 시야를 막는다.")]
    public LayerMask losWallMask = ~0;
    [Tooltip("각도 해상도(빈 개수). 클수록 그림자 경계 정밀, 비용↑.")]
    [Range(64, 720)] public int losTexels = 360;
    [Tooltip("시야가 닿는 최대 거리(m). 이 너머는 항상 가려진 것으로 본다.")]
    [Min(1f)] public float losMaxDist = 40f;
    [Tooltip("raycast 시작 높이(플레이어 발끝 위 오프셋, m). 벽 중간 높이를 맞추기 위함.")]
    public float losRayHeight = 1f;
    [Tooltip("노드도 시야를 막을지. 노드는 콜라이더 대신 위치+Tier 원형으로 처리.")]
    public bool losNodesBlock = true;
    [Tooltip("이 Tier 까지만 차폐(더 작은 건 제외). Tier1_Large=0..Tier3_Small=2. 기본 전부 차폐.")]
    public NodeTier losNodeMaxTier = NodeTier.Tier3_Small;
    [Tooltip("노드 차폐 반경 배율(Tier 기본 반경 7.5/5/2.5m 에 곱).")]
    [Min(0.01f)] public float losNodeRadiusScale = 1f;

    [Header("페인트 마스크 (순수 비주얼)")]
    public bool maskEnabled = false;
    public Texture2D maskTexture;
    [Tooltip("마스크가 덮는 월드 영역의 중심.")]
    public Vector3 maskCenter = Vector3.zero;
    [Tooltip("마스크가 덮는 월드 영역의 크기(X, Z).")]
    public Vector2 maskSize = new Vector2(100f, 100f);
    [Range(0f, 1f)] public float maskTintStrength = 1f;

    [Header("성능")]
    [Range(1, MaxVolumes)] public int maxVolumes = MaxVolumes;

    // ----- static registry -----
    private static FogManager s_active;
    private static readonly List<FogVolume> s_volumes = new List<FogVolume>();

    // 포그 또는 디밍 중 하나라도 켜져 있으면 렌더 패스를 큐잉한다(독립 토글).
    public static bool HasActiveInstance =>
        s_active != null && s_active.isActiveAndEnabled && (s_active.fogEnabled || s_active.dimEnabled);

    public static void Register(FogVolume v)
    {
        if (v != null && !s_volumes.Contains(v))
            s_volumes.Add(v);
    }

    public static void Unregister(FogVolume v) => s_volumes.Remove(v);

    // ----- buffers -----
    private readonly Vector4[] _params0 = new Vector4[MaxVolumes];
    private readonly Vector4[] _colors = new Vector4[MaxVolumes];
    private readonly Vector4[] _bounds = new Vector4[MaxVolumes];
    private readonly Matrix4x4[] _w2l = new Matrix4x4[MaxVolumes];
    private readonly List<FogVolume> _sortList = new List<FogVolume>(MaxVolumes * 2);
    private Vector3 _camPos;
    private FogProfile _fallback;
    private System.Comparison<FogVolume> _cmp;

    // 디밍: 플레이어 추적 타겟 캐시 + 마지막 위치(타겟 소실 시 유지). 층(y)+시야(xz) 공용.
    private Transform _dimTargetCache;
    private Vector3 _lastDimPlayerPos;

    // 시야 차폐: 라디얼 거리맵 + 노드 차폐 캐시(정적 맵 → 1회)
    private Texture2D _losTex;
    private float[] _losDist;
    private readonly List<Vector4> _losNodes = new List<Vector4>(); // xyz=pos, w=radius
    private bool _losNodesCached;

    // ----- shader property ids -----
    private static readonly int ID_GlobalEnabled = Shader.PropertyToID("_FogGlobalEnabled");
    private static readonly int ID_Color = Shader.PropertyToID("_FogColor");
    private static readonly int ID_Density = Shader.PropertyToID("_FogDensity");
    private static readonly int ID_DistanceMode = Shader.PropertyToID("_FogDistanceMode");
    private static readonly int ID_DistanceStart = Shader.PropertyToID("_FogDistanceStart");
    private static readonly int ID_DistanceEnd = Shader.PropertyToID("_FogDistanceEnd");
    private static readonly int ID_HeightStart = Shader.PropertyToID("_FogHeightStart");
    private static readonly int ID_HeightEnd = Shader.PropertyToID("_FogHeightEnd");
    private static readonly int ID_HeightStrength = Shader.PropertyToID("_FogHeightStrength");
    private static readonly int ID_MaxOpacity = Shader.PropertyToID("_FogMaxOpacity");
    private static readonly int ID_SkyboxInfluence = Shader.PropertyToID("_FogSkyboxInfluence");

    private static readonly int ID_SunColor = Shader.PropertyToID("_FogSunColor");
    private static readonly int ID_SunDir = Shader.PropertyToID("_FogSunDir");
    private static readonly int ID_SunIntensity = Shader.PropertyToID("_FogSunIntensity");
    private static readonly int ID_SunPower = Shader.PropertyToID("_FogSunPower");

    private static readonly int ID_NoiseEnabled = Shader.PropertyToID("_FogNoiseEnabled");
    private static readonly int ID_NoiseUseTexture = Shader.PropertyToID("_FogNoiseUseTexture");
    private static readonly int ID_NoiseScale = Shader.PropertyToID("_FogNoiseScale");
    private static readonly int ID_NoiseStrength = Shader.PropertyToID("_FogNoiseStrength");
    private static readonly int ID_NoiseScroll = Shader.PropertyToID("_FogNoiseScroll");
    private static readonly int ID_NoiseTex = Shader.PropertyToID("_FogNoiseTex");

    private static readonly int ID_MaskEnabled = Shader.PropertyToID("_FogMaskEnabled");
    private static readonly int ID_MaskRect = Shader.PropertyToID("_FogMaskRect");
    private static readonly int ID_MaskTintStrength = Shader.PropertyToID("_FogMaskTintStrength");
    private static readonly int ID_MaskTex = Shader.PropertyToID("_FogMaskTex");

    private static readonly int ID_VolumeCount = Shader.PropertyToID("_FogVolumeCount");
    private static readonly int ID_VolParams0 = Shader.PropertyToID("_FogVolumeParams0");
    private static readonly int ID_VolColor = Shader.PropertyToID("_FogVolumeColor");
    private static readonly int ID_VolBounds = Shader.PropertyToID("_FogVolumeBounds");
    private static readonly int ID_VolW2L = Shader.PropertyToID("_FogVolumeWorldToLocal");

    private static readonly int ID_DimEnabled = Shader.PropertyToID("_DimEnabled");
    private static readonly int ID_DimPlayerY = Shader.PropertyToID("_DimPlayerY");
    private static readonly int ID_DimRangeUp = Shader.PropertyToID("_DimRangeUp");
    private static readonly int ID_DimRangeDown = Shader.PropertyToID("_DimRangeDown");
    private static readonly int ID_DimFadeUp = Shader.PropertyToID("_DimFadeUp");
    private static readonly int ID_DimFadeDown = Shader.PropertyToID("_DimFadeDown");
    private static readonly int ID_DimSaturation = Shader.PropertyToID("_DimSaturation");
    private static readonly int ID_DimBrightness = Shader.PropertyToID("_DimBrightness");
    private static readonly int ID_DimAffectSky = Shader.PropertyToID("_DimAffectSky");
    private static readonly int ID_DimPlayerXZ = Shader.PropertyToID("_DimPlayerXZ");
    private static readonly int ID_ViewRange = Shader.PropertyToID("_ViewRange");
    private static readonly int ID_ViewFade = Shader.PropertyToID("_ViewFade");

    private static readonly int ID_LosEnabled = Shader.PropertyToID("_LosEnabled");
    private static readonly int ID_LosTex = Shader.PropertyToID("_LosTex");
    private static readonly int ID_LosMaxDist = Shader.PropertyToID("_LosMaxDist");
    private static readonly int ID_LosDarken = Shader.PropertyToID("_LosDarken");
    private static readonly int ID_LosDistanceBias = Shader.PropertyToID("_LosDistanceBias");
    private static readonly int ID_LosEdgeFade = Shader.PropertyToID("_LosEdgeFade");

    private void OnEnable()
    {
        s_active = this;
        _cmp = CompareByDistance;
    }

    private void OnDisable()
    {
        if (s_active == this)
        {
            s_active = null;
            Shader.SetGlobalFloat(ID_GlobalEnabled, 0f);
            Shader.SetGlobalFloat(ID_DimEnabled, 0f);
            Shader.SetGlobalFloat(ID_LosEnabled, 0f);
        }
    }

    private void LateUpdate() => PushGlobals();

#if UNITY_EDITOR
    private void OnValidate()
    {
        // 프리팹 격리 편집 등에서 씬 전역을 덮어쓰지 않도록 활성 인스턴스일 때만.
        if (isActiveAndEnabled && s_active == this)
            PushGlobals();
    }
#endif

    private FogProfile EffectiveProfile
    {
        get
        {
            if (profile != null)
                return profile;
            if (_fallback == null)
            {
                _fallback = ScriptableObject.CreateInstance<FogProfile>();
                _fallback.hideFlags = HideFlags.HideAndDontSave;
            }
            return _fallback;
        }
    }

    private void PushGlobals()
    {
        if (!isActiveAndEnabled)
        {
            Shader.SetGlobalFloat(ID_GlobalEnabled, 0f);
            Shader.SetGlobalFloat(ID_DimEnabled, 0f);
            return;
        }

        FogProfile p = EffectiveProfile;
        PushFogGlobals(p);
        PushDimGlobals(p);
    }

    private void PushFogGlobals(FogProfile p)
    {
        if (!fogEnabled)
        {
            Shader.SetGlobalFloat(ID_GlobalEnabled, 0f);
            return;
        }

        Shader.SetGlobalFloat(ID_GlobalEnabled, 1f);
        Shader.SetGlobalVector(ID_Color, (Vector4)p.color);
        Shader.SetGlobalFloat(ID_Density, p.density);
        Shader.SetGlobalInteger(ID_DistanceMode, (int)p.distanceMode);
        Shader.SetGlobalFloat(ID_DistanceStart, p.distanceStart);
        Shader.SetGlobalFloat(ID_DistanceEnd, p.distanceEnd);
        Shader.SetGlobalFloat(ID_HeightStart, p.heightStart);
        Shader.SetGlobalFloat(ID_HeightEnd, p.heightEnd);
        Shader.SetGlobalFloat(ID_HeightStrength, p.heightStrength);
        Shader.SetGlobalFloat(ID_MaxOpacity, p.maxOpacity);
        Shader.SetGlobalFloat(ID_SkyboxInfluence, p.skyboxInfluence);

        // 태양 방향 — 관례 통일: travel = 빛이 진행하는 방향(라이트 forward),
        // _FogSunDir = 태양을 향하는 방향(-travel). 셰이더는 dot(viewDir, _FogSunDir).
        Vector3 travel = (p.useMainLightDirection && RenderSettings.sun != null)
            ? RenderSettings.sun.transform.forward
            : p.sunDirection;
        Vector3 sunDir = -travel;
        if (sunDir.sqrMagnitude < 1e-6f)
            sunDir = Vector3.up;
        sunDir.Normalize();
        Shader.SetGlobalVector(ID_SunColor, (Vector4)p.sunColor);
        Shader.SetGlobalVector(ID_SunDir, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
        Shader.SetGlobalFloat(ID_SunIntensity, p.sunIntensity);
        Shader.SetGlobalFloat(ID_SunPower, p.sunPower);

        // 노이즈
        bool useTex = p.noiseEnabled && p.noiseTexture != null;
        Shader.SetGlobalFloat(ID_NoiseEnabled, p.noiseEnabled ? 1f : 0f);
        Shader.SetGlobalFloat(ID_NoiseUseTexture, useTex ? 1f : 0f);
        Shader.SetGlobalFloat(ID_NoiseScale, p.noiseScale);
        Shader.SetGlobalFloat(ID_NoiseStrength, p.noiseStrength);
        Shader.SetGlobalVector(ID_NoiseScroll, new Vector4(p.noiseScroll.x, p.noiseScroll.y, 0f, 0f));
        if (useTex)
            Shader.SetGlobalTexture(ID_NoiseTex, p.noiseTexture);

        // 마스크
        bool useMask = maskEnabled && maskTexture != null;
        Shader.SetGlobalFloat(ID_MaskEnabled, useMask ? 1f : 0f);
        if (useMask)
        {
            float minX = maskCenter.x - maskSize.x * 0.5f;
            float minZ = maskCenter.z - maskSize.y * 0.5f;
            Shader.SetGlobalVector(ID_MaskRect, new Vector4(minX, minZ,
                Mathf.Max(1e-4f, maskSize.x), Mathf.Max(1e-4f, maskSize.y)));
            Shader.SetGlobalFloat(ID_MaskTintStrength, maskTintStrength);
            Shader.SetGlobalTexture(ID_MaskTex, maskTexture);
        }

        // 볼륨
        int count = CollectVolumes();
        Shader.SetGlobalInteger(ID_VolumeCount, count);
        Shader.SetGlobalVectorArray(ID_VolParams0, _params0);
        Shader.SetGlobalVectorArray(ID_VolColor, _colors);
        Shader.SetGlobalVectorArray(ID_VolBounds, _bounds);
        Shader.SetGlobalMatrixArray(ID_VolW2L, _w2l);
    }

    private void PushDimGlobals(FogProfile p)
    {
        if (!dimEnabled)
        {
            Shader.SetGlobalFloat(ID_DimEnabled, 0f);
            Shader.SetGlobalFloat(ID_LosEnabled, 0f);
            return;
        }

        Vector3 playerPos = ResolveDimPlayerPos();
        Shader.SetGlobalFloat(ID_DimEnabled, 1f);
        Shader.SetGlobalFloat(ID_DimPlayerY, playerPos.y);
        Shader.SetGlobalVector(ID_DimPlayerXZ, new Vector4(playerPos.x, playerPos.z, 0f, 0f));
        Shader.SetGlobalFloat(ID_ViewRange, p.viewRange);
        Shader.SetGlobalFloat(ID_ViewFade, p.viewFade);
        Shader.SetGlobalFloat(ID_DimRangeUp, p.dimRangeUp);
        Shader.SetGlobalFloat(ID_DimRangeDown, p.dimRangeDown);
        Shader.SetGlobalFloat(ID_DimFadeUp, p.dimFadeUp);
        Shader.SetGlobalFloat(ID_DimFadeDown, p.dimFadeDown);
        Shader.SetGlobalFloat(ID_DimSaturation, p.dimSaturation);
        Shader.SetGlobalFloat(ID_DimBrightness, p.dimBrightness);
        Shader.SetGlobalFloat(ID_DimAffectSky, p.dimAffectSky);

        PushLos(p, playerPos);
    }

    // 플레이어 추적 위치(y=층, xz=시야): override → 카메라 추적 타겟 → 태그 캐시 → 마지막 값 순.
    private Vector3 ResolveDimPlayerPos()
    {
        // 1) 인스펙터 명시 override
        if (dimPlayerOverride != null)
        {
            _lastDimPlayerPos = dimPlayerOverride.position;
            return _lastDimPlayerPos;
        }

        // 2) 카메라가 현재 따라가는 대상(= 플레이어). '[' / ']' 전환에도 자동 추적.
        CameraTargetSwitcher cts = CameraTargetSwitcher.Active;
        if (cts != null && cts.CurrentFollowTarget != null)
        {
            _dimTargetCache = cts.CurrentFollowTarget;
            _lastDimPlayerPos = _dimTargetCache.position;
            return _lastDimPlayerPos;
        }

        // 3) 태그 검색 캐시(매 프레임 Find 금지 — 캐시 살아있으면 재사용)
        if (_dimTargetCache == null)
        {
            GameObject go = GameObject.FindGameObjectWithTag("CameraFollowTarget");
            if (go != null)
                _dimTargetCache = go.transform;
        }
        if (_dimTargetCache != null)
        {
            _lastDimPlayerPos = _dimTargetCache.position;
            return _lastDimPlayerPos;
        }

        // 4) fallback: 마지막 값 유지(씬 전환/스폰 전 깜빡임 방지)
        return _lastDimPlayerPos;
    }

    // ----- 시야 차폐 (LoS) -----
    // 벽=콜라이더 raycast, 노드=위치+Tier 원형. 라디얼 거리맵(_losTex)에 합성.
    private void PushLos(FogProfile p, Vector3 playerPos)
    {
        if (!losEnabled)
        {
            Shader.SetGlobalFloat(ID_LosEnabled, 0f);
            return;
        }

        BuildRadialMap(playerPos);

        Shader.SetGlobalFloat(ID_LosEnabled, 1f);
        Shader.SetGlobalTexture(ID_LosTex, _losTex);
        Shader.SetGlobalFloat(ID_LosMaxDist, losMaxDist);
        Shader.SetGlobalFloat(ID_LosDarken, p.losDarken);
        Shader.SetGlobalFloat(ID_LosDistanceBias, p.losDistanceBias);
        Shader.SetGlobalFloat(ID_LosEdgeFade, p.losEdgeFade);
    }

    // 각도별 최근접 차폐 거리 → _losTex(RFloat, n×1). 플레이어 이동 시 갱신.
    // CPU 각도 i→ang = i/n*2PI - PI, 셰이더 u = ang/2PI + 0.5 와 일치.
    private void BuildRadialMap(Vector3 playerPos)
    {
        int n = Mathf.Clamp(losTexels, 64, 720);
        if (_losTex == null || _losTex.width != n)
        {
            if (_losTex != null)
            {
                if (Application.isPlaying) Destroy(_losTex);
                else DestroyImmediate(_losTex);
            }
            _losTex = new Texture2D(n, 1, TextureFormat.RFloat, false, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = "FogLosRadialMap"
            };
            _losDist = new float[n];
        }

        float maxD = losMaxDist;
        for (int i = 0; i < n; i++) _losDist[i] = maxD;

        // (1) 벽 등 콜라이더 raycast (수평 레이라 바닥은 안 맞음)
        Vector3 origin = playerPos + Vector3.up * losRayHeight;
        for (int i = 0; i < n; i++)
        {
            float ang = ((float)i / n) * (2f * Mathf.PI) - Mathf.PI;
            Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            if (Physics.Raycast(origin, dir, out RaycastHit hit, maxD, losWallMask, QueryTriggerInteraction.Ignore))
                _losDist[i] = hit.distance;
        }

        // (2) 노드 원형 차폐 (콜라이더 없음 → 위치+Tier 반경)
        if (losNodesBlock)
        {
            if (!_losNodesCached) CacheNodes();
            float twoPi = 2f * Mathf.PI;
            for (int k = 0; k < _losNodes.Count; k++)
            {
                Vector4 nd = _losNodes[k];
                float dx = nd.x - playerPos.x, dz = nd.z - playerPos.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                float r = nd.w;
                if (d <= r || d > maxD) continue;
                float surf = d - r;                          // 원 표면까지 거리(근사)
                float center = Mathf.Atan2(dz, dx);
                float half = Mathf.Asin(Mathf.Clamp01(r / d));
                int i0 = Mathf.FloorToInt(((center - half + Mathf.PI) / twoPi) * n);
                int i1 = Mathf.CeilToInt(((center + half + Mathf.PI) / twoPi) * n);
                for (int i = i0; i <= i1; i++)
                {
                    int bin = ((i % n) + n) % n;             // 각도 wrap
                    if (surf < _losDist[bin]) _losDist[bin] = surf;
                }
            }
        }

        _losTex.SetPixelData(_losDist, 0);
        _losTex.Apply(false);
    }

    // 차폐 노드 수집(정적 맵 → 1회). Tier 컷오프 적용.
    private void CacheNodes()
    {
        _losNodes.Clear();
        NodeMarker[] nodes = FindObjectsByType<NodeMarker>(FindObjectsSortMode.None);
        for (int i = 0; i < nodes.Length; i++)
        {
            NodeMarker nd = nodes[i];
            if ((int)nd.Tier > (int)losNodeMaxTier) continue;   // 더 작은 Tier 제외
            Vector3 pos = nd.transform.position;
            _losNodes.Add(new Vector4(pos.x, pos.y, pos.z, TierToRadius(nd.Tier) * losNodeRadiusScale));
        }
        _losNodesCached = true;
    }

    // 맵 재생성 시 노드 캐시 무효화(외부 호출용).
    public void InvalidateLosNodes() => _losNodesCached = false;

    private static float TierToRadius(NodeTier t)
    {
        switch (t)
        {
            case NodeTier.Tier1_Large: return 7.5f;
            case NodeTier.Tier2_Medium: return 5f;
            case NodeTier.Tier3_Small: return 2.5f;
            default: return 3f;
        }
    }

    private int CollectVolumes()
    {
        _cmp ??= CompareByDistance;
        s_volumes.RemoveAll(v => v == null);

        Camera cam = GetCullCamera();
        _camPos = cam != null ? cam.transform.position : transform.position;

        _sortList.Clear();
        for (int i = 0; i < s_volumes.Count; i++)
        {
            FogVolume v = s_volumes[i];
            if (v != null && v.isActiveAndEnabled)
                _sortList.Add(v);
        }
        _sortList.Sort(_cmp);

        int cap = Mathf.Clamp(maxVolumes, 1, MaxVolumes);
        int count = Mathf.Min(cap, _sortList.Count);
        for (int i = 0; i < count; i++)
        {
            FogVolume v = _sortList[i];
            _params0[i] = v.GetParams0();
            _colors[i] = (Vector4)v.color;
            _bounds[i] = v.GetBounds();
            _w2l[i] = v.GetWorldToLocal();
        }
        for (int i = count; i < MaxVolumes; i++)
        {
            _params0[i] = Vector4.zero;
            _colors[i] = Vector4.zero;
            _bounds[i] = Vector4.zero;
            _w2l[i] = Matrix4x4.identity;
        }
        return count;
    }

    private int CompareByDistance(FogVolume a, FogVolume b)
    {
        float da = (a.transform.position - _camPos).sqrMagnitude;
        float db = (b.transform.position - _camPos).sqrMagnitude;
        return da.CompareTo(db);
    }

    private static Camera GetCullCamera()
    {
        if (Camera.main != null) return Camera.main;
        if (Camera.current != null) return Camera.current;
        return Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!maskEnabled) return;
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 1f);
        Vector3 c = new Vector3(maskCenter.x, maskCenter.y, maskCenter.z);
        Gizmos.DrawWireCube(c, new Vector3(maskSize.x, 0.05f, maskSize.y));
    }
#endif
}
