using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 존 게이트 패널(object_panel) 모니터 화면 판 저작 보조(PLAN-title-monitor T2).
/// 화면 면 위치는 런타임에 못 잰다(fbx isReadable 0) → 에디터에서 메시를 평면 묶음으로 나눠 후보를 보고한다.
/// 패널 프랍 루트가 MeshFilter 를 직접 가지므로 메시 공간 = 패널 로컬 공간이다.
/// </summary>
static class ZoneMonitorScreenAuthoring
{
    const string PanelPrefab = "Assets/2.Prefabs/Environment/Machinery/PowerUnits/object_panel.prefab";
    const string OutlineMatPath = "Assets/3.Materials/Map/MA_ZoneInteractOutline.mat";

    [MenuItem("Tools/Map/Authoring/Zone Monitor Screen/1. 화면 면 후보 보고 (읽기 전용)")]
    static void ReportCandidates()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefab);
        var mf = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null) { Debug.LogError("[ZoneMonitorScreen] object_panel 메시를 못 찾았다."); return; }

        Mesh mesh = mf.sharedMesh;
        Vector3[] v;
        int[] tri;
        try { v = mesh.vertices; tri = mesh.triangles; }
        catch (System.Exception e) { Debug.LogError($"[ZoneMonitorScreen] 메시 읽기 실패(isReadable={mesh.isReadable}): {e.Message}"); return; }
        if (v == null || v.Length == 0) { Debug.LogError($"[ZoneMonitorScreen] 정점 0 (isReadable={mesh.isReadable})"); return; }

        // 평면 묶음 = (양자화 법선, 평면 거리) 키.
        var groups = new Dictionary<(int, int, int, int), List<int>>();
        for (int t = 0; t < tri.Length; t += 3)
        {
            Vector3 a = v[tri[t]], b = v[tri[t + 1]], c = v[tri[t + 2]];
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) continue;
            n.Normalize();
            float d = Vector3.Dot(n, a);
            var key = (Mathf.RoundToInt(n.x * 20), Mathf.RoundToInt(n.y * 20), Mathf.RoundToInt(n.z * 20), Mathf.RoundToInt(d * 200));
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<int>();
            list.Add(t);
        }

        var sb = new StringBuilder($"[ZoneMonitorScreen] {mesh.name} — 정점 {v.Length} · 삼각형 {tri.Length / 3} · bounds {mesh.bounds.center} / {mesh.bounds.size} · 평면 묶음 {groups.Count}\n" +
                                   $"패널 프리팹 루트 스케일 {prefab.transform.localScale} · 메시 Transform {mf.transform.name}\n");
        int rank = 0;
        foreach (var kv in groups.OrderByDescending(g => Area(v, tri, g.Value)).Take(12))
        {
            Vector3 n = Vector3.Cross(v[tri[kv.Value[0] + 1]] - v[tri[kv.Value[0]]], v[tri[kv.Value[0] + 2]] - v[tri[kv.Value[0]]]).normalized;
            Bounds bb = new Bounds(v[tri[kv.Value[0]]], Vector3.zero);
            foreach (int t in kv.Value) { bb.Encapsulate(v[tri[t]]); bb.Encapsulate(v[tri[t + 1]]); bb.Encapsulate(v[tri[t + 2]]); }

            // 평면 안 축: 위쪽에 가까운 축 = up 투영, 옆 = n × up
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, n);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(Vector3.forward, n);
            up.Normalize();
            Vector3 side = Vector3.Cross(n, up).normalized;
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            foreach (int t in kv.Value)
                for (int k = 0; k < 3; k++)
                {
                    Vector3 p = v[tri[t + k]];
                    float pu = Vector3.Dot(p, side), pv = Vector3.Dot(p, up);
                    minU = Mathf.Min(minU, pu); maxU = Mathf.Max(maxU, pu); minV = Mathf.Min(minV, pv); maxV = Mathf.Max(maxV, pv);
                }
            Vector3 center = side * (minU + maxU) * 0.5f + up * (minV + maxV) * 0.5f + n * Vector3.Dot(n, bb.center);
            Quaternion rot = Quaternion.LookRotation(-n, up);   // Unity Quad 앞면은 -Z → 법선 n 쪽을 보게
            sb.AppendLine($"  #{rank++} 면적 {Area(v, tri, kv.Value):0.0000} · 삼각형 {kv.Value.Count} · 법선 {n:F2} · 중심 {center:F4} · " +
                          $"폭×높이 {maxU - minU:F3}×{maxV - minV:F3} · 쿼드 회전 {rot.eulerAngles:F1}");
        }
        Debug.Log(sb.ToString());
    }

    static float Area(Vector3[] v, int[] tri, List<int> ts)
    {
        float s = 0;
        foreach (int t in ts) s += Vector3.Cross(v[tri[t + 1]] - v[tri[t]], v[tri[t + 2]] - v[tri[t]]).magnitude * 0.5f;
        return s;
    }

    // 미리보기 후보 — 1번 보고에서 고른 값(패널 로컬). 확정되면 게이트에 같은 값을 쓴다.
    internal static readonly (string name, Vector3 pos, Vector3 euler, Vector2 size)[] Candidates =
    {
        ("A_front_#8", new Vector3(-0.2190f, 1.2047f, 0.2227f), new Vector3(45.6f, 134.1f, 0f), new Vector2(0.681f, 0.675f)),
        ("B_recessed_#9", new Vector3(-0.1640f, 1.1209f, 0.1640f), new Vector3(45.4f, 133.5f, 0f), new Vector2(0.693f, 0.678f)),
    };

    [MenuItem("Tools/Map/Authoring/Zone Monitor Screen/3. 후보 미리보기 PNG (프리뷰 씬)")]
    static void PreviewCandidates()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefab);
        var logo = AssetDatabase.LoadAssetAtPath<Texture>("Assets/50.Art/Environment/Textures/Props/Office/monitor_screen.png");
        string outDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ZoneMonitorPreview");
        System.IO.Directory.CreateDirectory(outDir);

        foreach (var c in Candidates)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try
            {
                var panel = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(quad, scene);
                quad.transform.SetParent(panel.transform, false);
                Vector3 n = Quaternion.Euler(c.euler) * Vector3.back;
                quad.transform.localPosition = c.pos + n * 0.003f;
                quad.transform.localRotation = Quaternion.Euler(c.euler);
                quad.transform.localScale = new Vector3(c.size.x * 0.95f, c.size.y * 0.95f, 1f);
                var unlit = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { mainTexture = logo };
                quad.GetComponent<Renderer>().sharedMaterial = unlit;

                var lightGo = new GameObject("L"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, scene);
                var l = lightGo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.2f;
                lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);

                var camGo = new GameObject("Cam"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.25f, 0.25f, 0.3f);
                Vector3 target = panel.transform.TransformPoint(c.pos);
                camGo.transform.position = target + n * 2.2f + Vector3.up * 0.4f;
                camGo.transform.LookAt(target);

                var rt = new RenderTexture(640, 480, 24);
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(640, 480, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); tex.Apply();
                RenderTexture.active = null;
                string path = System.IO.Path.Combine(outDir, c.name + ".png");
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex); cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(unlit);
                Debug.Log($"[ZoneMonitorScreen] 미리보기 {path}");
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        }
    }

    [MenuItem("Tools/Map/Authoring/Zone Monitor Screen/4. ZoneL_typeB 런타임 재현 PNG (프리뷰 씬)")]
    static void PreviewZoneRuntime()
    {
        var zonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2.Prefabs/Environment/Layouts/Zones/ZoneL_typeB.prefab");
        string outDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ZoneMonitorPreview");
        System.IO.Directory.CreateDirectory(outDir);
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var sb = new StringBuilder("[ZoneMonitorScreen] 런타임 재현\n");
        try
        {
            var zone = (GameObject)PrefabUtility.InstantiatePrefab(zonePrefab, scene);
            var gate = zone.GetComponent<ZoneBridgeGate>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(ZoneBridgeGate).GetMethod("BuildMonitorScreens", flags).Invoke(gate, null);
            gate.SetPanelActivatedVisual(0, true);
            gate.SetPanelHighlighted(1, true);

            for (int i = 0; i < gate.PanelCount; i++)
            {
                Transform p = gate.Panels[i];
                var mr = p.GetComponent<MeshRenderer>();
                Transform q = p.Find("MonitorScreen");
                sb.AppendLine($"  패널{i} {p.name} rot {p.eulerAngles:F0} scale {p.lossyScale:F2} renderer={(mr != null)} mats={(mr != null ? mr.sharedMaterials.Length : 0)} " +
                              $"screen={(q != null)} screenOn={(q != null && q.GetComponent<Renderer>().enabled)} " +
                              $"screenWorld={(q != null ? q.position.ToString("F2") : "-")} panelBoundsCenter={(mr != null ? mr.bounds.center.ToString("F2") : "-")}");
            }

            var lightGo = new GameObject("L"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, scene);
            var l = lightGo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(55, -30, 0);

            for (int i = 0; i < 2; i++)
            {
                Transform p = gate.Panels[i];
                var camGo = new GameObject("Cam"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
                Vector3 target = p.GetComponent<Renderer>().bounds.center;
                camGo.transform.position = target + new Vector3(0, 7f, -5f);   // 게임처럼 위에서 비스듬히
                camGo.transform.LookAt(target);
                var rt = new RenderTexture(640, 480, 24);
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(640, 480, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); tex.Apply();
                RenderTexture.active = null;
                string path = System.IO.Path.Combine(outDir, $"zone_panel{i}.png");
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex); cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt);
                Object.DestroyImmediate(camGo);
                sb.AppendLine($"  PNG {path}");
            }
            gate.SetPanelHighlighted(1, false);
        }
        catch (System.Exception e) { sb.AppendLine("  예외: " + e); }
        finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        Debug.Log(sb.ToString());
    }

    const string ZonePath = "Assets/2.Prefabs/Environment/Layouts/Zones/ZoneL_typeB.prefab";

    /// <summary>
    /// ZoneL_typeB 게이트에 모니터 화면·외곽선 값을 쓴다(후보 A = 앞면 #8).
    /// 🔴 YAML 을 직접 고치면 **프리팹 모드에 열려 있던 옛 내용이 자동 저장으로 덮어쓴다**(10-03 실제로 12줄이 사라졌다).
    ///    열려 있으면 그 스테이지에 쓰고 저장, 아니면 LoadPrefabContents 로 쓴다 — 메모리와 디스크가 항상 같다.
    /// </summary>
    [MenuItem("Tools/Map/Authoring/Zone Monitor Screen/5. ZoneL_typeB 게이트에 값 쓰기")]
    static void WriteGateValues()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/3.Materials/title/MA_TitleMonitorUI.mat");
        var logo = AssetDatabase.LoadAssetAtPath<Texture>("Assets/50.Art/Environment/Textures/Props/Office/monitor_screen.png");
        var outline = AssetDatabase.LoadAssetAtPath<Material>(OutlineMatPath);
        if (mat == null || logo == null || outline == null) { Debug.LogError($"[ZoneMonitorScreen] 참조 누락 mat={mat} logo={logo} outline={outline}"); return; }

        var c = Candidates[0];
        var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
        bool inStage = stage != null && stage.assetPath == ZonePath;
        GameObject root = inStage ? stage.prefabContentsRoot : PrefabUtility.LoadPrefabContents(ZonePath);
        try
        {
            var gate = root.GetComponent<ZoneBridgeGate>();
            var so = new SerializedObject(gate);
            so.FindProperty("monitorScreenEnabled").boolValue = true;
            so.FindProperty("screenLocalPosition").vector3Value = c.pos;
            so.FindProperty("screenLocalEuler").vector3Value = c.euler;
            so.FindProperty("screenSize").vector2Value = c.size * 0.95f;
            so.FindProperty("screenMaterialSource").objectReferenceValue = mat;
            so.FindProperty("screenLogo").objectReferenceValue = logo;
            so.FindProperty("screenOnDuration").floatValue = 0.9f;
            so.FindProperty("highlightOutlineMaterial").objectReferenceValue = outline;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (inStage)
            {
                EditorUtility.SetDirty(gate);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(stage.scene);
                PrefabUtility.SaveAsPrefabAsset(root, ZonePath);   // 스테이지 내용 = 디스크
                Debug.Log("[ZoneMonitorScreen] 프리팹 모드에 열린 ZoneL_typeB 에 쓰고 저장했다.");
            }
            else
            {
                PrefabUtility.SaveAsPrefabAsset(root, ZonePath);
                Debug.Log("[ZoneMonitorScreen] ZoneL_typeB 프리팹 파일에 썼다(프리팹 모드 아님).");
            }
        }
        finally
        {
            if (!inStage) PrefabUtility.UnloadPrefabContents(root);
        }

        // 디스크에서 다시 읽어 확인
        var check = AssetDatabase.LoadAssetAtPath<GameObject>(ZonePath).GetComponent<ZoneBridgeGate>();
        var cs = new SerializedObject(check);
        Debug.Log($"[ZoneMonitorScreen] 확인 — enabled={cs.FindProperty("monitorScreenEnabled").boolValue} " +
                  $"outline={cs.FindProperty("highlightOutlineMaterial").objectReferenceValue} mat={cs.FindProperty("screenMaterialSource").objectReferenceValue}");
    }

    [MenuItem("Tools/Map/Authoring/Zone Monitor Screen/2. 외곽선 머티리얼 만들기")]
    static void CreateOutlineMaterial()
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(OutlineMatPath) != null) { Debug.Log($"[ZoneMonitorScreen] 이미 있다: {OutlineMatPath}"); return; }
        var shader = Shader.Find("Zone/InteractOutline");
        if (shader == null) { Debug.LogError("[ZoneMonitorScreen] 셰이더 Zone/InteractOutline 없음"); return; }
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(OutlineMatPath));
        AssetDatabase.CreateAsset(new Material(shader) { name = "MA_ZoneInteractOutline" }, OutlineMatPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ZoneMonitorScreen] 만들었다: {OutlineMatPath} guid={AssetDatabase.AssetPathToGUID(OutlineMatPath)}");
    }
}
