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

    // 층 디밍: 플레이어 y 추적 타겟 캐시 + 마지막 값(타겟 소실 시 유지)
    private Transform _dimTargetCache;
    private float _lastDimPlayerY;

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
            return;
        }

        Shader.SetGlobalFloat(ID_DimEnabled, 1f);
        Shader.SetGlobalFloat(ID_DimPlayerY, ResolveDimPlayerY());
        Shader.SetGlobalFloat(ID_DimRangeUp, p.dimRangeUp);
        Shader.SetGlobalFloat(ID_DimRangeDown, p.dimRangeDown);
        Shader.SetGlobalFloat(ID_DimFadeUp, p.dimFadeUp);
        Shader.SetGlobalFloat(ID_DimFadeDown, p.dimFadeDown);
        Shader.SetGlobalFloat(ID_DimSaturation, p.dimSaturation);
        Shader.SetGlobalFloat(ID_DimBrightness, p.dimBrightness);
        Shader.SetGlobalFloat(ID_DimAffectSky, p.dimAffectSky);
    }

    // 플레이어 y 기준선: override → 카메라 추적 타겟 → 태그 캐시 → 마지막 값 순.
    private float ResolveDimPlayerY()
    {
        // 1) 인스펙터 명시 override
        if (dimPlayerOverride != null)
        {
            _lastDimPlayerY = dimPlayerOverride.position.y;
            return _lastDimPlayerY;
        }

        // 2) 카메라가 현재 따라가는 대상(= 플레이어). '[' / ']' 전환에도 자동 추적.
        CameraTargetSwitcher cts = CameraTargetSwitcher.Active;
        if (cts != null && cts.CurrentFollowTarget != null)
        {
            _dimTargetCache = cts.CurrentFollowTarget;
            _lastDimPlayerY = _dimTargetCache.position.y;
            return _lastDimPlayerY;
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
            _lastDimPlayerY = _dimTargetCache.position.y;
            return _lastDimPlayerY;
        }

        // 4) fallback: 마지막 값 유지(씬 전환/스폰 전 깜빡임 방지)
        return _lastDimPlayerY;
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
