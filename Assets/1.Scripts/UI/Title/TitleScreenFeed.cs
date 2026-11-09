using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using UnityEngine.Video;

/// <summary>
/// 벽 모니터 13대. 기본은 **원래 벽 이미지 + CRT 룩 + TV 롤링**(팀장 09-28).
/// <see cref="_liveFeed"/> 를 켜면 예전처럼 **지금 카메라 화면을 다시 비춘다**(화면 속 화면 반복, 계획서 §0.5).
/// 메인 카메라 최종 색(후처리 뒤)을 저해상도 RT 2장에 번갈아 복사하고, 벽 모니터는 **직전 프레임** RT 를 본다 →
/// 한 프레임 지연의 재귀가 저절로 생긴다.
/// </summary>
/// <remarks>
/// 🔴 공유 <c>PC_Renderer</c> 에 Renderer Feature 를 추가하지 않는다 — 타이틀에 이 컴포넌트가 있을 때만
///    <c>beginCameraRendering</c> 에서 **지정한 메인 카메라**의 렌더러에 패스를 직접 넣는다(다른 씬·카메라 무영향).
/// 🔴 <c>AfterRenderingPostProcessing</c> — <c>AfterRendering</c> 은 백버퍼일 수 있다. 오버레이 UI(PRESS ANY KEY·Fade)는
///    구조상 안 들어간다(의도와 일치, Codex 검토 09-23).
/// 🔴 백화 방지: 벽 머티리얼은 밝기 계수를 1 미만으로(재귀마다 곱해진다).
/// 벽 모니터 대상은 **원본 머티리얼 참조**(glitch/non)로 고른다 — 중앙 화면은 이름이 같아서 이름으로 가르면 섞인다.
/// 존 투어 영상(할 일 254): 원래 이미지 모드에서 <see cref="_videoScreens"/> 만 영상 RT 를 비춘다 — 모니터마다 VideoPlayer 1개·
/// RT 1장·벽 머티리얼 인스턴스 1장(CRT·지직 그대로, 세로 흐름 0). 같은 순간 서로 다른 영상(<see cref="TitleWallVideoPlaylist"/>).
/// </remarks>
[DisallowMultipleComponent]
public sealed class TitleScreenFeed : MonoBehaviour
{
    [SerializeField] private Camera _targetCamera;
    [SerializeField] private Renderer[] _wallScreens;
    [SerializeField] private Material _wallMaterialSource;
    [SerializeField] private Vector2Int _size = new(640, 360);

    [System.Serializable]
    public struct WallImage
    {
        [Tooltip("벽 모니터의 원래 머티리얼(glitch / non). 이걸로 대상을 가른다.")]
        public Material original;
        [Tooltip("그 머티리얼 셰이더 그래프에 박혀 있던 원래 이미지.")]
        public Texture image;
    }

    [Tooltip("켜면 화면 반복(09-24 판). 끄면 원래 이미지(09-28 판).")]
    [SerializeField] private bool _liveFeed;
    [SerializeField] private WallImage[] _images;

    [Header("TV 흐름 · 지직 (원래 이미지 모드) — 머티리얼 쪽은 띠 세기·높이·지직 표현 세기")]
    [Tooltip("세로 흐름 속도 범위(초당 화면 수). 모니터마다 이 범위에서 뽑고, 방향도 위/아래 랜덤.")]
    [SerializeField] private Vector2 _rollSpeed = new(0.05f, 0.14f);
    [Tooltip("평소 지직 빈도(초당 횟수).")]
    [SerializeField, Min(0f)] private float _glitchRate = 0.3f;
    [Tooltip("동기 띠가 화면 가운데를 지나는 동안 지직 빈도 배율 — 흐름 → 지직.")]
    [SerializeField, Min(1f)] private float _seamGlitchBoost = 6f;
    [Tooltip("지직이 터질 때 흐름 속도에 더해지는 배율(잠깐 빨라졌다 돌아온다) — 지직 → 흐름.")]
    [SerializeField] private Vector2 _glitchKick = new(1.5f, 4f);
    [Tooltip("지직이 터질 때 그림이 한 번에 미끄러지는 양(화면 비율).")]
    [SerializeField, Range(0f, 0.2f)] private float _glitchSlip = 0.04f;
    [SerializeField, Min(0.01f)] private float _kickDecay = 0.35f;
    [SerializeField, Min(0.01f)] private float _glitchDecay = 0.12f;

    [Header("존 투어 영상 (할 일 254) — 원래 이미지 모드에서만. 같은 순간 모니터끼리 다른 영상")]
    [Tooltip("영상을 비출 모니터 화면 Renderer. 기본 = TitleOffice 의 monitor (4)·(8)·(11) 의 monitor_screen. _wallScreens 에 있든 없든 된다. 비우면 영상 끔.")]
    [SerializeField] private Renderer[] _videoScreens;
    [Tooltip("재생 목록. 끝나면 다른 모니터가 지금 틀지 않는 다음 영상으로 넘어간다(순환). 모니터 수보다 많아야 안 겹친다.")]
    [SerializeField] private VideoClip[] _videoClips;
    [Tooltip("영상 렌더 텍스처 크기(모니터마다 1장).")]
    [SerializeField] private Vector2Int _videoSize = new(640, 360);
    [Tooltip("영상 모니터 전용 재질(Title/CRTScreen, 예: MA_TitleWallVideo). 모니터마다 이것을 복제해 _MainTex 만 영상 RT 로 바꾼다 — " +
             "CRT 값·흐름(_Roll)·이음새(_SeamBlend)·지직 표현(_Flicker)·Tiling/Offset(늘어남 보정)은 전부 이 재질 값 그대로. " +
             "비면 벽 재질(_wallMaterialSource)을 복제하고 흐름 0·이음새 0.05 만 덮는다.")]
    [SerializeField] private Material _videoMaterial;

    private struct TvState { public float roll, speed, kick, glitch; }
    private TvState[] _tv;
    private MaterialPropertyBlock _mpb;
    private static readonly int IdWallRoll = Shader.PropertyToID("_WallRoll");
    private static readonly int IdWallGlitch = Shader.PropertyToID("_WallGlitch");

    private RenderTexture[] _rt = new RenderTexture[2];
    private RTHandle[] _handle = new RTHandle[2];
    private int _write;
    private Material _wallMaterial;
    private readonly List<Material[]> _originals = new();
    private FeedPass _pass;
    private readonly List<Material> _imageMaterials = new();

    private static readonly int IdRoll = Shader.PropertyToID("_Roll");
    private static readonly int IdSeamBlend = Shader.PropertyToID("_SeamBlend");

    private sealed class VideoScreen
    {
        public Renderer renderer;
        public Material[] original;
        public Material material;
        public RenderTexture rt;
        public VideoPlayer player;
        public int clip = -1;
        public float glitch;
    }

    private readonly List<VideoScreen> _videos = new();
    private readonly List<VideoClip> _clips = new();
    private readonly HashSet<Renderer> _videoSet = new();
    private readonly List<int> _playingScratch = new();

    private void OnEnable()
    {
        if (!_liveFeed)
        {
            EnableStillImages();
            return;
        }

        if (_targetCamera == null) _targetCamera = Camera.main;
        if (_wallMaterialSource == null || _wallScreens == null || _wallScreens.Length == 0)
        {
            Debug.LogWarning("[TitleFeed] 벽 모니터 / 머티리얼 참조가 비어 반복 화면을 끈다.", this);
            enabled = false;
            return;
        }

        for (int i = 0; i < 2; i++)
        {
            _rt[i] = new RenderTexture(_size.x, _size.y, 0, RenderTextureFormat.ARGB32)
            {
                name = $"TitleFeed{i}", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            _rt[i].Create();
            ClearBlack(_rt[i]);
            _handle[i] = RTHandles.Alloc(_rt[i]);
        }

        _wallMaterial = new Material(_wallMaterialSource) { name = "TitleWallFeed (Instance)" };
        _wallMaterial.mainTexture = _rt[1];

        _originals.Clear();
        foreach (Renderer r in _wallScreens)
        {
            if (r == null) { _originals.Add(null); continue; }
            _originals.Add(r.sharedMaterials);
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = _wallMaterial;
            r.sharedMaterials = mats;
        }

        if (TitleCrtFx.Active != null) TitleCrtFx.Active.Register(_wallMaterial);

        _pass = new FeedPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        RenderPipelineManager.endCameraRendering += OnEndCamera;
    }

    private void Start()
    {
        if (TitleCrtFx.Active == null) return;
        if (_wallMaterial != null) TitleCrtFx.Active.Register(_wallMaterial);
        foreach (Material m in _imageMaterials) TitleCrtFx.Active.Register(m);
        foreach (VideoScreen v in _videos) TitleCrtFx.Active.Register(v.material);
    }

    /// <summary>원래 머티리얼마다 인스턴스 1장(이미지만 다르다) — 13대가 2장을 공유한다. 흐름·지직은 셰이더가 위치 해시로 모니터별 위상을 준다.</summary>
    private void EnableStillImages()
    {
        if (_wallMaterialSource == null || _wallScreens == null || _images == null || _images.Length == 0)
        {
            Debug.LogWarning("[TitleFeed] 벽 모니터 / 이미지 참조가 비어 벽 연출을 끈다.", this);
            enabled = false;
            return;
        }

        var byOriginal = new Dictionary<Material, Material>();
        foreach (WallImage w in _images)
        {
            if (w.original == null || w.image == null || byOriginal.ContainsKey(w.original)) continue;
            var m = new Material(_wallMaterialSource) { name = $"TitleWallImage ({w.image.name})", mainTexture = w.image };
            byOriginal.Add(w.original, m);
            _imageMaterials.Add(m);
        }

        _originals.Clear();
        foreach (Renderer r in _wallScreens)
        {
            if (r == null) { _originals.Add(null); continue; }
            Material[] mats = r.sharedMaterials;
            _originals.Add((Material[])mats.Clone());
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && byOriginal.TryGetValue(mats[i], out Material inst)) mats[i] = inst;
            r.sharedMaterials = mats;
        }

        if (TitleCrtFx.Active != null) foreach (Material m in _imageMaterials) TitleCrtFx.Active.Register(m);

        _mpb ??= new MaterialPropertyBlock();
        _tv = new TvState[_wallScreens.Length];
        for (int i = 0; i < _tv.Length; i++)
        {
            float sign = Random.value < 0.5f ? -1f : 1f;
            _tv[i] = new TvState { roll = Random.value, speed = sign * Random.Range(_rollSpeed.x, _rollSpeed.y) };
        }

        EnableVideos();
    }

    /// <summary>
    /// 영상 모니터마다 RT·벽 머티리얼 인스턴스·VideoPlayer 를 만든다. 원래 이미지로 바꾼 <b>뒤</b>에 덮으므로
    /// 끌 때는 <see cref="DisableVideos"/> 를 먼저 불러 그 상태(원래 이미지 인스턴스)로 되돌린 다음 벽 정리를 한다.
    /// </summary>
    private void EnableVideos()
    {
        _clips.Clear();
        if (_videoClips != null) foreach (VideoClip c in _videoClips) if (c != null) _clips.Add(c);
        if (_videoScreens == null || _videoScreens.Length == 0 || _clips.Count == 0) return;

        int[] initial = TitleWallVideoPlaylist.Initial(_videoScreens.Length, _clips.Count, Random.Range(0, _clips.Count));
        for (int i = 0; i < _videoScreens.Length; i++)
        {
            Renderer r = _videoScreens[i];
            if (r == null || !_videoSet.Add(r)) continue;

            var rt = new RenderTexture(Mathf.Max(16, _videoSize.x), Mathf.Max(16, _videoSize.y), 0, RenderTextureFormat.ARGB32)
            {
                name = $"TitleWallVideo{i}", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            rt.Create();
            ClearBlack(rt);

            // 전용 재질이 있으면 값은 전부 그 재질 그대로(_MainTex 텍스처만 RT — Tiling/Offset 도 재질 값 유지).
            // 없으면 벽 재질 복제 + 세로 흐름(동기 띠·감기)만 끄고, 지직이 있을 때 섞이는 위아래 이음새 폭을 최소로.
            var m = new Material(_videoMaterial != null ? _videoMaterial : _wallMaterialSource)
            {
                name = $"TitleWallVideo{i} (Instance)", mainTexture = rt
            };
            if (_videoMaterial == null)
            {
                m.SetFloat(IdRoll, 0f);
                m.SetFloat(IdSeamBlend, 0.05f);
            }

            Material[] original = r.sharedMaterials;
            var mats = new Material[original.Length];
            for (int k = 0; k < mats.Length; k++) mats[k] = m;
            r.sharedMaterials = mats;

            var go = new GameObject($"TitleWallVideo{i}");
            go.transform.SetParent(transform, false);
            VideoPlayer vp = go.AddComponent<VideoPlayer>();
            vp.playOnAwake = false;
            vp.isLooping = false;
            vp.source = VideoSource.VideoClip;
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.targetTexture = rt;
            vp.aspectRatio = VideoAspectRatio.Stretch;
            vp.audioOutputMode = VideoAudioOutputMode.None;
            vp.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            // 켜면 영상 3개가 0.5초 근처 첫 프레임에서 멈춘다(Play 진단: 끈 모니터만 정상 재생, 원본 mp4 도 같음).
            vp.skipOnDrop = false;

            var v = new VideoScreen { renderer = r, original = original, material = m, rt = rt, player = vp };
            vp.loopPointReached += _ => OnVideoEnded(v);
            vp.errorReceived += (_, msg) => Debug.LogWarning($"[TitleFeed] 벽 영상 재생 오류({r.name}): {msg}", this);
            _videos.Add(v);

            if (TitleCrtFx.Active != null) TitleCrtFx.Active.Register(m);
            Play(v, initial[i]);
        }
    }

    private void Play(VideoScreen v, int clip)
    {
        if (clip < 0 || clip >= _clips.Count) return;
        v.clip = clip;
        v.player.clip = _clips[clip];
        v.player.Play();
    }

    private void OnVideoEnded(VideoScreen v)
    {
        if (!_videos.Contains(v)) return; // 정리 뒤 늦게 온 콜백
        _playingScratch.Clear();
        foreach (VideoScreen o in _videos) if (o != v) _playingScratch.Add(o.clip);
        Play(v, TitleWallVideoPlaylist.Next(v.clip, _playingScratch, _clips.Count));
    }

    /// <summary>영상 모니터는 흐름 0, 지직만 평소 빈도로(동기 띠가 없으니 띠 가속·미끄러짐도 없다).</summary>
    private void UpdateVideoGlitch(float dt, float glitchFade)
    {
        foreach (VideoScreen v in _videos)
        {
            if (v.renderer == null) continue;
            if (Random.value < _glitchRate * dt) v.glitch = Random.Range(0.55f, 1f);
            v.glitch *= glitchFade;

            v.renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(IdWallRoll, 0f);
            _mpb.SetFloat(IdWallGlitch, v.glitch);
            v.renderer.SetPropertyBlock(_mpb);
        }
    }

    private void DisableVideos()
    {
        foreach (VideoScreen v in _videos)
        {
            if (v.player != null)
            {
                v.player.Stop();
                v.player.targetTexture = null;
                Destroy(v.player.gameObject);
            }
            if (v.renderer != null)
            {
                v.renderer.SetPropertyBlock(null);
                v.renderer.sharedMaterials = v.original;
            }
            if (TitleCrtFx.Active != null) TitleCrtFx.Active.Unregister(v.material);
            if (v.material != null) Destroy(v.material);
            if (v.rt != null) { v.rt.Release(); Destroy(v.rt); }
        }
        _videos.Clear();
        _videoSet.Clear();
        _clips.Clear();
    }

    private void Update()
    {
        if (_tv == null) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f); // 히치 한 프레임에 크게 튀지 않게
        float kickFade = Mathf.Exp(-dt / _kickDecay);
        float glitchFade = Mathf.Exp(-dt / _glitchDecay);

        for (int i = 0; i < _tv.Length; i++)
        {
            Renderer r = _wallScreens[i];
            if (r == null || _videoSet.Contains(r)) continue; // 영상 모니터는 UpdateVideoGlitch
            ref TvState t = ref _tv[i];

            // 흐름 → 지직: 동기 띠(이음새)가 화면 가운데 근처면 더 자주 터진다
            float seam = Mathf.Repeat(1f - t.roll, 1f);
            float rate = _glitchRate * (Mathf.Abs(seam - 0.5f) < 0.12f ? _seamGlitchBoost : 1f);
            if (Random.value < rate * dt)
            {
                t.glitch = Random.Range(0.55f, 1f);
                // 지직 → 흐름: 미끄러지고 잠깐 빨라진다(같은 방향)
                t.kick = Random.Range(_glitchKick.x, _glitchKick.y);
                t.roll += Mathf.Sign(t.speed) * Random.Range(0.3f, 1f) * _glitchSlip;
            }

            t.roll = Mathf.Repeat(t.roll + t.speed * (1f + t.kick) * dt, 1f);
            t.kick *= kickFade;
            t.glitch *= glitchFade;

            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(IdWallRoll, t.roll);
            _mpb.SetFloat(IdWallGlitch, t.glitch);
            r.SetPropertyBlock(_mpb);
        }

        UpdateVideoGlitch(dt, glitchFade);
    }

    private void DisableStillImages()
    {
        DisableVideos(); // 영상이 원래 이미지 위에 덮였으므로 먼저 걷는다
        _tv = null;
        foreach (Renderer r in _wallScreens) if (r != null) r.SetPropertyBlock(null);
        for (int i = 0; i < _wallScreens.Length && i < _originals.Count; i++)
            if (_wallScreens[i] != null && _originals[i] != null) _wallScreens[i].sharedMaterials = _originals[i];
        _originals.Clear();
        foreach (Material m in _imageMaterials)
        {
            if (TitleCrtFx.Active != null) TitleCrtFx.Active.Unregister(m);
            Destroy(m);
        }
        _imageMaterials.Clear();
    }

    private void OnDisable()
    {
        if (_imageMaterials.Count > 0) { DisableStillImages(); return; }

        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
        if (_pass == null) return; // OnEnable 에서 참조 누락으로 꺼진 경우 — 정리할 것이 없다
        _pass = null;

        for (int i = 0; i < _wallScreens.Length && i < _originals.Count; i++)
            if (_wallScreens[i] != null && _originals[i] != null) _wallScreens[i].sharedMaterials = _originals[i];
        _originals.Clear();

        if (TitleCrtFx.Active != null && _wallMaterial != null) TitleCrtFx.Active.Unregister(_wallMaterial);
        if (_wallMaterial != null) Destroy(_wallMaterial);
        for (int i = 0; i < 2; i++)
        {
            _handle[i]?.Release();
            _handle[i] = null;
            if (_rt[i] != null) { _rt[i].Release(); Destroy(_rt[i]); _rt[i] = null; }
        }
    }

    private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam != _targetCamera || cam.cameraType != CameraType.Game || cam.targetTexture != null) return;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data == null || data.scriptableRenderer == null) return;

        // 벽은 읽기 RT(직전 프레임), 패스는 쓰기 RT — 같은 텍스처를 동시에 읽고 쓰지 않는다.
        _wallMaterial.mainTexture = _rt[1 - _write];
        _pass.Target = _handle[_write];
        data.scriptableRenderer.EnqueuePass(_pass);
    }

    private void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam != _targetCamera) return;
        _write = 1 - _write;
    }

    private static void ClearBlack(RenderTexture rt)
    {
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(false, true, Color.black);
        RenderTexture.active = prev;
    }

    private sealed class FeedPass : ScriptableRenderPass
    {
        public RTHandle Target;

        public FeedPass() => requiresIntermediateTexture = true;

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (Target == null) return;
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;

            TextureHandle source = resources.activeColorTexture;
            if (!source.IsValid()) return;

            TextureHandle dest = renderGraph.ImportTexture(Target);
            renderGraph.AddBlitPass(source, dest, Vector2.one, Vector2.zero, passName: "Title Screen Feed");
        }
    }
}
