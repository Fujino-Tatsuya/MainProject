using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 벽 모니터 13대 = **지금 카메라 화면을 다시 비춘다**(화면 속 화면 반복, 계획서 §0.5).
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

    private RenderTexture[] _rt = new RenderTexture[2];
    private RTHandle[] _handle = new RTHandle[2];
    private int _write;
    private Material _wallMaterial;
    private readonly List<Material[]> _originals = new();
    private FeedPass _pass;

    private void OnEnable()
    {
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
        if (TitleCrtFx.Active != null && _wallMaterial != null) TitleCrtFx.Active.Register(_wallMaterial);
    }

    private void OnDisable()
    {
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
