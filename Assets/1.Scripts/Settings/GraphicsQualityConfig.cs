using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 그래픽 품질 — 프리셋(하/중/상)·렌더 스케일·그림자·안티앨리어싱·후처리·시야 디밍(비네트) 크기/경계
/// (PLAN-settings 그래픽 확장, 팀장 10-12). 저장은 PlayerPrefs. 해상도·창 모드·프레임은 <see cref="DisplayConfig"/>.
///
/// 적용 방식:
/// - 렌더 스케일·그림자 = 현재 URP 에셋 속성 · 안티앨리어싱 = 카메라 FXAA/SMAA(MSAA 안 씀 — CameraMode 주석). 🔴 에디터에서는 Play 종료 때 원래 값으로 되돌린다
///   (에셋에 직접 쓰면 PC_RPAsset 파일이 바뀐 채 커밋될 수 있다).
/// - 후처리 끄기 = **블룸·색수차만** 0 으로 덮는 전역 볼륨(우선순위 높음, 런타임 생성). 톤매핑·색 보정은 남긴다(색이 변하지 않게).
/// - 시야 디밍 = <see cref="FogManager"/> 가 셰이더에 넘기는 반경·페이드에 배율을 곱한다.
/// 프리셋 값 계산(<see cref="PresetValues"/>)은 순수 함수라 EditMode 로 검증한다.
/// </summary>
public static class GraphicsQualityConfig
{
    const string Prefix = "Settings.Graphics.";
    public const float MinRenderScale = 0.5f;
    public const float MinVignetteSize = 0.7f, MaxVignetteSize = 1.5f;
    public const float MinVignetteFade = 0.5f, MaxVignetteFade = 2f;

    static bool _loaded;
    static GraphicsQuality _current;

    public static GraphicsQuality Current
    {
        get { EnsureLoaded(); return _current; }
    }

    // ─── 순수 규칙 ────────────────────────────────────────────────────

    /// <summary>프리셋이 정하는 값. 상 = 지금(10-12) 화면 그대로(팀장 결정). 비네트는 프리셋과 무관(취향)이라 건드리지 않는다.</summary>
    public static GraphicsQuality PresetValues(GraphicsPreset preset, GraphicsQuality keepVignette)
    {
        var q = keepVignette;
        q.preset = preset;
        switch (preset)
        {
            case GraphicsPreset.Low:
                q.renderScale = 0.7f; q.shadows = ShadowSetting.Low; q.antiAliasing = AntiAliasingSetting.Off; q.postEffects = false; break;
            case GraphicsPreset.Medium:
                q.renderScale = 0.85f; q.shadows = ShadowSetting.Medium; q.antiAliasing = AntiAliasingSetting.Fxaa; q.postEffects = true; break;
            default:
                q.renderScale = 1f; q.shadows = ShadowSetting.High; q.antiAliasing = AntiAliasingSetting.Smaa; q.postEffects = true; break;
        }
        return q;
    }

    public static GraphicsQuality Defaults()
    {
        var q = new GraphicsQuality { vignetteSize = 1f, vignetteFade = 1f };
        return PresetValues(GraphicsPreset.High, q);
    }

    /// <summary>항목 값이 어떤 프리셋과 정확히 같으면 그 프리셋, 아니면 사용자 지정.</summary>
    public static GraphicsPreset Classify(GraphicsQuality q)
    {
        foreach (GraphicsPreset p in new[] { GraphicsPreset.Low, GraphicsPreset.Medium, GraphicsPreset.High })
        {
            GraphicsQuality r = PresetValues(p, q);
            if (Mathf.Approximately(r.renderScale, q.renderScale) && r.shadows == q.shadows &&
                r.antiAliasing == q.antiAliasing && r.postEffects == q.postEffects)
                return p;
        }
        return GraphicsPreset.Custom;
    }

    public static GraphicsQuality Sanitize(GraphicsQuality q)
    {
        q.renderScale = Mathf.Clamp(q.renderScale, MinRenderScale, 1f);
        q.vignetteSize = Mathf.Clamp(q.vignetteSize, MinVignetteSize, MaxVignetteSize);
        q.vignetteFade = Mathf.Clamp(q.vignetteFade, MinVignetteFade, MaxVignetteFade);
        if (!System.Enum.IsDefined(typeof(ShadowSetting), q.shadows)) q.shadows = ShadowSetting.High;
        if (!System.Enum.IsDefined(typeof(AntiAliasingSetting), q.antiAliasing)) q.antiAliasing = AntiAliasingSetting.Smaa;
        q.preset = Classify(q);
        return q;
    }

    // 🔴 MSAA 는 쓰지 않는다(10-13). 안개 렌더 피처(FogRendererFeature)가 결과를 MSAA 1 컬러로 갈아 끼우는데 깊이는
    //    MSAA 4 로 남아, 뒤에 그리는 반투명(물)이 깊이 판정 없이 떠 보였다(팀장 빌드). → 카메라 후처리 AA(FXAA/SMAA)로 교체.
    //    맵 카메라(MainCamera.prefab)는 원래 SMAA 였다 — 그래서 상 프리셋 = SMAA(지금 화면 그대로).
    const string AntiAliasingKey = "AntiAliasing";   // 옛 키 "AntiAliasingSetting"(MSAA 값)은 읽지 않는다

    public static AntialiasingMode CameraMode(AntiAliasingSetting aa) => aa switch
    {
        AntiAliasingSetting.Fxaa => AntialiasingMode.FastApproximateAntialiasing,
        AntiAliasingSetting.Smaa => AntialiasingMode.SubpixelMorphologicalAntiAliasing,
        _ => AntialiasingMode.None,
    };

    // 렌더 직전 카메라마다 AA 를 맞춘다 — 씬마다·런타임에 생기는 카메라를 따로 찾지 않아도 된다.
    // 게임 카메라(Base)만. RT 로 그리는 카메라(타이틀 모니터 UI)는 건드리지 않는다. 타이틀(글씨를 3D 로 그림) 동안은 끔.
    static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera.cameraType != CameraType.Game || camera.targetTexture != null)
            return;
        if (!camera.TryGetComponent(out UniversalAdditionalCameraData data) || data.renderType != CameraRenderType.Base)
            return;
        AntialiasingMode want = _forceFullRenderScale ? AntialiasingMode.None : CameraMode(Current.antiAliasing);
        if (data.antialiasing != want)
            data.antialiasing = want;
        if (want == AntialiasingMode.SubpixelMorphologicalAntiAliasing && data.antialiasingQuality != AntialiasingQuality.High)
            data.antialiasingQuality = AntialiasingQuality.High;
    }

    // ─── 저장·적용 ────────────────────────────────────────────────────

    public static void Set(GraphicsQuality q)
    {
        EnsureLoaded();
        _current = Sanitize(q);
        PlayerPrefs.SetFloat(Prefix + "RenderScale", _current.renderScale);
        PlayerPrefs.SetInt(Prefix + "Shadows", (int)_current.shadows);
        PlayerPrefs.SetInt(Prefix + AntiAliasingKey, (int)_current.antiAliasing);
        PlayerPrefs.SetInt(Prefix + "PostEffects", _current.postEffects ? 1 : 0);
        PlayerPrefs.SetFloat(Prefix + "VignetteSize", _current.vignetteSize);
        PlayerPrefs.SetFloat(Prefix + "VignetteFade", _current.vignetteFade);
        PlayerPrefs.Save();
        Apply(_current);
    }

    /// <summary>시야 디밍 반경 배율(<see cref="FogManager"/> 가 매 프레임 읽는다).</summary>
    public static float VignetteSizeScale => Current.vignetteSize;
    /// <summary>시야 디밍 경계 페이드 배율.</summary>
    public static float VignetteFadeScale => Current.vignetteFade;

    // 🔴 타이틀 메뉴·설정창은 3D 모니터 표면에 찍힌 글씨라 렌더 스케일을 그대로 맞는다 — 50% 면 작은 글씨가 깨졌다
    //    (팀장 10-13: "렌더 스케일·품질 프리셋은 글씨 빼고"). 인게임 UI 는 전부 오버레이 캔버스라 영향이 없어
    //    글씨를 3D 로 그리는 곳은 타이틀뿐이다 → 타이틀에 있는 동안만 100% 로 그린다. 저장값·설정창 표시는 그대로.
    static bool _forceFullRenderScale;

    /// <summary>켜져 있는 동안 저장값과 무관하게 렌더 스케일 100%. <see cref="TitleMonitorDisplay"/> 가 켜고 끈다.</summary>
    public static void SetForceFullRenderScale(bool on)
    {
        if (_forceFullRenderScale == on) return;
        _forceFullRenderScale = on;
        Apply(Current);
    }

    static void Apply(GraphicsQuality q)
    {
        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null)
        {
            Snapshot(urp);
            urp.renderScale = _forceFullRenderScale ? 1f : q.renderScale;
            urp.msaaSampleCount = _origMsaa;   // MSAA 는 에셋 원래 값(끔) 그대로 — AA 는 카메라에서(OnBeginCamera)
            // 🔴 supportsMainLightShadows·supportsSoftShadows 는 internal set(읽기 전용)이다 — 쓰면 컴파일 오류.
            //    끄기 = 그림자 거리 0(URP 는 maxShadowDistance > 0 일 때만 그림자를 그린다, UniversalRenderPipeline.cs).
            //    단계 차이는 거리·해상도로만 둔다(부드러운 그림자는 저작값 그대로).
            switch (q.shadows)
            {
                case ShadowSetting.Off:
                    urp.shadowDistance = 0f; break;
                case ShadowSetting.Low:
                    urp.shadowDistance = 12f; urp.mainLightShadowmapResolution = 512; break;
                case ShadowSetting.Medium:
                    urp.shadowDistance = 20f; urp.mainLightShadowmapResolution = 1024; break;
                default:   // High = 원래 저작값(28m · 2048)
                    urp.shadowDistance = _origShadowDistance; urp.mainLightShadowmapResolution = _origShadowRes; break;
            }
        }
        PostEffectsOverride.SetDisabled(!q.postEffects);
    }

    static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        GraphicsQuality d = Defaults();
        _current = Sanitize(new GraphicsQuality
        {
            renderScale = PlayerPrefs.GetFloat(Prefix + "RenderScale", d.renderScale),
            shadows = (ShadowSetting)PlayerPrefs.GetInt(Prefix + "Shadows", (int)d.shadows),
            antiAliasing = (AntiAliasingSetting)PlayerPrefs.GetInt(Prefix + AntiAliasingKey, (int)d.antiAliasing),
            postEffects = PlayerPrefs.GetInt(Prefix + "PostEffects", d.postEffects ? 1 : 0) != 0,
            vignetteSize = PlayerPrefs.GetFloat(Prefix + "VignetteSize", 1f),
            vignetteFade = PlayerPrefs.GetFloat(Prefix + "VignetteFade", 1f),
        });
    }

    // ─── 원래 값 보관(상 프리셋 = 저작값, 에디터 복원) ───────────────────
    static bool _snapshotted;
    static UniversalRenderPipelineAsset _snapAsset;
    static float _origRenderScale, _origShadowDistance;
    static int _origMsaa, _origShadowRes;

    static void Snapshot(UniversalRenderPipelineAsset urp)
    {
        if (_snapshotted && _snapAsset == urp) return;
        _snapshotted = true;
        _snapAsset = urp;
        _origRenderScale = urp.renderScale;
        _origMsaa = urp.msaaSampleCount;
        _origShadowDistance = urp.shadowDistance;
        _origShadowRes = urp.mainLightShadowmapResolution;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged -= RestoreOnExit;
        UnityEditor.EditorApplication.playModeStateChanged += RestoreOnExit;
#endif
    }

#if UNITY_EDITOR
    // 🔴 에디터 Play 에서 바꾼 URP 에셋 값을 되돌린다 — 안 하면 PC_RPAsset 이 바뀐 채 남아 커밋될 수 있다.
    static void RestoreOnExit(UnityEditor.PlayModeStateChange change)
    {
        if (change != UnityEditor.PlayModeStateChange.ExitingPlayMode || _snapAsset == null) return;
        _snapAsset.renderScale = _origRenderScale;
        _snapAsset.msaaSampleCount = _origMsaa;
        _snapAsset.shadowDistance = _origShadowDistance;
        _snapAsset.mainLightShadowmapResolution = _origShadowRes;
        _snapshotted = false;
        UnityEditor.EditorApplication.playModeStateChanged -= RestoreOnExit;
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplyAtBoot()
    {
        Apply(Current);
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache() { _loaded = false; _snapshotted = false; _forceFullRenderScale = false; }
}

/// <summary>그래픽 설정 한 벌.</summary>
public struct GraphicsQuality
{
    public GraphicsPreset preset;
    public float renderScale;          // 0.5~1
    public ShadowSetting shadows;
    public AntiAliasingSetting antiAliasing;
    public bool postEffects;           // 블룸·색수차
    public float vignetteSize;         // 시야 디밍 반경 배율 0.7~1.5
    public float vignetteFade;         // 시야 디밍 경계 배율 0.5~2
}

/// <summary>PlayerPrefs·드롭다운 순서로 쓰인다 — 값을 바꾸지 말 것.</summary>
public enum GraphicsPreset { Low = 0, Medium = 1, High = 2, Custom = 3 }
public enum ShadowSetting { Off = 0, Low = 1, Medium = 2, High = 3 }
public enum AntiAliasingSetting { Off = 0, Fxaa = 1, Smaa = 2 }   // 10-13: MSAA(X2·X4) → 후처리 AA 로 교체. 저장 키도 새로(AntiAliasingKey)

/// <summary>
/// 후처리 끄기 = 블룸·색수차를 0 으로 덮는 전역 볼륨. 첫 사용 때 DDOL 로 하나 만든다(씬 배치 없음).
/// 톤매핑·색 보정은 그대로 두어 화면 색이 바뀌지 않게 한다.
/// </summary>
public static class PostEffectsOverride
{
    static Volume _volume;

    public static void SetDisabled(bool disabled)
    {
        if (_volume == null)
        {
            if (!disabled) return;   // 켜진 상태면 만들 필요도 없다
            var go = new GameObject("SettingsPostEffectsOverride") { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(go);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 1000f;   // 씬 볼륨보다 위
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Bloom bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0f);
            ChromaticAberration ca = profile.Add<ChromaticAberration>(true);
            ca.intensity.Override(0f);
            _volume.sharedProfile = profile;
        }
        _volume.enabled = disabled;
    }
}
