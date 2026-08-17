using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VeyTrace.Rendering.Occlusion;

// 벽 투명화 저작 도구.
//
// 소스 머티리얼(SVN 관리, 개별 Shader Graph)을 건드리지 않고, 같은 룩을 흉내내는
// 디더 셰이더 변종을 Git 쪽에 만들어 설정에 매핑한다. 런타임에는 WallOcclusionDriver가
// 이 매핑으로 머티리얼만 바꿔 끼운다.
//
// 주의: 변종은 원본 Shader Graph의 근사치다. 원본이 쓰는 이미시브·디테일맵 등은 재현되지
// 않는다. 룩을 정확히 보존하려면 원본 그래프에 Custom Function + Dither 노드를 넣는 편이
// 낫지만 그건 SVN 관리 아트 파일 수정이라 별도 합의가 필요하다.
// (Docs/tech/wall-occlusion-implementation.md 참고)
public static class WallOcclusionAuthoring
{
    private const string ShaderName = "Project/Environment/Wall Occlusion Dither";
    private const string SettingsPath =
        "Assets/99.Settings/WallOcclusionSettings.asset";
    private const string MaterialDirectory =
        "Assets/3.Materials/Level1_Materials/Occlusion";
    private const string SourceMaterialDirectory =
        "Assets/50.Art/MapGen/MapObj/material";
    // dash-soul 머지에서 씬이 0.Scenes/MainFlow/ 아래로 재편됐다(구 경로 "0.Scenes/MapScene.unity"는
    // 더 이상 존재하지 않아 이 도구가 씬을 못 찾고 있었다).
    //
    // 🔴 2026-08-18: 정본 전투 맵이 4.MapScene-trensparent 로 바뀌었다(아트 인수인계
    // Docs/tech/map-rendering-lighting-handoff.md §2 — 4.MapScene 은 실험 이력이 남은 보조 장면).
    // 보조 장면에 적용해도 실제 검수 화면에는 반영되지 않으므로 정본을 가리킨다.
    private const string MapScenePath = "Assets/0.Scenes/MainFlow/4.MapScene-trensparent.unity";

    // 바닥/천장은 셰이더의 wallness 판정으로 이미 제외되므로 변종을 만들지 않는다.
    // 만들어봐야 룩만 근사치로 바뀌고 얻는 게 없다.
    private static readonly string[] ExcludedNameFragments = { "floor", "convayor", "conveyor" };

    private static readonly string[] PrefabSearchFolders =
    {
        "Assets/2.Prefabs/Map/WallPrefabs",
        "Assets/2.Prefabs/Map/Zoneprefab"
    };

    [MenuItem("Tools/Rendering/Wall Occlusion/Apply All")]
    public static void ApplyAll()
    {
        Dictionary<Material, Material> materialMap = EnsureMaterials();
        WallOcclusionSettings settings = EnsureSettings(materialMap);
        int restored = RestorePrefabMaterials(materialMap);
        InstallMapScene(settings);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"[WallOcclusion] Apply 완료 — 매핑 {materialMap.Count}쌍, " +
            $"프리팹에서 되돌린 슬롯 {restored}개. " +
            $"매핑: {string.Join(", ", materialMap.Keys.Select(m => m.name))}");
    }

    // ShaderHasError는 "마지막 임포트 시점"의 상태만 본다. 실제 메시지와 변종별
    // 컴파일 결과를 봐야 자주색(에러 셰이더) 원인을 특정할 수 있다.
    [MenuItem("Tools/Rendering/Wall Occlusion/Dump Shader Messages")]
    public static void DumpShaderMessages()
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[WallOcclusion] 셰이더를 찾을 수 없다: {ShaderName}");
            return;
        }

        string shaderPath = AssetDatabase.GetAssetPath(shader);
        Debug.Log(
            $"[WallOcclusion] shader='{shader.name}', path='{shaderPath}', " +
            $"hasError={ShaderUtil.ShaderHasError(shader)}, " +
            $"passCount={shader.passCount}, " +
            $"subshaderCount={shader.subshaderCount}, " +
            $"renderQueue={shader.renderQueue}, isSupported={shader.isSupported}",
            shader);

        ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
        if (messages == null || messages.Length == 0)
        {
            Debug.Log("[WallOcclusion] 셰이더 메시지 없음.");
        }
        else
        {
            foreach (ShaderMessage message in messages)
            {
                string text =
                    $"[WallOcclusion] {message.severity} " +
                    $"({message.platform}) {message.file}:{message.line} — {message.message}";
                if (message.severity ==
                    UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    Debug.LogError(text, shader);
                else
                    Debug.LogWarning(text, shader);
            }
        }

        // 변종 머티리얼이 실제로 이 셰이더를 들고 있는지, 렌더 가능한지 확인한다.
        foreach (string path in EnumerateVariantPaths())
        {
            var variant = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (variant == null)
                continue;

            Debug.Log(
                $"[WallOcclusion] variant='{variant.name}', " +
                $"shader='{(variant.shader != null ? variant.shader.name : "<null>")}', " +
                $"isSupported={(variant.shader != null && variant.shader.isSupported)}, " +
                $"passCount={variant.passCount}, " +
                $"baseMap={(variant.GetTexture("_BaseMap") != null)}",
                variant);
        }
    }

    [MenuItem("Tools/Rendering/Wall Occlusion/Validate")]
    public static void ValidateAll()
    {
        int errors = 0;

        WallOcclusionSettings settings =
            AssetDatabase.LoadAssetAtPath<WallOcclusionSettings>(SettingsPath);
        if (settings == null || !settings.HasValidMaterialMappings)
        {
            Debug.LogError("[WallOcclusion] 설정의 머티리얼 매핑이 없거나 짝이 맞지 않는다.");
            errors++;
        }

        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[WallOcclusion] 셰이더를 찾을 수 없다: {ShaderName}");
            errors++;
        }
        else if (ShaderUtil.ShaderHasError(shader))
        {
            // Shader.Find는 컴파일이 깨진 셰이더도 non-null로 돌려준다.
            Debug.LogError(
                $"[WallOcclusion] 셰이더 컴파일 오류: {ShaderName}. " +
                "Dump Shader Messages로 상세 메시지를 볼 것.",
                shader);
            errors++;
        }
        else
        {
            // ⚠️ 이 결과를 "셰이더 정상"으로 읽으면 안 된다. 셰이더 변종은 필요할 때
            // 지연 컴파일되므로, 아직 렌더된 적 없는 변종(예: Forward+ 클러스터)의
            // 오류는 여기 잡히지 않는다. 실제로 2026-07-28에 이 검사가 통과한 뒤
            // Play에서 _CLUSTER_LIGHT_LOOP 변종이 깨져 벽이 전부 자주색이 됐다.
            // 결정적 확인은 대상 머티리얼이 실제로 렌더되는 상태에서 하는 것뿐이다.
            Debug.Log(
                $"[WallOcclusion] 컴파일된 변종에 오류 없음: {ShaderName} " +
                "(미컴파일 변종은 포함하지 않음 — 실제 렌더 후 재확인할 것)");
        }

        // 변종 머티리얼에 텍스처가 비어 있으면 흰 벽이 된다 — 가장 흔한 회귀다.
        foreach (string path in EnumerateVariantPaths())
        {
            var variant = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (variant == null)
                continue;

            if (variant.GetTexture("_BaseMap") == null)
            {
                Debug.LogError(
                    $"[WallOcclusion] 변종 머티리얼의 _BaseMap이 비었다: {path}. " +
                    "원본 Shader Graph의 텍스처 프로퍼티를 찾지 못했다.",
                    variant);
                errors++;
            }
        }

        // 프리팹에 오클루전 머티리얼이 구워져 있으면 안 된다(런타임 스왑만 사용).
        int persisted = CountPersistedOcclusionSlots();
        if (persisted > 0)
        {
            Debug.LogError(
                $"[WallOcclusion] 맵 프리팹에 오클루전 머티리얼 슬롯 {persisted}개가 " +
                "직렬화돼 있다. Apply All로 되돌릴 것.");
            errors++;
        }

        if (errors == 0)
            Debug.Log("[WallOcclusion] 검증 통과 — errors=0, persistedOcclusionSlots=0.");
        else
            Debug.LogError($"[WallOcclusion] 검증 실패 — errors={errors}.");
    }

    // 소스 폴더를 스캔해 변종을 만든다. 경로 하드코딩을 없앴으므로 아트 교체로
    // 머티리얼이 추가/개명돼도 다음 Apply All에서 자동으로 따라간다.
    public static Dictionary<Material, Material> EnsureMaterials()
    {
        EnsureAssetDirectory(MaterialDirectory);
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
            throw new InvalidOperationException($"셰이더를 찾을 수 없다: {ShaderName}");

        var result = new Dictionary<Material, Material>();
        string[] guids = AssetDatabase.FindAssets(
            "t:Material",
            new[] { SourceMaterialDirectory });

        foreach (string guid in guids)
        {
            string sourcePath = AssetDatabase.GUIDToAssetPath(guid);

            // FindAssets("t:Material")은 같은 폴더의 .shadergraph에 내장된 기본 머티리얼
            // 서브에셋까지 돌려준다. 실제 .mat 파일만 대상으로 삼는다.
            if (!sourcePath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                continue;

            string fileName = Path.GetFileNameWithoutExtension(sourcePath);
            if (IsExcluded(fileName))
                continue;

            var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            if (source == null)
                continue;

            string outputPath = $"{MaterialDirectory}/{fileName}_Occlusion.mat";
            var variant = AssetDatabase.LoadAssetAtPath<Material>(outputPath);
            if (variant == null)
            {
                variant = new Material(shader) { name = $"{fileName}_Occlusion" };
                AssetDatabase.CreateAsset(variant, outputPath);
            }
            else if (variant.shader != shader)
            {
                variant.shader = shader;
            }

            CopyVisualProperties(source, variant);
            EditorUtility.SetDirty(variant);
            result[source] = variant;
        }

        return result;
    }

    public static WallOcclusionSettings EnsureSettings(
        Dictionary<Material, Material> materialMap)
    {
        var settings = AssetDatabase.LoadAssetAtPath<WallOcclusionSettings>(SettingsPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<WallOcclusionSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }

        settings.ConfigureMaterialMappings(
            materialMap.Keys.ToArray(),
            materialMap.Values.ToArray());
        EditorUtility.SetDirty(settings);
        return settings;
    }

    // 과거 저작이 프리팹에 구워 넣은 오클루전 머티리얼을 원본으로 되돌린다.
    private static int RestorePrefabMaterials(Dictionary<Material, Material> materialMap)
    {
        var reverse = new Dictionary<Material, Material>();
        foreach (KeyValuePair<Material, Material> pair in materialMap)
            reverse[pair.Value] = pair.Key;

        int restored = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", PrefabSearchFolders))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;
            try
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (PrefabUtility.IsPartOfPrefabInstance(renderer.gameObject))
                        continue;

                    Material[] materials = renderer.sharedMaterials;
                    bool rendererChanged = false;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] == null ||
                            !reverse.TryGetValue(materials[i], out Material source))
                            continue;

                        materials[i] = source;
                        rendererChanged = true;
                        changed = true;
                        restored++;
                    }

                    if (rendererChanged)
                        renderer.sharedMaterials = materials;
                }

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        return restored;
    }

    private static int CountPersistedOcclusionSlots()
    {
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", PrefabSearchFolders))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null &&
                        material.shader != null &&
                        material.shader.name == ShaderName)
                        count++;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        return count;
    }

    private static void InstallMapScene(WallOcclusionSettings settings)
    {
        Scene scene = SceneManager.GetSceneByPath(MapScenePath);
        bool openedForAuthoring = !scene.IsValid() || !scene.isLoaded;
        if (openedForAuthoring)
            scene = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Additive);

        try
        {
            MapGenerator mapGenerator = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                mapGenerator = root.GetComponentInChildren<MapGenerator>(true);
                if (mapGenerator != null)
                    break;
            }

            if (mapGenerator == null)
                throw new InvalidOperationException("MapScene에 MapGenerator가 없다.");

            GameObject host = mapGenerator.gameObject;
            WallOcclusionDriver driver = host.GetComponent<WallOcclusionDriver>();
            if (driver == null)
                driver = host.AddComponent<WallOcclusionDriver>();

            driver.SetSettings(settings);
            EditorUtility.SetDirty(driver);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (openedForAuthoring && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    // 원본이 Shader Graph라 프로퍼티 이름이 자동 생성 GUID를 포함한다.
    // 이름을 하드코딩하지 않고 셰이더의 텍스처 프로퍼티를 훑어서 albedo/normal을 고른다.
    private static void CopyVisualProperties(Material source, Material destination)
    {
        destination.SetColor(
            "_BaseColor",
            source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : Color.white);
        destination.SetFloat(
            "_Metallic",
            source.HasProperty("_Metallic") ? source.GetFloat("_Metallic") : 0f);
        destination.SetFloat(
            "_Smoothness",
            source.HasProperty("_Smoothness") ? source.GetFloat("_Smoothness") : 0.5f);
        destination.SetFloat(
            "_BumpScale",
            source.HasProperty("_BumpScale") ? source.GetFloat("_BumpScale") : 1f);
        destination.SetFloat("_WallOcclusionOpacity", 1f);
        destination.SetFloat("_WallOccAffected", 1f);

        ResolveTextures(source, out Texture albedo, out Texture normal);
        destination.SetTexture("_BaseMap", albedo);
        destination.SetTexture("_BumpMap", normal);
        destination.SetTextureScale("_BaseMap", Vector2.one);
        destination.SetTextureOffset("_BaseMap", Vector2.zero);

        if (albedo == null)
        {
            Debug.LogWarning(
                $"[WallOcclusion] '{source.name}'에서 albedo 텍스처를 찾지 못했다. " +
                $"변종이 흰색으로 보인다.",
                source);
        }
    }

    private static void ResolveTextures(
        Material source,
        out Texture albedo,
        out Texture normal)
    {
        albedo = null;
        normal = null;

        Shader shader = source.shader;
        if (shader == null)
            return;

        int propertyCount = shader.GetPropertyCount();
        for (int i = 0; i < propertyCount; i++)
        {
            if (shader.GetPropertyType(i) != ShaderPropertyType.Texture)
                continue;

            Texture texture = source.GetTexture(shader.GetPropertyNameId(i));
            if (texture == null)
                continue;

            // 임포터 설정이 normal map 여부의 유일하게 신뢰할 수 있는 근거다.
            if (IsNormalMap(texture))
                normal ??= texture;
            else
                albedo ??= texture;

            if (albedo != null && normal != null)
                return;
        }
    }

    private static bool IsNormalMap(Texture texture)
    {
        string path = AssetDatabase.GetAssetPath(texture);
        if (string.IsNullOrEmpty(path))
            return false;

        return AssetImporter.GetAtPath(path) is TextureImporter importer &&
               importer.textureType == TextureImporterType.NormalMap;
    }

    private static IEnumerable<string> EnumerateVariantPaths()
    {
        if (!AssetDatabase.IsValidFolder(MaterialDirectory))
            yield break;

        foreach (string guid in AssetDatabase.FindAssets(
                     "t:Material",
                     new[] { MaterialDirectory }))
        {
            yield return AssetDatabase.GUIDToAssetPath(guid);
        }
    }

    private static bool IsExcluded(string materialName)
    {
        string lower = materialName.ToLowerInvariant();
        foreach (string fragment in ExcludedNameFragments)
        {
            if (lower.Contains(fragment))
                return true;
        }

        return false;
    }

    private static void EnsureAssetDirectory(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
