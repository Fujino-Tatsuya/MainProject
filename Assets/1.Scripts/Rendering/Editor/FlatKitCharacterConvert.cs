using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 캐릭터 프리팹의 몸 머티리얼을 Flat Kit(Stylized Surface)으로 바꾼다 (PLAN-flatkit.md 4·7단계 · 2026-09-30).
//
// - 원본(SVN 50.Art · FBX 내장 · 3.Materials/Toon)은 **건드리지 않는다.** 원본 1장당 git 쪽
//   Assets/3.Materials/FlatKit/<그룹>/FK_*.mat 1장을 만들고, 프리팹(git)의 렌더러 슬롯만 갈아 끼운다.
//   중첩 프리팹·FBX 안의 렌더러면 루트 프리팹에 머티리얼 오버라이드로 저장된다.
// - 원본 → FK 대응은 FK 머티리얼 임포터의 userData(원본 GUID/fileID)로 기억한다 — 다시 돌려도 새로 만들지 않고
//   같은 FK 를 쓴다. 이름이 같은 다른 원본(M_PeekABot_01 Pack01·Pack02)도 갈라진다.
// - 슬롯 순서는 그대로 둔다 — DissolveOverlay 가 슬롯 번호를 고정으로 들고 있다.
// - 룩 값은 FlatKitCharacterLook 한 곳에서 온다.
public static class FlatKitCharacterConvert
{
    const string FlatKitShaderName = "FlatKit/Stylized Surface";
    const string OutRoot = "Assets/3.Materials/FlatKit";

    // Wells 를 23호보다 먼저 — 23호 안의 Wells 는 중첩 프리팹이라 Wells.prefab 을 먼저 바꾸면 그대로 물려받는다.
    static readonly (string prefab, string group)[] Targets =
    {
        ("Assets/2.Prefabs/Monster/ChompBot.prefab", "Monster"),
        ("Assets/2.Prefabs/Monster/GauntletBot.prefab", "Monster"),
        ("Assets/2.Prefabs/Monster/HumanoidBot.prefab", "Monster"),
        ("Assets/2.Prefabs/Monster/MortarBot.prefab", "Monster"),
        ("Assets/2.Prefabs/Monster/PeekABot.prefab", "Monster"),
        ("Assets/2.Prefabs/Monster/SpinnerBot.prefab", "Monster"),
        ("Assets/2.Prefabs/Monster/TeslaBot.prefab", "Monster"),
        ("Assets/2.Prefabs/Monster/WallBot.prefab", "Monster"),
        ("Assets/2.Prefabs/Monster/Boss/Wells.prefab", "Boss"),
        ("Assets/2.Prefabs/Monster/Boss/TwentyThree.prefab", "Boss"),
        ("Assets/2.Prefabs/Monster/Boss/TwentyThree_Solo.prefab", "Boss"),
        // 플레이어(6단계). 실제 스폰 = Paladin(NetworkManager defaultPlayerPrefab). Player = 역할 프리팹(설계상 Armature 교체),
        // Paladin_VFX = 이펙트 작업용 복제본 — 셋 다 같은 룩이어야 이펙트 작업 화면이 인게임과 같다.
        ("Assets/2.Prefabs/Player/Paladin/Paladin.prefab", "Player"),
        ("Assets/2.Prefabs/Player/Paladin/Paladin_VFX.prefab", "Player"),
        ("Assets/2.Prefabs/Player/Player.prefab", "Player"),
    };

    // 몸이 아닌 것: 예고 데칼·방향 표시·레이저·디졸브/인터럽트 겹·VFX. 셰이더 이름과 경로로 거른다.
    static bool IsBodyMaterial(Material m)
    {
        if (m == null || m.shader == null) return false;
        string sh = m.shader.name;
        if (sh == FlatKitShaderName || sh == FlatKitCharacterLook.SkinnedOutlineShader) return false;   // 이미 전환됨
        bool bodyShader = sh.StartsWith("Universal Render Pipeline/Lit") || sh.StartsWith("Universal Render Pipeline/Simple Lit")
                          || sh.StartsWith("Universal Render Pipeline/Unlit") || sh == "Project/ToonLit";
        if (!bodyShader) return false;
        string path = AssetDatabase.GetAssetPath(m);
        if (path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) return true;   // FBX 내장
        return path.StartsWith("Assets/50.Art/Char/") || path.StartsWith("Assets/3.Materials/Toon/");
    }

    static bool IsTransparent(Material m) =>
        (m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f) || m.renderQueue >= 2500;

    static Material SourceOf(Material fk)
    {
        var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(fk));
        if (imp == null || !imp.userData.StartsWith("fkSource=")) return null;
        string[] k = imp.userData.Substring("fkSource=".Length).Split(':');
        string path = AssetDatabase.GUIDToAssetPath(k[0]);
        long id = long.Parse(k[1]);
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is Material mm && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mm, out _, out long fid) && fid == id) return mm;
        return null;
    }

    // [검증] 대상 프리팹의 실제 렌더러 슬롯을 전부 찍는다 — 파일 스캔으로는 중첩 원본에 남은 참조와 구분이 안 된다.
    [MenuItem("Tools/Rendering/Flat Kit/Verify Characters")]
    public static void VerifyAll()
    {
        foreach (var (prefabPath, _) in Targets)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var sb = new System.Text.StringBuilder($"[FlatKitVerify] {System.IO.Path.GetFileNameWithoutExtension(prefabPath)}\n");
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        Material m = mats[i];
                        string flag = m == null ? "NULL" : (m.shader.name == FlatKitShaderName || m.shader.name == FlatKitCharacterLook.SkinnedOutlineShader) ? "FK" : IsBodyMaterial(m) ? "⚠원본" : "-";
                        sb.Append($"   {flag,-5} {r.name}[{i}] {(m ? m.name : "null")} ({(m ? m.shader.name : "")}) active={r.gameObject.activeInHierarchy}\n");
                    }
                }
                Debug.Log(sb.ToString());
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    [MenuItem("Tools/Rendering/Flat Kit/Convert Characters (Monsters · 23 · Wells · Player)")]
    public static void ConvertAll()
    {
        var active = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
        if (active == null || AssetDatabase.GetAssetPath(active) != "Assets/99.Settings/PC_RPAsset.asset")
        {
            Debug.LogError("[FlatKitConvert] 활성 URP 에셋이 PC_RPAsset 이 아니다 — 먼저 Restore Project Pipeline.");
            return;
        }

        var map = LoadExistingMap();
        var log = new List<string>();
        int swapped = 0;

        foreach (var (prefabPath, group) in Targets)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            int here = 0;
            try
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                    Material[] mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (!IsBodyMaterial(mats[i])) continue;
                        Material fk = GetOrCreate(mats[i], group, map, log);
                        if (fk == null) continue;
                        mats[i] = fk;
                        changed = true;
                        here++;
                    }
                    if (changed) r.sharedMaterials = mats;
                }
                if (here > 0) PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            log.Add($"[FlatKitConvert] {System.IO.Path.GetFileNameWithoutExtension(prefabPath)} — 슬롯 {here}개 교체");
            swapped += here;
        }

        FlatKitCharacterLook.ApplyAll();   // 새로 만든 것까지 표준 룩 · 외곽선 피처 등록
        AssetDatabase.SaveAssets();
        foreach (string l in log) Debug.Log(l);
        Debug.Log($"[FlatKitConvert] 완료 — 슬롯 {swapped}개 · FK 머티리얼 {map.Count}개");
    }

    static string SourceKey(Material src)
    {
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(src, out string guid, out long fileId);
        return $"{guid}:{fileId}";
    }

    static Dictionary<string, Material> LoadExistingMap()
    {
        var map = new Dictionary<string, Material>();
        foreach (string g in AssetDatabase.FindAssets("t:Material", new[] { OutRoot }))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var imp = AssetImporter.GetAtPath(p);
            if (imp != null && imp.userData.StartsWith("fkSource="))
                map[imp.userData.Substring("fkSource=".Length)] = AssetDatabase.LoadAssetAtPath<Material>(p);
        }
        return map;
    }

    static Material GetOrCreate(Material src, string group, Dictionary<string, Material> map, List<string> log)
    {
        string key = SourceKey(src);
        if (map.TryGetValue(key, out Material existing) && existing != null)
        {
            CopySurface(src, existing);   // 이전 실행에서 불투명으로 만든 것도 바로잡는다
            return existing;
        }

        string dir = $"{OutRoot}/{group}";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(OutRoot, group);

        string srcPath = AssetDatabase.GetAssetPath(src);
        string baseName = src.name.StartsWith("M_") ? src.name.Substring(2) : src.name;
        if (srcPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
            baseName = $"{System.IO.Path.GetFileNameWithoutExtension(srcPath)}_{src.name}";
        string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/FK_{Sanitize(baseName)}.mat");

        var fk = new Material(Shader.Find(FlatKitShaderName));
        fk.SetColor("_BaseColor", src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : Color.white);
        Texture tex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : null;
        if (tex == null && src.HasProperty("_MainTex")) tex = src.GetTexture("_MainTex");
        fk.SetTexture("_BaseMap", tex);
        if (src.HasProperty("_BaseMap")) { fk.SetTextureScale("_BaseMap", src.GetTextureScale("_BaseMap")); fk.SetTextureOffset("_BaseMap", src.GetTextureOffset("_BaseMap")); }

        // 이미시브(눈·램프): 원본이 켜 둔 경우만 옮긴다.
        if (src.IsKeywordEnabled("_EMISSION") && src.HasProperty("_EmissionColor"))
        {
            fk.SetColor("_EmissionColor", src.GetColor("_EmissionColor"));
            if (src.HasProperty("_EmissionMap")) fk.SetTexture("_EmissionMap", src.GetTexture("_EmissionMap"));
            fk.EnableKeyword("_EMISSION");
        }
        else if (fk.HasProperty("_EmissionColor"))
        {
            fk.SetColor("_EmissionColor", Color.black);
        }

        CopySurface(src, fk);
        AssetDatabase.CreateAsset(fk, path);
        var imp = AssetImporter.GetAtPath(path);
        imp.userData = "fkSource=" + key;
        imp.SaveAndReimport();

        fk = AssetDatabase.LoadAssetAtPath<Material>(path);
        map[key] = fk;
        log.Add($"[FlatKitConvert]   + {path}  ← {srcPath} ({src.shader.name}){(tex == null ? "  ⚠ 텍스처 없음" : "")}");
        return fk;
    }

    // 🔴 투명 원본(PeekABot 안테나 알파 0.7 · SpinnerBot 날개 잔상 · Wells 보호유리 0.3)은 Flat Kit 도 투명으로.
    //    Flat Kit 인스펙터(StylizedSurfaceEditor 의 Surface Type = Transparent · Blend = Alpha)와 같은 값을 쓴다.
    static void CopySurface(Material src, Material fk)
    {
        if (!IsTransparent(src)) return;
        fk.SetFloat("_Surface", 1f);
        fk.SetFloat("_Blend", 0f);   // Alpha
        fk.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        fk.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        fk.SetInt("_ZWrite", 0);
        fk.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        fk.SetOverrideTag("RenderType", "Transparent");
        fk.renderQueue = src.renderQueue >= 2500 ? src.renderQueue : (int)UnityEngine.Rendering.RenderQueue.Transparent;
        fk.SetShaderPassEnabled("ShadowCaster", false);
        if (src.HasProperty("_BaseColor")) fk.SetColor("_BaseColor", src.GetColor("_BaseColor"));   // 알파 포함
        EditorUtility.SetDirty(fk);
    }

    static string Sanitize(string s)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }
}
