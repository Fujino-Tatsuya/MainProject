// 투명화 그룹 툴 — 씬 뷰 오버레이.
// PLAN-transparent-group-tool.md 결정 14~18·21. S0 스파이크에서 검증된 기법을 그대로 쓴다.
//
// 기법: Camera.SubmitRenderRequest(ObjectIdRequest) 로 "픽셀마다 몇 번 오브젝트인가" 버퍼를 받고,
// 그걸 마스크 삼아 풀스크린 blit 한 번으로 색을 섞는다. 원본 Renderer 의 material 도 MPB 도
// 건드리지 않고, URP Renderer 에셋에 Feature 를 등록하지도 않는다.
//
// S0 에서 확인된 것:
//  - 에디트 모드에서 동작한다 (NetVis 의 플레이 모드 제약은 오너십 데이터 때문이지 기법의 한계가 아니다)
//  - Render Graph 가 켜져 있어도(m_EnableRenderCompatibilityMode: 0) CameraTarget blit 이 먹는다
//  - SubmitRenderRequest 는 endCameraRendering 을 다시 부르지 않는다 (재귀 없음)

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VeyTrace.Rendering.Occlusion.Editor
{
    [InitializeOnLoad]
    static class TransparentGroupVisualizer
    {
        const string k_ShaderName = "Hidden/Rendering/TransparentGroupOverlay";
        const string k_OutlineKeyword = "TRANSPARENT_GROUP_OUTLINE";

        static readonly int k_SceneTexId = Shader.PropertyToID("_SceneRenderTex");
        static readonly int k_ObjectIdTexId = Shader.PropertyToID("_ObjectIdTex");
        static readonly int k_ColorBufferId = Shader.PropertyToID("_ObjectIdToColorBuffer");
        static readonly int k_SaturationId = Shader.PropertyToID("_SceneSaturation");

        static bool s_Hooked;
        static Material s_Material;
        static readonly Dictionary<Camera, CameraResources> s_Resources = new Dictionary<Camera, CameraResources>();

        /// <summary>색 맵은 프레임마다 다시 만들 필요가 없다. 세션이 바뀔 때만 갱신한다.</summary>
        static Dictionary<int, Color> s_ColorMap = new Dictionary<int, Color>();

        sealed class CameraResources
        {
            public RenderTexture ObjectIdTex;
            public RenderTexture SceneTex;
            public ObjectIdRequest Request;
            public ComputeBuffer ColorBuffer;

            // CommandBufferPool 은 SRP Core 패키지에 있다. 그것 하나 때문에 asmdef 에
            // 패키지 의존을 늘리지 않고, 카메라마다 하나를 만들어 재사용한다.
            public readonly CommandBuffer Commands = new CommandBuffer { name = nameof(TransparentGroupVisualizer) };

            public void Dispose()
            {
                if (ObjectIdTex != null) { ObjectIdTex.Release(); ObjectIdTex = null; }
                if (SceneTex != null) { SceneTex.Release(); SceneTex = null; }
                if (ColorBuffer != null) { ColorBuffer.Release(); ColorBuffer = null; }
                Commands.Dispose();
                Request = null;
            }
        }

        static TransparentGroupVisualizer()
        {
            TransparentGroupSession.Changed += OnSessionChanged;
            EditorApplication.playModeStateChanged += _ => Sync();   // PLAN 결정 21
            AssemblyReloadEvents.beforeAssemblyReload += Teardown;
            EditorApplication.delayCall += Sync;
        }

        static void OnSessionChanged()
        {
            s_ColorMap = TransparentGroupSession.BuildColorMap();
            if (s_Material != null) s_Material.SetFloat(k_SaturationId, TransparentGroupSession.SceneSaturation);
            Sync();
        }

        /// <summary>켜야 하는 상태면 훅을 걸고, 아니면 걷는다.</summary>
        static void Sync()
        {
            var shouldRun = TransparentGroupSession.OverlayEnabled
                            && !EditorApplication.isPlayingOrWillChangePlaymode;

            if (shouldRun == s_Hooked) return;
            if (shouldRun) Setup(); else Teardown();
        }

        static void Setup()
        {
            var shader = Shader.Find(k_ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[TransparentGroup] 셰이더를 찾지 못했다: {k_ShaderName}. 오버레이를 켤 수 없다.");
                TransparentGroupSession.OverlayEnabled = false;
                return;
            }

            s_Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            s_Material.EnableKeyword(k_OutlineKeyword);
            s_Material.SetFloat(k_SaturationId, TransparentGroupSession.SceneSaturation);

            s_ColorMap = TransparentGroupSession.BuildColorMap();

            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            s_Hooked = true;
            SceneView.RepaintAll();
        }

        static void Teardown()
        {
            if (!s_Hooked) return;

            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            foreach (var res in s_Resources.Values) res.Dispose();
            s_Resources.Clear();
            if (s_Material != null) Object.DestroyImmediate(s_Material);
            s_Material = null;
            s_Hooked = false;
            SceneView.RepaintAll();
        }

        static bool IsSceneViewCamera(Camera camera)
        {
            // PLAN 결정 18 — 열린 씬 뷰 전부. (NetVis 는 sceneViews[0] 만 한다.)
            foreach (SceneView sv in SceneView.sceneViews)
            {
                if (sv != null && sv.camera == camera) return true;
            }
            return false;
        }

        static void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (s_Material == null) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!IsSceneViewCamera(camera)) return;
            if (camera.pixelWidth <= 0 || camera.pixelHeight <= 0) return;
            if (s_ColorMap.Count == 0) return;   // 칠할 것이 없으면 추가 렌더 패스를 돌리지 않는다.

            var res = GetOrCreateResources(camera);
            camera.SubmitRenderRequest(res.Request);

            var result = res.Request.result;
            if (result?.idToObjectMapping == null) return;

            UploadColorBuffer(res, result.idToObjectMapping);

            var cmd = res.Commands;
            cmd.Clear();
            cmd.Blit(BuiltinRenderTextureType.CameraTarget, res.SceneTex);
            s_Material.SetTexture(k_ObjectIdTexId, res.ObjectIdTex);
            s_Material.SetTexture(k_SceneTexId, res.SceneTex);
            cmd.Blit(res.SceneTex, BuiltinRenderTextureType.CameraTarget, s_Material);
            context.ExecuteCommandBuffer(cmd);
            context.Submit();
        }

        static CameraResources GetOrCreateResources(Camera camera)
        {
            if (!s_Resources.TryGetValue(camera, out var res))
            {
                res = new CameraResources();
                s_Resources[camera] = res;
            }

            res.ObjectIdTex = EnsureTexture(res.ObjectIdTex, camera, GraphicsFormat.D32_SFloat);
            res.SceneTex = EnsureTexture(res.SceneTex, camera, GraphicsFormat.None);

            if (res.Request == null) res.Request = new ObjectIdRequest(res.ObjectIdTex);
            // 씬 뷰 크기가 바뀌면 텍스처가 새로 만들어지므로 매번 다시 꽂는다.
            res.Request.destination = res.ObjectIdTex;
            return res;
        }

        static RenderTexture EnsureTexture(RenderTexture existing, Camera camera, GraphicsFormat depthFormat)
        {
            const GraphicsFormat colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
            if (existing != null &&
                existing.width == camera.pixelWidth &&
                existing.height == camera.pixelHeight &&
                existing.graphicsFormat == colorFormat &&
                existing.depthStencilFormat == depthFormat)
            {
                return existing;
            }

            if (existing != null) existing.Release();
            return new RenderTexture(new RenderTextureDescriptor(
                width: camera.pixelWidth,
                height: camera.pixelHeight,
                colorFormat: colorFormat,
                depthStencilFormat: depthFormat));
        }

        static void UploadColorBuffer(CameraResources res, Object[] mapping)
        {
            var colors = new Color[Mathf.Max(mapping.Length, 1)];
            for (var id = 0; id < mapping.Length; id++)
            {
                var obj = mapping[id];
                // 🔴 mapping 에 담기는 것은 Renderer 컴포넌트다. GameObject 가 아니다(S0).
                colors[id] = obj != null && s_ColorMap.TryGetValue(obj.GetInstanceID(), out var c)
                    ? c
                    : Color.clear;   // a == 0 이면 그룹 아님
            }

            if (res.ColorBuffer == null || res.ColorBuffer.count != colors.Length)
            {
                if (res.ColorBuffer != null) res.ColorBuffer.Release();
                res.ColorBuffer = new ComputeBuffer(colors.Length, sizeof(float) * 4);
            }

            res.ColorBuffer.SetData(colors);
            s_Material.SetBuffer(k_ColorBufferId, res.ColorBuffer);
        }
    }
}
