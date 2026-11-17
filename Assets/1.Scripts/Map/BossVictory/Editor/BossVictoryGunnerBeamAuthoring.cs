using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 302 — 처치 연출 전용 거너 레이저 뼈대 이펙트를 <c>Assets/5.VFX/BossVictory/Gunner/</c> 에 만든다.
/// 메시 2개(캡슐 — 옆면 +Z 1m + 셰이더가 둥글게 미는 반구 · 빌보드 판) · 머티리얼 3개(코어·글로우·플레어) · 프리팹 FX_BossVictoryGunnerBeam(BossVictoryBeamRig — Body·Muzzle·End)
/// · 거너 프로필 gunnerQ.victoryBeamPrefab 이 비어 있으면 연결. <b>이미 있는 파일은 건드리지 않는다</b>(은희가 고친 이펙트를 덮지 않게 — 다시 만들려면 지우고 실행).
/// </summary>
public static class BossVictoryGunnerBeamAuthoring
{
    const string Folder = "Assets/5.VFX/BossVictory/Gunner";
    const string ShaderPath = Folder + "/BossVictoryGunnerBeam.shader";
    const string CapsulePath = Folder + "/Mesh_BossVictoryGunnerBeam_Capsule.asset";
    const string QuadPath = Folder + "/Mesh_BossVictoryGunnerBeam_Quad.asset";
    const string CoreMatPath = Folder + "/M_BossVictoryGunnerBeam_Core.mat";
    const string GlowMatPath = Folder + "/M_BossVictoryGunnerBeam_Glow.mat";
    const string FlareMatPath = Folder + "/M_BossVictoryGunnerBeam_Flare.mat";
    const string BoltsPath = Folder + "/Mesh_BossVictoryGunnerBeam_Bolts.asset";
    const string WispMatPath = Folder + "/M_BossVictoryGunnerBeam_Wisp.mat";
    const string LightningMatPath = Folder + "/M_BossVictoryGunnerBeam_Lightning.mat";
    const string PrefabPath = Folder + "/FX_BossVictoryGunnerBeam.prefab";
    const string GunnerProfilePath = "Assets/Resources/BossVictory/Profiles/BossVictoryProfile_Gunner.asset";

    [MenuItem("Tools/Boss Victory/거너 레이저 뼈대 이펙트 만들기 (302)")]
    public static void Create()
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null)
        {
            Debug.LogError($"[302] 셰이더가 없습니다: {ShaderPath}");
            return;
        }

        Mesh capsule = LoadOrCreate(CapsulePath, () => BuildCapsule(32, 8));
        Mesh quad = LoadOrCreate(QuadPath, BuildQuad);
        Material core = LoadOrCreate(CoreMatPath, () => MakeMaterial(shader, "M_BossVictoryGunnerBeam_Core", false,
            new Color(6f, 6f, 6f, 1f), new Color(1.5f, 3.5f, 7f, 1f), corePower: 3f, glowPower: 1.5f, noise: 0.25f, opacity: 1f, startFade: 0f, endFade: 0f).Streak(0.35f, 0.5f, 2f, 0.4f, 0.2f, 0.8f));
        Material glow = LoadOrCreate(GlowMatPath, () => MakeMaterial(shader, "M_BossVictoryGunnerBeam_Glow", false,
            Color.black, new Color(0.6f, 1.6f, 4f, 1f), corePower: 8f, glowPower: 1f, noise: 0.6f, opacity: 0.5f, startFade: 0f, endFade: 0f).Streak(0.8f, 0.35f, 2.5f, 0.5f, 0.15f, 0.55f).Lines(1f, new Color(0.02f, 0.12f, 0.7f, 1f)));
        Material flare = LoadOrCreate(FlareMatPath, () => MakeMaterial(shader, "M_BossVictoryGunnerBeam_Flare", true,
            new Color(1.5f, 1.5f, 1.5f, 1f), new Color(0.3f, 0.7f, 1.6f, 1f), corePower: 10f, glowPower: 3f, noise: 0f, opacity: 0f, startFade: 0f, endFade: 0f));
        Mesh bolts = LoadOrCreate(BoltsPath, () => BuildBolts(4, 65));
        Material wisp = LoadOrCreate(WispMatPath, MakeWisp);
        Material lightning = LoadOrCreate(LightningMatPath, MakeLightning);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            // 열린 씬을 dirty 로 만들지 않게 미리보기 씬에서 조립.
            UnityEngine.SceneManagement.Scene preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var root = new GameObject("FX_BossVictoryGunnerBeam");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                Transform body = Child(root.transform, "Body", Vector3.zero, Vector3.one);
                AddMesh(Child(body, "Core", Vector3.zero, new Vector3(0.3f, 0.3f, 1f)), capsule, core);
                AddMesh(Child(body, "Glow", Vector3.zero, new Vector3(1.1f, 1.1f, 1f)), capsule, glow);
                AddLayers(body, capsule, wisp, bolts, lightning);
                Transform muzzle = Child(root.transform, "Muzzle", Vector3.zero, Vector3.one);
                AddMesh(Child(muzzle, "Flare", Vector3.zero, Vector3.one * 0.8f), quad, flare);
                Transform end = Child(root.transform, "End", Vector3.zero, Vector3.one);
                AddMesh(Child(end, "Flare", Vector3.zero, Vector3.one * 1f), quad, flare);

                BossVictoryBeamRig rig = root.AddComponent<BossVictoryBeamRig>();
                var so = new SerializedObject(rig);
                so.FindProperty("body").objectReferenceValue = body;
                so.FindProperty("muzzle").objectReferenceValue = muzzle;
                so.FindProperty("end").objectReferenceValue = end;
                so.ApplyModifiedPropertiesWithoutUndo();
                WireLayers(rig, body);

                prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[302] 프리팹 생성: {PrefabPath}", prefab);
            }
            finally
            {
                Object.DestroyImmediate(root);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
            }
        }
        else
        {
            // 이미 있는 프리팹 — Body 에 결(Wisp)·번개(Lightning) 층이 없을 때만 덧붙이고, 리그 층별 굵기 칸이 비었을 때만 연결(나머지는 그대로).
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform body = contents.transform.Find("Body");
                bool added = body != null && AddLayers(body, capsule, wisp, bolts, lightning);
                bool wired = body != null && WireLayers(contents.GetComponent<BossVictoryBeamRig>(), body);
                if (added || wired)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                    Debug.Log($"[302] 프리팹 갱신(결·번개 층 추가 {added} · 층별 굵기 연결 {wired}): {PrefabPath}", prefab);
                }
                else Debug.Log($"[302] 프리팹이 이미 있어 그대로 둡니다: {PrefabPath}", prefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        var profile = AssetDatabase.LoadAssetAtPath<BossVictoryProfile>(GunnerProfilePath);
        if (profile != null && profile.gunnerQ != null && profile.gunnerQ.victoryBeamPrefab == null && prefab != null)
        {
            Undo.RecordObject(profile, "거너 처치 연출 레이저 이펙트 연결 (302)");
            profile.gunnerQ.victoryBeamPrefab = prefab;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            Debug.Log("[302] 거너 프로필 gunnerQ.victoryBeamPrefab 연결", profile);
        }
        AssetDatabase.SaveAssets();
    }

    static T LoadOrCreate<T>(string path, System.Func<T> make) where T : Object
    {
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) return existing;
        T made = make();
        AssetDatabase.CreateAsset(made, path);
        return made;
    }

    static Material MakeMaterial(Shader shader, string name, bool billboard, Color coreColor, Color glowColor, float corePower, float glowPower, float noise, float opacity, float startFade, float endFade)
    {
        var m = new Material(shader) { name = name };
        m.SetColor("_CoreColor", coreColor);
        m.SetColor("_GlowColor", glowColor);
        m.SetFloat("_CorePower", corePower);
        m.SetFloat("_GlowPower", glowPower);
        m.SetFloat("_NoiseAmount", noise);
        m.SetFloat("_Opacity", opacity);
        m.SetFloat("_StartFade", startFade);
        m.SetFloat("_EndFade", endFade);
        m.SetFloat("_Billboard", billboard ? 1f : 0f);
        if (billboard) m.EnableKeyword("_BILLBOARD");
        return m;
    }

    /// <summary>결 노이즈 칸(캡슐 모드) — 세기·길이 방향 촘촘함·둘레 촘촘함·깎는 기준·부드러움·가운데 남김.</summary>
    static Material Streak(this Material m, float amount, float scale, float around, float cut, float soft, float keep)
    {
        m.SetFloat("_StreakAmount", amount);
        m.SetFloat("_StreakScale", scale);
        m.SetFloat("_StreakAround", around);
        m.SetFloat("_StreakCut", cut);
        m.SetFloat("_StreakSoft", soft);
        m.SetFloat("_StreakKeep", keep);
        return m;
    }

    /// <summary>만화 선 칸(캡슐 모드) — 세기·색 + 진한 먹선 기본(가림 0.9·둘레 9칸·굵기 0.35·거칠기 1·획 3m). 흰 몸통 위 흰 선은 안 보여 진한 파란 선으로 둔다.</summary>
    static Material Lines(this Material m, float amount, Color color)
    {
        m.SetFloat("_LineAmount", amount);
        m.SetColor("_LineColor", color);
        m.SetFloat("_LineCover", 0.9f);
        m.SetFloat("_LineCount", 9f);
        m.SetFloat("_LineWidth", 0.35f);
        m.SetFloat("_LineRough", 1f);
        m.SetFloat("_LineFill", 0.55f);
        m.SetFloat("_LineLength", 3f);
        return m;
    }

    /// <summary>바깥 연기 결 — 캡슐, 길게 늘인 결 노이즈로 가장자리를 깎는다(흰빛 연기).</summary>
    static Material MakeWisp()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        Material m = MakeMaterial(shader, "M_BossVictoryGunnerBeam_Wisp", false,
            Color.black, new Color(0.9f, 1.05f, 1.5f, 1f), corePower: 8f, glowPower: 1.6f, noise: 0.5f, opacity: 0.25f, startFade: 0f, endFade: 0f).Streak(1f, 0.25f, 2.2f, 0.6f, 0.12f, 0.25f);
        m.SetFloat("_StreakFlow", 0.8f);
        return m;
    }

    /// <summary>3D 번개 — 축 둘레 공간 꺾은선을 따라 가는 줄기(가산).</summary>
    static Material MakeLightning()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        var m = new Material(shader) { name = "M_BossVictoryGunnerBeam_Lightning" };
        m.SetColor("_CoreColor", new Color(4f, 6f, 10f, 1f));
        m.SetColor("_GlowColor", new Color(0.25f, 0.6f, 1.8f, 1f));
        m.SetFloat("_Opacity", 0f);
        m.SetFloat("_PulseAmount", 0f);
        m.SetFloat("_Lightning", 1f);
        m.EnableKeyword("_LIGHTNING");
        return m;
    }

    /// <summary>Body 에 결(Wisp)·번개(Lightning) 층이 없으면 덧붙인다. 하나라도 붙였으면 true.</summary>
    static bool AddLayers(Transform body, Mesh capsule, Material wisp, Mesh bolts, Material lightning)
    {
        bool added = false;
        if (body.Find("Wisp") == null)
        {
            AddMesh(Child(body, "Wisp", Vector3.zero, new Vector3(1.7f, 1.7f, 1f)), capsule, wisp);
            added = true;
        }
        if (body.Find("Lightning") == null)
        {
            AddMesh(Child(body, "Lightning", Vector3.zero, new Vector3(3f, 3f, 1f)), bolts, lightning);
            added = true;
        }
        return added;
    }

    /// <summary>리그 층별 굵기 칸 — 안쪽 몸통 = Body/Core·Glow, 바깥 공기막 = Body/Wisp. 칸이 비었을 때만 채운다. 채웠으면 true.</summary>
    static bool WireLayers(BossVictoryBeamRig rig, Transform body)
    {
        if (rig == null) return false;
        var so = new SerializedObject(rig);
        bool changed = Fill(so.FindProperty("innerBody"), body, "Core", "Glow") | Fill(so.FindProperty("outerAir"), body, "Wisp");
        if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        return changed;
    }

    static bool Fill(SerializedProperty list, Transform body, params string[] names)
    {
        if (list == null || list.arraySize > 0) return false;
        int n = 0;
        foreach (string name in names)
        {
            Transform t = body.Find(name);
            if (t == null) continue;
            list.InsertArrayElementAtIndex(n);
            list.GetArrayElementAtIndex(n++).objectReferenceValue = t;
        }
        return n > 0;
    }

    static Transform Child(Transform parent, string name, Vector3 localPos, Vector3 localScale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        return go.transform;
    }

    static void AddMesh(Transform t, Mesh mesh, Material mat)
    {
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = t.gameObject.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    /// <summary>
    /// 캡슐 — 옆면 축 +Z, z∈[0,1], 반지름 0.5 + 양 끝 반구. 반구 정점은 z 0·1 에 붙어 있고 UV2.x(늘림 비율 −1~1)로 셰이더가 실제 반지름만큼 민다
    /// (몸통 Z 스케일 = 길이라 메시로 반구를 만들면 길쭉한 타원이 된다). 법선 = 실제 캡슐 법선, UV.x = 길이(0 = 총구 쪽 끝, 1 = 끝 쪽),
    /// UV.y = 둘레 0~1(이음매 정점 복제), 정점 색 흰색. 경계 상자는 반구가 나가는 만큼 z 로 넓힌다.
    /// </summary>
    public static Mesh BuildCapsule(int segments, int capRings)
    {
        segments = Mathf.Max(3, segments);
        capRings = Mathf.Max(1, capRings);
        int ring = segments + 1;
        // 고리 순서 = +Z 로 갈수록: 총구 반구(극 → 적도) · 끝 반구(적도 → 극). 적도 고리 2개 사이 = 옆면.
        int rings = (capRings + 1) * 2;
        var vertices = new Vector3[ring * rings];
        var normals = new Vector3[ring * rings];
        var uvs = new Vector2[ring * rings];
        var uv2 = new Vector2[ring * rings];
        var colors = new Color[ring * rings];
        for (int r = 0; r < rings; r++)
        {
            bool endCap = r > capRings;
            int k = endCap ? r - (capRings + 1) : capRings - r;   // 0 = 적도 · capRings = 극
            float lat = (float)k / capRings * Mathf.PI * 0.5f;
            float c = Mathf.Cos(lat), s = Mathf.Sin(lat) * (endCap ? 1f : -1f);
            for (int i = 0; i < ring; i++)
            {
                float around = (float)i / segments;
                float a = around * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                int v = r * ring + i;
                vertices[v] = dir * (0.5f * c) + (endCap ? Vector3.forward : Vector3.zero);
                normals[v] = dir * c + new Vector3(0f, 0f, s);
                uvs[v] = new Vector2(endCap ? 1f : 0f, around);
                uv2[v] = new Vector2(s, 0f);
                colors[v] = Color.white;
            }
        }
        var triangles = new int[(rings - 1) * segments * 6];
        int t = 0;
        for (int r = 0; r < rings - 1; r++)
            for (int i = 0; i < segments; i++)
            {
                int b0 = r * ring + i, b1 = b0 + 1, t0 = b0 + ring, t1 = t0 + 1;
                // 바깥에서 보이는 면이 앞면(Unity 시계 방향) — 각도가 +Z 축 기준 반시계(+X → +Y)로 늘어난다.
                triangles[t++] = b0; triangles[t++] = b1; triangles[t++] = t0;
                triangles[t++] = b1; triangles[t++] = t1; triangles[t++] = t0;
            }
        var mesh = new Mesh { name = "Mesh_BossVictoryGunnerBeam_Capsule" };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.uv2 = uv2;
        mesh.colors = colors;
        mesh.triangles = triangles;
        mesh.bounds = new Bounds(new Vector3(0f, 0f, 0.5f), new Vector3(1f, 1f, 3f));
        return mesh;
    }

    /// <summary>빌보드 판 — xy ±0.5, UV 0~1, 정점 색 흰색. 셰이더가 카메라를 향해 펴므로 경계 상자를 정육면체로 넓혀 둔다(잘림 방지).</summary>
    public static Mesh BuildQuad()
    {
        var mesh = new Mesh { name = "Mesh_BossVictoryGunnerBeam_Quad" };
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
        return mesh;
    }

    /// <summary>3D 번개 — 줄기 <paramref name="bolts"/> 개 × 축 따라 점 <paramref name="points"/> 개(점마다 좌우 정점 2개) 띠.
    /// 정점 위치 = 축 위 (0, 0, t) — 셰이더가 축 둘레 꺾은선 위로 옮기고 선의 진행 방향·시선에 수직으로 편다.
    /// UV.x = 길이 t(0~1), UV.y = 좌 0 · 우 1, UV2.x = 줄기 번호. 경계 상자는 꺾은선이 나가는 만큼 x·y 로 넓혀 둔다.</summary>
    public static Mesh BuildBolts(int bolts, int points)
    {
        bolts = Mathf.Max(1, bolts);
        points = Mathf.Max(2, points);
        int perBolt = points * 2;
        var vertices = new Vector3[bolts * perBolt];
        var uvs = new Vector2[vertices.Length];
        var uv2 = new Vector2[vertices.Length];
        var colors = new Color[vertices.Length];
        var normals = new Vector3[vertices.Length];
        var triangles = new int[bolts * (points - 1) * 6];
        int k = 0;
        for (int b = 0; b < bolts; b++)
        {
            int baseV = b * perBolt;
            for (int i = 0; i < points; i++)
            {
                float t = (float)i / (points - 1);
                for (int s = 0; s < 2; s++)
                {
                    int v = baseV + i * 2 + s;
                    vertices[v] = new Vector3(0f, 0f, t);
                    uvs[v] = new Vector2(t, s);
                    uv2[v] = new Vector2(b, 0f);
                    colors[v] = Color.white;
                    normals[v] = Vector3.up;
                }
                if (i == points - 1) continue;
                int l0 = baseV + i * 2, r0 = l0 + 1, l1 = l0 + 2, r1 = l0 + 3;
                // 셰이더: 우(UV.y 1) = +옆 = cross(진행 방향, 카메라 방향) → 면 법선 cross(옆, 진행)이 카메라 쪽 = 이 감는 방향이 앞면(Cull Back).
                triangles[k++] = l0; triangles[k++] = r0; triangles[k++] = l1;
                triangles[k++] = r0; triangles[k++] = r1; triangles[k++] = l1;
            }
        }
        var mesh = new Mesh { name = "Mesh_BossVictoryGunnerBeam_Bolts" };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.uv2 = uv2;
        mesh.colors = colors;
        mesh.triangles = triangles;
        mesh.bounds = new Bounds(new Vector3(0f, 0f, 0.5f), new Vector3(1.6f, 1.6f, 1.2f));
        return mesh;
    }
}
