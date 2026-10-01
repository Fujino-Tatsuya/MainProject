using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

/// <summary>Read-only preview and verification of the production monster prefabs.</summary>
public static class MonsterSurfaceTextureReview
{
    const string Art = "Assets/50.Art/Char/Monster/Redesign/SurfaceV1";
    const string Output = "Generated/monster-remodel-20260926/surface-v1/material-review";
    [Serializable] public class Manifest { public Monster[] monsters; }
    [Serializable] public class Monster { public string id, game_prefab; public Entry[] meshes; }
    [Serializable] public class Entry { public string objectName, rendererPath, meshPath; public bool skinned; public Bounds animationBounds; public string[] materialPaths; }
    [Serializable] public class BoundEntry { public string path; public Bounds bounds; }
    [Serializable] public class RenderReport { public string monster, mode, stage, error; public int clips, animationSamples; public float outsideBoundsError; public List<BoundEntry> rendererBounds = new(); public List<string> files = new(); }
    static Manifest Read() => JsonUtility.FromJson<Manifest>(File.ReadAllText(Art + "/manifest.json"));
    static void Json(string path, object value) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(value, true)); }
    static Transform Find(GameObject root, string path) => root.transform.Find(path) ?? throw new InvalidOperationException("Missing renderer: " + path);
    static Mesh MeshOf(Transform t) => t.GetComponent<SkinnedMeshRenderer>() ? t.GetComponent<SkinnedMeshRenderer>().sharedMesh : t.GetComponent<MeshFilter>().sharedMesh;
    static string Group(Entry e) => e.objectName.StartsWith("G5_") ? e.objectName : "Body";
    static string MaterialPath(Monster m, Entry e) => Art + "/" + m.id + "/Materials/" + m.id + "_" + Group(e) + ".mat";
    internal sealed class Preview : IDisposable
    {
        public readonly GameObject root;
        public readonly PreviewRenderUtility utility = new();
        public readonly AnimationClip[] clips;
        readonly Animator animator;
        readonly List<(SkinnedMeshRenderer skin, Mesh mesh, MeshRenderer proxy)> frozen = new();
        readonly List<Material> materials = new();
        readonly Vector3[] positions, scales; readonly Quaternion[] rotations; readonly Transform[] transforms;
        PlayableGraph graph; AnimationClipPlayable playable;
        public Preview(Monster monster)
        {
            root = utility.InstantiatePrefabInScene(AssetDatabase.LoadAssetAtPath<GameObject>(monster.game_prefab));
            root.transform.position = Vector3.zero;
            foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true)) if (script) script.enabled = false;
            foreach (var particle in root.GetComponentsInChildren<ParticleSystem>(true)) particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) r.enabled = false;
            // The runtime controls this overlay; reproduce its neutral state in the isolated preview.
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var slots = r.sharedMaterials;
                for (int i = 0; i < slots.Length; i++) if (slots[i] && (slots[i].shader.name == "Monster/InterruptOverlay" || slots[i].shader.name == "VFX/DissolveOverlay"))
                { var copy = new Material(slots[i]); copy.SetFloat("_Dissolve", 1); materials.Add(copy); slots[i] = copy; }
                r.sharedMaterials = slots;
            }
            var body = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(s => s.bones.Length).First();
            animator = body.GetComponentInParent<Animator>(true);
            foreach (var a in root.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            animator.enabled = true; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.applyRootMotion = false;
            transforms = root.GetComponentsInChildren<Transform>(true); positions = transforms.Select(t => t.localPosition).ToArray(); rotations = transforms.Select(t => t.localRotation).ToArray(); scales = transforms.Select(t => t.localScale).ToArray();
            var controllers = new List<RuntimeAnimatorController>(); if (animator.runtimeAnimatorController) controllers.Add(animator.runtimeAnimatorController);
            var data = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/2.Prefabs/Monster/Data/" + monster.id + "Data.asset");
            if (data) using (var so = new SerializedObject(data)) { var prop = so.GetIterator(); while (prop.Next(true)) if (prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue is RuntimeAnimatorController controller) controllers.Add(controller); }
            clips = controllers.SelectMany(c => c.animationClips).Where(c => c).Distinct().OrderBy(c => c.name).ToArray();
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.forceRenderingOff = false; if (skin.name.Contains("Blades")) continue;
                var mesh = new Mesh(); var go = new GameObject("Frozen material proof"); go.transform.SetParent(skin.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; var proxy = go.AddComponent<MeshRenderer>(); proxy.sharedMaterials = skin.sharedMaterials; proxy.forceRenderingOff = true;
                frozen.Add((skin, mesh, proxy));
            }
            utility.camera.clearFlags = CameraClearFlags.SolidColor; utility.camera.backgroundColor = new Color(.20f, .22f, .23f); utility.ambientColor = new Color(.65f, .65f, .65f);
            utility.lights[0].intensity = 1.4f; utility.lights[0].transform.rotation = Quaternion.Euler(35, 135, 0);
            utility.lights[1].intensity = .9f; utility.lights[1].transform.rotation = Quaternion.Euler(25, -45, 0);
            Bake();
        }
        public void Select(int clip)
        {
            if (graph.IsValid()) graph.Destroy(); animator.Rebind();
            for (int i = 0; i < transforms.Length; i++) { transforms[i].localPosition = positions[i]; transforms[i].localRotation = rotations[i]; transforms[i].localScale = scales[i]; }
            graph = PlayableGraph.Create("Native surface texture check"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual); playable = AnimationClipPlayable.Create(graph, clips[clip]); playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
            var output = AnimationPlayableOutput.Create(graph, "Original rig", animator); output.SetSourcePlayable(playable); output.SetWeight(1); graph.Play(); Sample(0);
        }
        public void Sample(float seconds) { playable.SetTime(seconds); graph.Evaluate(.00001f); Bake(); }
        void Bake() { foreach (var p in frozen) { p.skin.BakeMesh(p.mesh, true); p.proxy.gameObject.SetActive(p.skin.enabled); } }
        public bool Finite() => frozen.All(p => p.mesh.vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)));
        public float BoundsError()
        {
            float error = 0;
            foreach (var p in frozen) foreach (var v in p.mesh.vertices)
            {
                var point = p.skin.transform.TransformPoint(v); error = Mathf.Max(error, Vector3.Distance(point, p.skin.bounds.ClosestPoint(point)));
            }
            return error;
        }
        public void RefreshRenderBounds()
        {
            utility.BeginPreview(new Rect(0, 0, 64, 64), GUIStyle.none); utility.Render(true); utility.EndPreview();
        }
        void Frame(float yaw, float pitch)
        {
            var bounds = new Bounds(); bool found = false;
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy)) { if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds); }
            if (!found) throw new InvalidOperationException("No visible mesh."); float radius = bounds.extents.magnitude;
            utility.camera.orthographic = true; utility.camera.orthographicSize = radius * 1.04f; utility.camera.nearClipPlane = .001f; utility.camera.farClipPlane = Mathf.Max(100, radius * 8 + 20);
            utility.camera.transform.position = bounds.center + Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward * Mathf.Max(10, radius * 3); utility.camera.transform.LookAt(bounds.center);
        }
        public void Draw(Rect rect, float yaw, float pitch)
        {
            Frame(yaw, pitch); utility.BeginPreview(rect, GUIStyle.none); utility.Render(true); GUI.DrawTexture(rect, utility.EndPreview(), ScaleMode.ScaleToFit);
        }
        public void Capture(string filename, float yaw, float pitch)
        {
            Frame(yaw, pitch);
            Directory.CreateDirectory(Path.GetDirectoryName(filename)); bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false; Texture2D image = null;
            try { utility.BeginStaticPreview(new Rect(0, 0, 1000, 1000)); utility.Render(true); image = utility.EndStaticPreview(); File.WriteAllBytes(filename, image.EncodeToPNG()); }
            finally { if (image) Object.DestroyImmediate(image); ShaderUtil.allowAsyncCompilation = async; }
        }
        public void Dispose() { if (graph.IsValid()) graph.Destroy(); foreach (var p in frozen) Object.DestroyImmediate(p.mesh); foreach (var m in materials) Object.DestroyImmediate(m); utility.Cleanup(); }
    }
    [MenuItem("Tools/Monster Surface Textures/Verify Saved Prefabs")]
    public static void VerifySaved()
    {
        foreach (var m in Read().monsters)
        {
            var report = new RenderReport { monster = m.id, mode = "gpu-saved", stage = "checking" };
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(m.game_prefab);
                foreach (var e in m.meshes)
                {
                    var t = Find(prefab, e.rendererPath); var mesh = MeshOf(t); var slots = t.GetComponent<Renderer>().sharedMaterials;
                    if (mesh != AssetDatabase.LoadAssetAtPath<Mesh>(e.meshPath) || mesh.uv.Length != mesh.vertexCount) throw new InvalidOperationException("Mesh/UV mismatch.");
                    if (slots.Length != e.materialPaths.Length || AssetDatabase.GetAssetPath(slots[0]) != MaterialPath(m, e)) throw new InvalidOperationException("Material mismatch.");
                    for (int i = 1; i < slots.Length; i++) if (AssetDatabase.GetAssetPath(slots[i]) != e.materialPaths[i]) throw new InvalidOperationException("Display/overlay slot mismatch.");
                    foreach (var pair in new[] { ("BaseColor", "_BaseMap"), ("Normal", "_BumpMap"), ("MetallicSmoothness", "_MetallicGlossMap") })
                    {
                        string path = Art + "/" + m.id + "/Textures/" + Group(e) + "/" + m.id + "_" + Group(e) + "_" + pair.Item1 + ".png";
                        if (AssetDatabase.GetAssetPath(slots[0].GetTexture(pair.Item2)) != path) throw new InvalidOperationException("Texture mismatch.");
                        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                        if (importer.sRGBTexture != (pair.Item1 == "BaseColor") || (pair.Item1 == "Normal" && importer.textureType != TextureImporterType.NormalMap)) throw new InvalidOperationException("Texture importer mismatch.");
                    }
                    if (!slots[0].shader.isSupported || !slots[0].IsKeywordEnabled("_NORMALMAP") || !slots[0].IsKeywordEnabled("_METALLICSPECGLOSSMAP")) throw new InvalidOperationException("Unsupported material.");
                }
                using var preview = new Preview(m); report.clips = preview.clips.Length;
                int idle = Array.FindIndex(preview.clips, c => c.name == "Idle"); if (idle >= 0) preview.Select(idle);
                foreach (var view in new[] { ("front", 35f, 15f), ("rear", 215f, 15f), ("top", 35f, 55f) })
                { var p = Output + "/" + m.id + "/saved-" + view.Item1 + ".png"; preview.Capture(p, view.Item2, view.Item3); report.files.Add(p); }
                for (int c = 0; c < preview.clips.Length; c++)
                {
                    preview.Select(c);
                    for (int f = 0; f < 5; f++)
                    {
                        preview.Sample(preview.clips[c].length * f / 4); string path = Output + "/" + m.id + "/saved-clips/" + preview.clips[c].name + "-" + f + ".png";
                        preview.Capture(path, 35, 25); report.files.Add(path);
                        if (!preview.Finite()) throw new InvalidOperationException("Non-finite pose."); report.outsideBoundsError = Mathf.Max(report.outsideBoundsError, preview.BoundsError()); report.animationSamples++;
                    }
                }
                if (report.outsideBoundsError > .0001f) throw new InvalidOperationException("Invalid culling bounds."); report.stage = "verified";
            }
            catch (Exception ex) { report.stage = "failed"; report.error = ex.ToString(); Debug.LogException(ex); }
            Json(Output + "/" + m.id + "/saved-report.json", report);
        }
    }

    sealed class ReviewWindow : EditorWindow
    {
        Preview preview; Monster[] monsters; int monsterIndex = 3, clipIndex; float time; bool playing; double last; float yaw = 35, pitch = 15;
        [MenuItem("Tools/Monster Surface Textures/Open Animation And Texture Review")]
        static void Open()
        {
            var window = GetWindow<ReviewWindow>("몬스터 메쉬·텍스처 검토"); window.minSize = new Vector2(600, 650);
            var rect = window.position; rect.width = Mathf.Max(rect.width, 640); rect.height = Mathf.Max(rect.height, 800); window.position = rect;
            window.playing = true; window.Show(); window.Focus();
        }
        void OnEnable() { monsters = Read().monsters; EditorApplication.update += Tick; last = EditorApplication.timeSinceStartup; }
        void OnDisable() { EditorApplication.update -= Tick; preview?.Dispose(); preview = null; }
        void Tick() { double now = EditorApplication.timeSinceStartup; if (playing && preview != null && preview.clips.Length > 0) { time = (time + (float)(now - last)) % Mathf.Max(.01f, preview.clips[clipIndex].length); preview.Sample(time); Repaint(); } last = now; }
        void OnGUI()
        {
            if (monsters == null) return;
            EditorGUILayout.LabelField("저장된 게임 프리팹 · 기존 리그 · Unity 실제 렌더링", EditorStyles.boldLabel);
            int next = EditorGUILayout.Popup("몬스터", monsterIndex, monsters.Select(m => m.id).ToArray());
            if (next != monsterIndex || preview == null) { monsterIndex = next; preview?.Dispose(); preview = new Preview(monsters[monsterIndex]); clipIndex = Mathf.Max(0, Array.FindIndex(preview.clips, c => c.name == "Idle")); time = 0; preview.Select(clipIndex); }
            int clip = EditorGUILayout.Popup("동작", clipIndex, preview.clips.Select(c => c.name).ToArray());
            if (clip != clipIndex) { clipIndex = clip; time = 0; preview.Select(clip); }
            playing = EditorGUILayout.Toggle("재생", playing); float seconds = EditorGUILayout.Slider("시점", time, 0, preview.clips[clipIndex].length);
            if (seconds != time) { time = seconds; preview.Sample(time); }
            using (new EditorGUILayout.HorizontalScope()) { if (GUILayout.Button("앞면")) { yaw = 35; pitch = 15; } if (GUILayout.Button("뒷면")) { yaw = 215; pitch = 15; } if (GUILayout.Button("탑다운")) { yaw = 35; pitch = 55; } }
            preview.Draw(GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)), yaw, pitch);
            EditorGUILayout.HelpBox("색·노멀·금속 맵 적용. 검토 중 인터럽트 외곽 효과는 숨깁니다. 실제 전투의 상태 전환·네트워크 검사는 포함하지 않습니다.", MessageType.Info);
        }
    }
}
