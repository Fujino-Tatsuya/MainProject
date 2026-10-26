using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 로비 형광등 (2026-10-23 사용자 요청) — URP 에는 실시간 튜브 라이트가 없어서 포인트 라이트를 줄 세워 '일자형 빛'을 낸다.
///   · 형광등 한 개 = 이름이 "_Fluorescent" 또는 "_Fluorescent (n)" 인 묶음. 묶음의 위치·회전이 형광등 자리·방향(로컬 X)이다.
///     사용자가 이 묶음을 복제해 배치한다 — 도구는 묶음의 내용만 다시 만들고 위치·회전은 건드리지 않는다.
///   · 내용: 포인트 라이트(형광등당 0/1/3개 — 메뉴로 고름, 3개면 LightSpacing 간격 일렬, 그림자 끔) + 바닥 빛 줄기.
///     일자형 모양은 바닥 빛 줄기가 맡고, 라이트는 주변(바닥·캐릭터·소품)을 밝히는 역할만 한다(사용자 10-23).
///   · **막대(관·마개) 메시는 두지 않는다** — 탑다운이라 천장 요소가 필요 없고, 공중의 막대가 바닥에 놓인 것처럼 보였다(사용자 10-23).
///   · 바닥 빛 줄기(기본 켬 — 사용자 결정 10-23 C안): 형광등 바로 아래 바닥(FloorTopY)에 길쭉하고 부드러운 가산 빛을 깐다 — 메뉴로 켜고 끈다.
/// 메뉴
///   · "형광등 — 선택 위치에 놓기": 빈 오브젝트를 고르면 그 아래 묶음 하나를 만들고(이미 있으면 다시 조립), 포인트 라이트를 고르면 옆에 만들고 원래 라이트는 끈다.
///   · "형광등 — 씬 전체 다시 조립": 활성 씬의 모든 묶음을 지금 설정으로 다시 조립한다.
///   · "형광등 — 바닥 빛 줄기 사용": 켜고 끄기(EditorPrefs). 바꾼 뒤 '씬 전체 다시 조립'.
///   · "조명 캡처": 게임 카메라 각도(PlayerFollowCamera: 오프셋 (7,17,-7)·59.8°/-45°·FOV 34)·후처리로 Temp/LightingCapture/ 에 PNG.
/// </summary>
static class LobbyFluorescentAuthoring
{
    const string MatFolderParent = "Assets/3.Materials/Environment";
    const string MatFolder = MatFolderParent + "/Lighting";
    const string StreakTexPath = MatFolder + "/T_FluorescentStreak.png";
    const string StreakMatPath = MatFolder + "/M_FluorescentStreak.mat";

    const string UnitBaseName = "_Fluorescent";
    static readonly Regex UnitName = new Regex(@"^_Fluorescent( \(\d+\))?$");

    // 빛
    static readonly Color LightColor = new Color(0.94f, 0.98f, 1.00f); // 차가운 흰색(너무 푸르지 않게)
    // 형광등당 라이트 수 — 0(빛 줄기만) / 1 / 3. 일자형 모양은 바닥 빛 줄기가 맡으므로 라이트는 '주변을 밝히는' 역할만 한다.
    static readonly int[] LightCountChoices = { 0, 1, 3 };
    const int DefaultLightCount = 1;
    const string LightCountPrefKey = "MainProject.LobbyFluorescent.LightCount";
    const string LightCountMenuBase = "Tools/Map/Authoring/형광등 — 라이트 수/";
    const float LightSpacing = 0.6f;     // 3개일 때 간격 — 형광등 길이(1.2m) 끝까지
    const float LightIntensityEach = 1.2f; // 3개일 때 하나의 세기
    const float LightRange = 3.5f;
    const float LightIntensitySingle = 2.6f; // 1개일 때 — 3개(1.2×3)와 비슷한 밝기가 되게
    const float LightRangeSingle = 3.8f;
    const float LightDrop = 0.1f;

    // 바닥 빛 줄기
    const float FloorTopY = 0f;          // 로비 보행면(레이캐스트로 확인)
    const float StreakLift = 0.012f;     // 바닥과 겹쳐 깜빡이지 않게
    const float StreakLength = 2.6f, StreakWidth = 0.75f;
    static readonly Color StreakColor = new Color(0.18f, 0.187f, 0.195f, 1f); // 가산 밝기 — 블룸 기준(1.1)보다 한참 낮게(0.16 흐릿 → 0.24 → 사용자 요청으로 살짝 옅게 0.18)
    const string StreakPrefKey = "MainProject.LobbyFluorescent.FloorStreak";
    const string StreakMenu = "Tools/Map/Authoring/형광등 — 바닥 빛 줄기 사용";

    // 캡처 = PlayerFollowCamera
    static readonly Vector3 CamOffset = new Vector3(7f, 17f, -7f);
    static readonly Vector3 CamEuler = new Vector3(59.787f, -45f, 0f);
    const float CamFov = 34f;
    const string Tag = "[Fluorescent]";

    static bool StreakOn
    {
        get => EditorPrefs.GetBool(StreakPrefKey, true); // 기본 켬 — 사용자 결정(10-23 C안). 팀원 PC 에서 다시 조립해도 빛 줄기가 유지된다
        set => EditorPrefs.SetBool(StreakPrefKey, value);
    }

    [MenuItem(StreakMenu)]
    static void ToggleStreak()
    {
        StreakOn = !StreakOn;
        Debug.Log($"{Tag} 바닥 빛 줄기 {(StreakOn ? "켬" : "끔")} — '형광등 — 씬 전체 다시 조립'으로 반영.");
    }

    [MenuItem(StreakMenu, true)]
    static bool ToggleStreakValidate()
    {
        Menu.SetChecked(StreakMenu, StreakOn);
        return true;
    }

    static int LightCountSetting
    {
        get
        {
            int v = EditorPrefs.GetInt(LightCountPrefKey, DefaultLightCount);
            return System.Array.IndexOf(LightCountChoices, v) >= 0 ? v : DefaultLightCount;
        }
        set => EditorPrefs.SetInt(LightCountPrefKey, value);
    }

    static void SetLightCount(int n)
    {
        LightCountSetting = n;
        Debug.Log($"{Tag} 형광등당 라이트 {n}개 — '형광등 — 씬 전체 다시 조립'으로 반영.");
    }

    static bool ValidateLightCount(int n)
    {
        foreach (int c in LightCountChoices) Menu.SetChecked(LightCountMenuBase + MenuLabel(c), c == LightCountSetting);
        return true;
    }

    static string MenuLabel(int n) => n == 0 ? "0개 (빛 줄기만)" : $"{n}개";

    [MenuItem(LightCountMenuBase + "0개 (빛 줄기만)")] static void LightCount0() => SetLightCount(0);
    [MenuItem(LightCountMenuBase + "0개 (빛 줄기만)", true)] static bool LightCount0V() => ValidateLightCount(0);
    [MenuItem(LightCountMenuBase + "1개")] static void LightCount1() => SetLightCount(1);
    [MenuItem(LightCountMenuBase + "1개", true)] static bool LightCount1V() => ValidateLightCount(1);
    [MenuItem(LightCountMenuBase + "3개")] static void LightCount3() => SetLightCount(3);
    [MenuItem(LightCountMenuBase + "3개", true)] static bool LightCount3V() => ValidateLightCount(3);

    // ───────── 놓기 ─────────

    [MenuItem("Tools/Map/Authoring/형광등 — 선택 위치에 놓기")]
    static void Build()
    {
        if (!CanEdit()) return;
        // 씬 오브젝트만(프리팹 에셋 제외), 부모·자식을 함께 골랐으면 맨 위만.
        GameObject[] sel = Selection.GetFiltered<GameObject>(SelectionMode.Editable | SelectionMode.ExcludePrefab | SelectionMode.TopLevel);
        if (sel.Length == 0) { Debug.LogError($"{Tag} 형광등을 놓을 빈 오브젝트나 포인트 라이트를 (씬에서) 선택하고 실행."); return; }

        bool streak = StreakOn;
        int lights = LightCountSetting;
        if (lights == 0 && !streak) { Debug.LogError($"{Tag} 라이트 0개 + 빛 줄기 꺼짐이면 형광등이 비어 버린다 — 둘 중 하나를 켜고 실행."); return; }
        bool captured = false;
        var built = new List<Object>();
        foreach (GameObject target in sel)
        {
            if (EditorUtility.IsPersistent(target)) { Debug.LogWarning($"{Tag} '{target.name}' 는 에셋이라 건너뜀."); continue; }

            Transform unit;
            GameObject disableAfter = null; // 원래 포인트 라이트 — '전' 캡처 뒤, 조립에 성공했을 때만 끈다
            if (UnitName.IsMatch(target.name)) unit = target.transform;                 // 묶음을 골랐으면 그 묶음을 다시 조립
            else if (InsideUnit(target.transform)) { Debug.LogWarning($"{Tag} '{target.name}' 는 형광등 묶음 안의 부품이라 건너뜀 — 묶음을 선택."); continue; }
            else
            {
                Transform host;
                Light src = target.GetComponent<Light>();
                if (src != null)
                {
                    if (src.type != LightType.Point) { Debug.LogWarning($"{Tag} '{target.name}' 는 포인트 라이트가 아니라 건너뜀."); continue; }
                    host = FindOrCreateSibling(target.transform, "Fluorescent_" + target.name);
                    if (target.activeSelf) disableAfter = target; // 원래 라이트는 끄기만 한다(아래에서)
                }
                else host = target.transform;
                unit = host.Find(UnitBaseName);
                if (unit == null) unit = CreateChild(host, UnitBaseName);
            }

            bool doCapture = !captured; // 실제로 조립하는 첫 대상만 찍는다
            Vector3 floor = new Vector3(unit.position.x, FloorTopY, unit.position.z);
            string tag = Safe(unit.parent != null ? unit.parent.name : unit.name);
            if (doCapture) CaptureHidden(unit, floor, tag + "_before");

            if (!Rebuild(unit, streak, lights)) continue;
            if (disableAfter != null) { Undo.RecordObject(disableAfter, "Fluorescent"); disableAfter.SetActive(false); }
            built.Add(unit.gameObject);
            if (doCapture) { Capture(floor, tag + "_after"); captured = true; }
        }
        if (built.Count > 0)
        {
            Selection.objects = built.ToArray(); // 다음 실행이 같은 묶음을 다시 조립하도록
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
    }

    [MenuItem("Tools/Map/Authoring/형광등 — 씬 전체 다시 조립")]
    static void RebuildAll()
    {
        if (!CanEdit()) return;
        var scene = EditorSceneManager.GetActiveScene();
        var units = new List<Transform>();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (UnitName.IsMatch(t.name) && !InsideUnit(t.parent)) units.Add(t);
        if (units.Count == 0) { Debug.LogWarning($"{Tag} 활성 씬 '{scene.name}' 에 형광등 묶음(_Fluorescent)이 없다."); return; }

        bool streak = StreakOn;
        int lights = LightCountSetting;
        if (lights == 0 && !streak) { Debug.LogError($"{Tag} 라이트 0개 + 빛 줄기 꺼짐이면 형광등이 비어 버린다 — 둘 중 하나를 켜고 실행."); return; }
        int ok = 0, skipped = 0;
        foreach (Transform u in units) { if (Rebuild(u, streak, lights)) ok++; else skipped++; }
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"{Tag} 씬 전체 다시 조립: {ok}개{(skipped > 0 ? $", 건너뜀 {skipped}개(프리팹 인스턴스 소속)" : "")} — 라이트 {lights}개씩(총 {ok * lights}개), 바닥 빛 줄기 {(streak ? "켬" : "끔")}, 막대 메시 없음.");
    }

    static bool CanEdit()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode) return true;
        Debug.LogError($"{Tag} Play 모드를 먼저 정지하고 실행.");
        return false;
    }

    static bool InsideUnit(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
            if (UnitName.IsMatch(p.name)) return true;
        return false;
    }

    static Transform FindOrCreateSibling(Transform t, string name)
    {
        Transform found = null;
        if (t.parent != null) found = t.parent.Find(name);
        else foreach (GameObject r in t.gameObject.scene.GetRootGameObjects()) if (r.name == name) { found = r.transform; break; }
        if (found != null) return found;

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Fluorescent");
        Undo.SetTransformParent(go.transform, t.parent, "Fluorescent");
        if (t.parent == null && go.scene != t.gameObject.scene)
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, t.gameObject.scene);
        go.transform.SetPositionAndRotation(t.position, t.rotation);
        return go.transform;
    }

    static Transform CreateChild(Transform parent, string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Fluorescent");
        Undo.SetTransformParent(go.transform, parent, "Fluorescent");
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    // ───────── 조립 ─────────

    static bool IsPartName(string n) =>
        n == "Housing" || n == "Streak" || n.StartsWith("Cap_") || n.StartsWith("Tube_") || n.StartsWith("Light_");

    /// <summary>묶음의 이전 부품(예전 막대 메시 포함)을 지우고 지금 설정으로 다시 만든다. 프리팹 인스턴스 소속이면 false.</summary>
    static bool Rebuild(Transform unit, bool streak, int lightCount)
    {
        var doomed = new List<GameObject>();
        for (int c = 0; c < unit.childCount; c++)
            if (IsPartName(unit.GetChild(c).name)) doomed.Add(unit.GetChild(c).gameObject);
        foreach (GameObject g in doomed)
            if (PrefabUtility.IsPartOfPrefabInstance(g))
            {
                Debug.LogWarning($"{Tag} '{unit.name}/{g.name}' 는 프리팹 인스턴스 소속이라 다시 조립하지 않음 — 프리팹에서 직접 고칠 것.");
                return false;
            }
        foreach (GameObject g in doomed) Undo.DestroyObjectImmediate(g);

        for (int k = 0; k < lightCount; k++)
        {
            var go = new GameObject("Light_" + k);
            Undo.RegisterCreatedObjectUndo(go, "Fluorescent");
            go.transform.SetParent(unit, false);
            go.transform.localPosition = new Vector3((k - (lightCount - 1) * 0.5f) * LightSpacing, -LightDrop, 0f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = LightColor;
            l.intensity = lightCount == 1 ? LightIntensitySingle : LightIntensityEach;
            l.range = lightCount == 1 ? LightRangeSingle : LightRange;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.Auto;
            l.lightmapBakeType = LightmapBakeType.Realtime;
        }

        if (streak)
        {
            GameObject s = GameObject.CreatePrimitive(PrimitiveType.Quad);
            s.name = "Streak";
            Object.DestroyImmediate(s.GetComponent<Collider>());
            Undo.RegisterCreatedObjectUndo(s, "Fluorescent");
            s.transform.SetParent(unit, false);
            // 바닥에 눕힌다(쿼드 앞면 −Z → X 축 90° 로 위를 향함). 길이 = 형광등 방향(묶음 로컬 X).
            s.transform.SetPositionAndRotation(
                new Vector3(unit.position.x, FloorTopY + StreakLift, unit.position.z),
                Quaternion.Euler(90f, unit.eulerAngles.y, 0f));
            Vector3 ls = unit.lossyScale;
            // 부모가 거울 반전(음수 스케일)이어도 크기가 맞게 절댓값으로 나눈다(쿼드·텍스처가 대칭이라 부호는 상관없다)
            s.transform.localScale = new Vector3(StreakLength / Mathf.Max(0.0001f, Mathf.Abs(ls.x)), StreakWidth / Mathf.Max(0.0001f, Mathf.Abs(ls.z)), 1f);
            var mr = s.GetComponent<MeshRenderer>();
            mr.sharedMaterial = EnsureStreakMaterial();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
        return true;
    }

    // ───────── 바닥 빛 줄기 에셋 ─────────

    /// <summary>길쭉하고 부드러운 빛 모양(가운데 평평·양 끝 부드럽게·폭 방향 가우스). RGB 흰색, 알파 = 세기.</summary>
    static Texture2D EnsureStreakTexture()
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(StreakTexPath);
        if (tex != null) return tex;
        EnsureFolder();
        const int w = 256, h = 64;
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = Mathf.Abs((x + 0.5f) / w * 2f - 1f), v = (y + 0.5f) / h * 2f - 1f;
                float along = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, u));
                float across = Mathf.Exp(-(v * v) / (2f * 0.3f * 0.3f));
                t.SetPixel(x, y, new Color(1f, 1f, 1f, along * across));
            }
        t.Apply();
        File.WriteAllBytes(Path.GetFullPath(StreakTexPath), t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(StreakTexPath, ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(StreakTexPath) is TextureImporter ti)
        {
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = false;       // 알파 = 세기 그대로
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(StreakTexPath);
    }

    /// <summary>URP Unlit 가산(Additive) 투명 — URP 머티리얼 검증이 돌아도 같은 설정이 되도록 _Surface/_Blend 와 블렌드 상태를 맞춘다.</summary>
    static Material EnsureStreakMaterial()
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(StreakMatPath);
        if (m == null)
        {
            EnsureFolder();
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(m, StreakMatPath);
        }
        m.SetTexture("_BaseMap", EnsureStreakTexture());
        m.SetColor("_BaseColor", StreakColor);
        m.SetFloat("_Surface", 1f);   // Transparent
        m.SetFloat("_Blend", 2f);     // Additive
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.One);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssetIfDirty(m);
        return m;
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder(MatFolderParent, "Lighting");
    }

    // ───────── 캡처 ─────────

    [MenuItem("Tools/Map/Authoring/조명 캡처 (게임 카메라 각도)")]
    static void CaptureSelected()
    {
        Transform t = Selection.activeTransform;
        Vector3 p = t != null ? t.position : (SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero);
        Capture(new Vector3(p.x, FloorTopY, p.z), t != null ? Safe(t.name) : "pivot");
    }

    /// <summary>'전' 사진: 그 묶음을 잠깐 숨기고 찍는다(Undo 기록 없이 바로 되돌림).</summary>
    static void CaptureHidden(Transform unit, Vector3 floor, string name)
    {
        bool hide = unit.gameObject.activeSelf && unit.childCount > 0;
        if (hide) unit.gameObject.SetActive(false);
        try { Capture(floor, name); }
        finally { if (hide) unit.gameObject.SetActive(true); }
    }

    /// <summary>게임 카메라 각도·후처리 그대로 오프스크린 렌더 → Temp/LightingCapture/&lt;name&gt;.png. 경로를 돌려준다.</summary>
    static string Capture(Vector3 target, string name)
    {
        const int w = 1600, h = 900;
        var go = new GameObject("~LightingCapture") { hideFlags = HideFlags.HideAndDontSave };
        RenderTexture rt = null, hdr = null;
        Texture2D tex = null;
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = CamFov;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 300f;
            cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.Skybox;
            go.transform.SetPositionAndRotation(target + CamOffset, Quaternion.Euler(CamEuler));
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            // URP 는 대상 텍스처 형식을 따라간다 — 8비트로 렌더하면 HDR 이 꺼져 1.0 에서 잘리고 블룸(threshold 1.1)이 안 생긴다.
            // 게임 화면처럼 HDR 텍스처에 렌더한 뒤 sRGB 8비트로 옮긴다(Blit 이 선형→sRGB 변환).
            hdr = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            cam.targetTexture = hdr;
            var req = new RenderPipeline.StandardRequest();
            if (RenderPipeline.SupportsRenderRequest(cam, req)) { req.destination = hdr; RenderPipeline.SubmitRenderRequest(cam, req); }
            else cam.Render();

            rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(hdr, rt);

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/LightingCapture"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, name + ".png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Debug.Log($"{Tag} 캡처: {file}");
            return file;
        }
        finally
        {
            if (go.TryGetComponent(out Camera c)) c.targetTexture = null;
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            if (hdr != null) { hdr.Release(); Object.DestroyImmediate(hdr); }
            if (tex != null) Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
        }
    }

    static string Safe(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }
}
