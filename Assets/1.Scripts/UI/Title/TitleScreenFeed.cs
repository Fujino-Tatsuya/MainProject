using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

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
            if (r == null) continue;
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
    }

    private void DisableStillImages()
    {
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
