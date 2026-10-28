using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>적중 섬광 오너 화면 칸(할 일 132 §15) — 디렉터가 구현하고, 보일 때마다 읽는다.</summary>
public interface IInterruptHitFlashScreenSettings
{
    /// <summary>오버레이 캔버스 재질(<c>BeaverLobby/UI/InterruptHitFlashOverlay</c>) — 색·덮어쓸 텍스처 칸은 재질에. 비면 그림 없음.</summary>
    Material HitFlashOverlayMaterial { get; }
    /// <summary>② 렌즈 플레어 모양(URP Lens Flare (SRP) 데이터). 비면 플레어 없음.</summary>
    LensFlareDataSRP HitFlashLensFlare { get; }
    /// <summary>② 플레어 세기(LensFlareComponentSRP.intensity, 단계 곡선 1 일 때).</summary>
    float HitFlashFlareIntensity { get; }
    /// <summary>② 플레어 크기(LensFlareComponentSRP.scale).</summary>
    float HitFlashFlareScale { get; }
    /// <summary>방사형 블러 최대 세기 [0, 1](단계 곡선 1 일 때 — ③).</summary>
    float HitFlashBlurStrength { get; }
    /// <summary>⑤⑥ 색수차 Volume 프로필(에셋은 바꾸지 않는다 — 가중치만). 비면 색수차 없음.</summary>
    VolumeProfile HitFlashAberrationProfile { get; }
    /// <summary>⑤⑥ 블룸 Volume 프로필. 비면 블룸 상승 없음.</summary>
    VolumeProfile HitFlashBloomProfile { get; }
}

/// <summary>
/// 적중 섬광 오너 화면 출력(할 일 132 §15) — <see cref="IInterruptHitFlashOverlayOutput"/> 실제 출력. 오너 화면에서만 불린다.
/// <list type="bullet">
/// <item>오버레이 캔버스(②③④⑥ 그림): 오너 암전 덮개(<see cref="InterruptOwnerHudCover"/>)와 같은 방식 — 최상위 Screen Space Overlay 캔버스 하나에 화면 전체 RawImage.
///       월드·HUD·암전 덮개 전부 위(<see cref="SortingOrder"/> = PerformanceOverlay 바로 아래, 암전 덮개 바로 위). 재질이 단계·진행도·중심으로 절차 생성,
///       단계별 '덮어쓸 텍스처' 칸이 차면 그 단계는 그 텍스처. ② = 플레어 층 RT + 푸른 고리를 더함, ③ = 회청색 화이트아웃, ④ = 암전 덮개 RT 의 남김 영역을 흰 덩어리로 번짐, ⑥ = 무지개 고리.</item>
/// <item>렌즈 플레어(②): 적중 지점의 LensFlareComponentSRP 하나. 렌더 기능(<see cref="DarkAdaptationFeature"/>)이 플레어 층 RT 에만 따로 그리고 캔버스가 암전 위에 더한다.</item>
/// <item>방사형 블러(③⑤⑥): <see cref="DarkAdaptationState.HitFlashBlurStrength"/> — 렌더 기능이 후처리 전 카메라 컬러에 건다(HUD 는 블러 밖).</item>
/// <item>색수차·블룸(⑤⑥): 이 출력 소유의 전역 Volume 둘(우선순위 <see cref="VolumePriority"/>) 가중치만 움직인다. 첫 프레임에 가중치를 스냅샷하고
///       숨김(끝·강제 리셋)에 그 값으로 되돌린다. 기존 전역 Volume·프로필 에셋은 건드리지 않는다.</item>
/// <item>캔버스·재질 사본·플레어 오브젝트·RT·Volume 은 첫 사용 때 한 번 만들어 재사용한다(RT 는 화면 크기가 바뀔 때만 다시).</item>
/// </list>
/// </summary>
public sealed class InterruptHitFlashOverlay : IInterruptHitFlashOverlayOutput, IDisposable
{
    public const int SortingOrder = short.MaxValue - 1;
    /// <summary>섬광 Volume 우선순위 — 씬 전역 Volume(기본 0 대) 위라 가중치만큼 그 값을 덮는다.</summary>
    public const float VolumePriority = 1000f;

    private static readonly int StageId = Shader.PropertyToID("_Stage");
    private static readonly int ProgressId = Shader.PropertyToID("_Progress");
    private static readonly int CenterId = Shader.PropertyToID("_Center");
    private static readonly int AspectId = Shader.PropertyToID("_Aspect");
    private static readonly int FlareTexId = Shader.PropertyToID("_FlareTex");
    private static readonly int CoverTexId = Shader.PropertyToID("_CoverTex");

    private readonly IInterruptHitFlashScreenSettings _settings;
    private readonly InterruptHitFlashVolumeSnapshot _snapshot = new InterruptHitFlashVolumeSnapshot();

    private GameObject _root;
    private Canvas _canvas;
    private RawImage _image;
    private Material _instance;
    private LensFlareComponentSRP _flare;
    private RenderTexture _flareTarget;
    private Volume _aberrationVolume;
    private Volume _bloomVolume;

    public InterruptHitFlashOverlay(IInterruptHitFlashScreenSettings settings)
    {
        _settings = settings;
    }

    /// <summary>지금 색수차·블룸 Volume 가중치(꺼짐 = 0) — 확인용.</summary>
    public float AberrationWeight => WeightOf(_aberrationVolume);
    public float BloomWeight => WeightOf(_bloomVolume);
    /// <summary>② 플레어가 켜져 있다 — 확인용.</summary>
    public bool FlareActive => _flare != null && _flare.enabled;

    public void Show(InterruptHitFlashStage stage, float progress, Vector3 worldCenter)
    {
        Camera camera = Camera.main;
        Vector2 center = camera != null
            ? InterruptHitFlashRules.ViewportCenter(camera.WorldToViewportPoint(worldCenter))
            : new Vector2(0.5f, 0.5f);
        InterruptHitFlashLook look = InterruptHitFlashRules.Look(stage, progress);

        EnsureRoot();
        ApplyFlare(look.Flare, worldCenter);
        DarkAdaptationState.HitFlashBlurCenter = center;
        DarkAdaptationState.HitFlashBlurStrength = look.Blur * Mathf.Clamp01(_settings != null ? _settings.HitFlashBlurStrength : 0f);
        ApplyVolumes(look);

        if (!EnsureCanvas())
            return;

        _instance.SetFloat(StageId, (float)stage);
        _instance.SetFloat(ProgressId, Mathf.Clamp01(progress));
        _instance.SetVector(CenterId, center);
        _instance.SetFloat(AspectId, Screen.height > 0 ? (float)Screen.width / Screen.height : 1f);
        // 덮개 RT(흰색 + 알파 1 − 남김) — 없으면 흰 텍스처(= 전부 덮음, 남김 없음).
        RenderTexture cover = DarkAdaptationState.MaskCoverTarget;
        _instance.SetTexture(CoverTexId, cover != null ? cover : Texture2D.whiteTexture);
        _instance.SetTexture(FlareTexId, DarkAdaptationState.HitFlashFlareTarget != null ? _flareTarget : Texture2D.blackTexture);
        _canvas.enabled = true;
    }

    public void Hide()
    {
        if (_canvas != null)
            _canvas.enabled = false;

        ApplyFlare(0f, Vector3.zero);
        DarkAdaptationState.HitFlashBlurStrength = 0f;
        RestoreVolumes();
    }

    public void Dispose()
    {
        Hide();
        DestroyObject(_root);
        DestroyObject(_instance);
        if (_flareTarget != null)
            _flareTarget.Release();
        DestroyObject(_flareTarget);

        _root = null;
        _canvas = null;
        _image = null;
        _instance = null;
        _flare = null;
        _flareTarget = null;
        _aberrationVolume = null;
        _bloomVolume = null;
    }

    // EditMode 테스트에서도 정리되게(Destroy 는 Play 전용).
    private static void DestroyObject(UnityEngine.Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            UnityEngine.Object.Destroy(target);
        else
            UnityEngine.Object.DestroyImmediate(target);
    }

    private void ApplyFlare(float amount, Vector3 worldCenter)
    {
        LensFlareDataSRP data = _settings?.HitFlashLensFlare;
        if (amount <= 0f || data == null || _root == null)
        {
            if (_flare != null)
                _flare.enabled = false;
            DarkAdaptationState.HitFlashFlareTarget = null;
            return;
        }

        if (_flare == null)
        {
            var flareObject = new GameObject("Flare");
            flareObject.transform.SetParent(_root.transform, false);
            flareObject.SetActive(false);
            _flare = flareObject.AddComponent<LensFlareComponentSRP>();
            // 탑다운 카메라 거리와 무관하게 같은 세기·크기 — 거리·화면 가장자리 감쇠 끔, 가림 판정 없음.
            _flare.useOcclusion = false;
            _flare.allowOffScreen = true;
            _flare.attenuationByLightShape = false;
            _flare.distanceAttenuationCurve = AnimationCurve.Constant(0f, 1f, 1f);
            _flare.scaleByDistanceCurve = AnimationCurve.Constant(0f, 1f, 1f);
            _flare.radialScreenAttenuationCurve = AnimationCurve.Constant(0f, 1f, 1f);
            _flare.enabled = false;
            flareObject.SetActive(true);
        }

        _flare.transform.position = worldCenter;
        _flare.lensFlareData = data;
        _flare.intensity = amount * Mathf.Max(0f, _settings.HitFlashFlareIntensity);
        _flare.scale = Mathf.Max(0f, _settings.HitFlashFlareScale);
        _flare.enabled = true;

        EnsureFlareTarget();
        DarkAdaptationState.HitFlashFlareTarget = _flareTarget;
    }

    private void ApplyVolumes(InterruptHitFlashLook look)
    {
        _aberrationVolume = EnsureVolume(_aberrationVolume, "Aberration", _settings?.HitFlashAberrationProfile);
        _bloomVolume = EnsureVolume(_bloomVolume, "Bloom", _settings?.HitFlashBloomProfile);
        _snapshot.Capture(WeightOf(_aberrationVolume), WeightOf(_bloomVolume));
        SetWeight(_aberrationVolume, look.Aberration);
        SetWeight(_bloomVolume, look.Bloom);
    }

    private void RestoreVolumes()
    {
        if (!_snapshot.Release(out float aberration, out float bloom))
            return;

        SetWeight(_aberrationVolume, aberration);
        SetWeight(_bloomVolume, bloom);
    }

    private static float WeightOf(Volume volume) => volume != null && volume.enabled ? volume.weight : 0f;

    // 가중치 0 이면 꺼 둔다(Volume 섞기 비용 없음).
    private static void SetWeight(Volume volume, float weight)
    {
        if (volume == null)
            return;

        volume.weight = Mathf.Clamp01(weight);
        volume.enabled = volume.weight > 0f;
    }

    private Volume EnsureVolume(Volume volume, string name, VolumeProfile profile)
    {
        if (volume == null)
        {
            if (profile == null || _root == null)
                return null;

            var volumeObject = new GameObject(name);
            volumeObject.transform.SetParent(_root.transform, false);
            volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = VolumePriority;
            volume.weight = 0f;
            volume.enabled = false;
        }

        // sharedProfile — 에셋을 복제·수정하지 않는다(가중치만).
        volume.sharedProfile = profile;
        return volume;
    }

    private void EnsureRoot()
    {
        if (_root != null)
            return;

        // Volume 이 카메라 Volume 마스크(Default)에 들도록 기본 레이어.
        _root = new GameObject("[InterruptHitFlashOverlay]") { layer = 0 };
        if (Application.isPlaying)
            UnityEngine.Object.DontDestroyOnLoad(_root);
    }

    private void EnsureFlareTarget()
    {
        int width = Mathf.Max(1, Screen.width);
        int height = Mathf.Max(1, Screen.height);
        if (_flareTarget != null && _flareTarget.width == width && _flareTarget.height == height)
            return;

        if (_flareTarget != null)
        {
            _flareTarget.Release();
            DestroyObject(_flareTarget);
        }

        // 가산 플레어(HDR 세기) — 반정밀 RGBA.
        _flareTarget = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        {
            name = "_InterruptHitFlashFlare",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        _flareTarget.Create();
    }

    private bool EnsureCanvas()
    {
        if (_canvas != null)
            return true;

        Material source = _settings?.HitFlashOverlayMaterial;
        if (source == null || _root == null)
            return false;

        _instance = new Material(source) { name = source.name + " (Runtime)" };
        InterruptHitFlashRules.ApplyOverlayKeywords(_instance);

        var canvasObject = new GameObject("Canvas");
        canvasObject.transform.SetParent(_root.transform, false);
        _canvas = canvasObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = SortingOrder;

        var imageObject = new GameObject("Flash", typeof(RectTransform));
        imageObject.transform.SetParent(canvasObject.transform, false);
        var rect = (RectTransform)imageObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        _image = imageObject.AddComponent<RawImage>();
        _image.raycastTarget = false;
        _image.material = _instance;
        _canvas.enabled = false;
        return true;
    }
}
