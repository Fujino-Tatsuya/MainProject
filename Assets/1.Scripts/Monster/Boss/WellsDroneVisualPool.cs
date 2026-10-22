using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

/// <summary>
/// 피어 로컬 웰즈 드론 표현 풀. 드론은 NetworkObject가 아니며 판정·권한을 갖지 않는다.
/// 현재 전투에는 23호 한 마리와 WellsDroneAttack 하나만 있고 새 Lock 전에 Cancel 하므로
/// 비활성 보관 상한은 실제 동시 수요와 같은 1기다.
/// </summary>
static class WellsDroneVisualPool
{
    const int Capacity = 1;
    const string CatalogPath = "Monster/WellsDronePrewarmCatalog";
    const string RootName = "WellsDroneVisualPool";

    static readonly List<WellsDroneVisualInstance> Inactive = new List<WellsDroneVisualInstance>(Capacity);
    static readonly HashSet<WellsDroneVisualInstance> Active = new HashSet<WellsDroneVisualInstance>();
    static Transform _root;
    static WellsDronePrewarmCatalog _catalog;

    internal static int CreatedCount { get; private set; }
    internal static int GraphCreateCount { get; private set; }
    internal static int GraphDestroyCount { get; private set; }
    internal static int VisualCount => Inactive.Count + Active.Count;
    internal static int InactiveCount => Inactive.Count;
    internal static int ActiveCount => Active.Count;
    internal static int LiveGraphCount => GraphCreateCount - GraphDestroyCount;

    internal static WellsDroneDataSO PrewarmData
    {
        get
        {
            if (_catalog == null) _catalog = Resources.Load<WellsDronePrewarmCatalog>(CatalogPath);
            return _catalog != null ? _catalog.data : null;
        }
    }

    internal static WellsDroneVisualInstance Rent(WellsDroneDataSO data, Vector3 position, Quaternion rotation)
    {
        if (data == null || data.droneModel == null) return null;
        EnsureRoot();

        WellsDroneVisualInstance visual = null;
        for (int i = Inactive.Count - 1; i >= 0; i--)
        {
            WellsDroneVisualInstance candidate = Inactive[i];
            if (candidate == null)
            {
                Inactive.RemoveAt(i);
                continue;
            }
            if (candidate.SourcePrefab != data.droneModel) continue;
            visual = candidate;
            Inactive.RemoveAt(i);
            break;
        }

        if (visual == null) visual = Create(data.droneModel);
        Active.Add(visual);
        visual.PrepareForRent(position, rotation, data.droneScale, data.dashStartClip, data.dashLoopClip);
        return visual;
    }

    internal static void Return(WellsDroneVisualInstance visual)
    {
        if (visual == null) return;
        Active.Remove(visual);
        visual.PrepareForReturn();

        EnsureRoot();
        visual.transform.SetParent(_root, false);
        if (Inactive.Count < Capacity)
        {
            Inactive.Add(visual);
            return;
        }

        visual.DisposeGraph();
        DestroyObject(visual.gameObject);
    }

    /// <summary>
    /// 첫 실제 그리기에서 lazy mesh/material/texture 역직렬화가 생기지 않도록 전용 RT에 한 번 그린다.
    /// 게임 카메라를 쓰지 않으므로 로딩 씬에 카메라가 없어도 같은 경로를 탄다.
    /// </summary>
    internal static void WarmRenderer(WellsDroneVisualInstance visual)
    {
        if (visual == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;

        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        var materials = new HashSet<Material>();
        foreach (Renderer renderer in renderers)
        {
            if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null) _ = skinned.sharedMesh.bounds;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) _ = filter.sharedMesh.bounds;
            foreach (Material material in renderer.sharedMaterials)
                if (material != null) materials.Add(material);
        }

        foreach (Material material in materials)
        {
            Shader shader = material.shader;
            if (shader == null) continue;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                Texture texture = material.GetTexture(shader.GetPropertyNameId(i));
                if (texture != null) _ = texture.width;
            }
        }

        bool wasActive = visual.gameObject.activeSelf;
        visual.gameObject.SetActive(true);
        RenderTexture target = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var commands = new CommandBuffer { name = "WellsDronePrewarm" };
        try
        {
            commands.SetRenderTarget(target);
            commands.ClearRenderTarget(true, true, Color.clear);
            commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            foreach (Renderer renderer in renderers)
            {
                Material[] shared = renderer.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                    if (shared[i] != null) commands.DrawRenderer(renderer, shared[i], i);
            }
            Graphics.ExecuteCommandBuffer(commands);
            GL.Flush();
        }
        finally
        {
            commands.Release();
            RenderTexture.ReleaseTemporary(target);
            visual.gameObject.SetActive(wasActive);
        }
    }

    /// <summary>Play 재진입(도메인 리로드 끔 포함) 경계. 풀과 영구 PlayableGraph를 같이 정리한다.</summary>
    internal static void ResetForPlay()
    {
        if (_root != null)
        {
            foreach (WellsDroneVisualInstance visual in _root.GetComponentsInChildren<WellsDroneVisualInstance>(true))
                if (visual != null) visual.DisposeGraph();
            DestroyObject(_root.gameObject);
        }

        _root = null;
        _catalog = null;
        Inactive.Clear();
        Active.Clear();
        CreatedCount = 0;
        GraphCreateCount = 0;
        GraphDestroyCount = 0;
    }

    static void EnsureRoot()
    {
        if (_root != null) return;
        var go = new GameObject(RootName) { hideFlags = HideFlags.HideAndDontSave };
        _root = go.transform;
        if (Application.isPlaying) Object.DontDestroyOnLoad(go);
    }

    static WellsDroneVisualInstance Create(GameObject prefab)
    {
        GameObject go = Object.Instantiate(prefab, _root);
        go.name = prefab.name + " (Pooled)";
        go.hideFlags = HideFlags.HideAndDontSave;
        WellsDroneVisualInstance visual = go.AddComponent<WellsDroneVisualInstance>();
        visual.Initialize(prefab);
        CreatedCount++;
        return visual;
    }

    static void DestroyObject(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Object.Destroy(value);
        else Object.DestroyImmediate(value);
    }

    internal static void NotifyGraphCreated() => GraphCreateCount++;
    internal static void NotifyGraphDestroyed() => GraphDestroyCount++;
}

/// <summary>풀 인스턴스와 수명이 같은 Animator/PlayableGraph 소유자.</summary>
sealed class WellsDroneVisualInstance : MonoBehaviour
{
    internal GameObject SourcePrefab { get; private set; }

    Vector3 _prefabScale;
    Animator _animator;
    PlayableGraph _graph;
    AnimationMixerPlayable _mixer;
    AnimationClipPlayable _startPlayable;
    AnimationClipPlayable _loopPlayable;
    AnimationClip _startClip;
    AnimationClip _loopClip;
    bool _looping;

    internal void Initialize(GameObject sourcePrefab)
    {
        SourcePrefab = sourcePrefab;
        _prefabScale = transform.localScale;
        foreach (Collider collider in GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.useGravity = false;
            body.detectCollisions = false;
        }

        _animator = GetComponentInChildren<Animator>(true);
        if (_animator == null) _animator = gameObject.AddComponent<Animator>();
        _animator.applyRootMotion = false;
        gameObject.SetActive(false);
    }

    internal void PrepareForRent(Vector3 position, Quaternion rotation, float scale, AnimationClip start, AnimationClip loop)
    {
        transform.SetPositionAndRotation(position, rotation);
        transform.localScale = _prefabScale * scale;
        ConfigureGraph(start, loop);
        gameObject.SetActive(false);
    }

    internal void Play()
    {
        if (!_graph.IsValid()) return;
        _graph.Play();
        if (_startPlayable.IsValid()) _startPlayable.SetTime(0d);
        if (_loopPlayable.IsValid()) _loopPlayable.SetTime(0d);

        _looping = _startClip == null && _loopClip != null;
        _mixer.SetInputWeight(0, _looping ? 0f : 1f);
        _mixer.SetInputWeight(1, _looping ? 1f : 0f);
        _graph.Evaluate(0f);
    }

    internal void Tick()
    {
        if (!_graph.IsValid()) return;
        if (_looping)
        {
            if (!_loopPlayable.IsValid() || _loopClip == null || _loopClip.length <= 0f) return;
            double time = _loopPlayable.GetTime();
            if (time >= _loopClip.length) _loopPlayable.SetTime(time % _loopClip.length);
            return;
        }

        if (!_startPlayable.IsValid() || _startClip == null || _startClip.length <= 0f) return;
        if (_startPlayable.GetTime() < _startClip.length || !_loopPlayable.IsValid() || _loopClip == null) return;
        _looping = true;
        _loopPlayable.SetTime(0d);
        _mixer.SetInputWeight(0, 0f);
        _mixer.SetInputWeight(1, 1f);
    }

    internal void WarmClipTransitions()
    {
        if (_startPlayable.IsValid() && _startClip != null)
        {
            _startPlayable.SetTime(_startClip.length);
            Tick();
        }
        if (_loopPlayable.IsValid() && _loopClip != null)
        {
            _loopPlayable.SetTime(_loopClip.length);
            Tick();
        }
        if (_graph.IsValid()) _graph.Evaluate(0f);
    }

    internal void PrepareForReturn()
    {
        if (_graph.IsValid()) _graph.Stop();
        _looping = false;
        gameObject.SetActive(false);
    }

    internal void DisposeGraph()
    {
        if (!_graph.IsValid()) return;
        _graph.Destroy();
        WellsDroneVisualPool.NotifyGraphDestroyed();
        _startPlayable = default;
        _loopPlayable = default;
        _mixer = default;
    }

    void ConfigureGraph(AnimationClip start, AnimationClip loop)
    {
        if (_graph.IsValid() && _startClip == start && _loopClip == loop) return;
        DisposeGraph();
        _startClip = start;
        _loopClip = loop;
        if (start == null && loop == null) return;

        _graph = PlayableGraph.Create("WellsDroneVisual");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        _mixer = AnimationMixerPlayable.Create(_graph, 2);
        if (start != null)
        {
            _startPlayable = AnimationClipPlayable.Create(_graph, start);
            _graph.Connect(_startPlayable, 0, _mixer, 0);
        }
        if (loop != null)
        {
            _loopPlayable = AnimationClipPlayable.Create(_graph, loop);
            _graph.Connect(_loopPlayable, 0, _mixer, 1);
        }
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "WellsDrone", _animator);
        output.SetSourcePlayable(_mixer);
        _graph.Play();
        _graph.Stop();
        WellsDroneVisualPool.NotifyGraphCreated();
    }

    void OnDestroy() => DisposeGraph();
}
