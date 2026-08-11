using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Effect System v1의 에디터 진입점 두 가지.
///
/// ① <b>기본 에셋 생성</b> — 엔트리 · 카탈로그 · 매니저 프리팹을 만든다. 멱등이라 이미 있으면 건드리지 않는다.
/// ② <b>스모크 테스트</b> — 플레이 모드에서 배관을 자동 검증한다(<see cref="EffectSmokeTestRunner"/>).
///    S6에서 드라이버를 추가한 뒤 이걸 다시 돌려 "어댑터 추가만으로 끝났는지"를 확인한다.
/// </summary>
public static class EffectSystemSetup
{
    private const string EntryFolder = "Assets/5.VFX/Common";
    private const string CatalogFolder = "Assets/9.ScriptableObject/Effects";
    private const string CatalogPath = CatalogFolder + "/EffectCatalog.asset";
    private const string ManagerPrefabPath = "Assets/5.VFX/EffectManager.prefab";

    [MenuItem("Tools/Effects/v1 기본 에셋 생성")]
    public static void Run()
    {
        EnsureFolder(CatalogFolder);

        var sparkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EntryFolder + "/FX_Hit_Spark.prefab");
        var bluntPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EntryFolder + "/FX_Hit_Blunt Variant.prefab");

        if (sparkPrefab == null || bluntPrefab == null)
        {
            Debug.LogError("[EffectSystemSetup] 기준 프리팹을 찾지 못했다. 경로를 확인할 것.");
            return;
        }

        EffectEntry spark = CreateEntry(EntryFolder + "/FX_Hit_Spark_Entry.asset", entry =>
        {
            entry.duration = 0f;   // 0 = 프리팹에서 자동 계산
            entry.prewarmCount = 8;
            entry.maxActiveWarn = 32;
            entry.parts = new[] { Part(sparkPrefab, Vector3.zero, 0f) };
            entry.outroParts = new EffectPart[0];
        });

        EffectEntry blunt = CreateEntry(EntryFolder + "/FX_Hit_Blunt_Entry.asset", entry =>
        {
            entry.duration = 0f;   // 0 = 프리팹에서 자동 계산
            entry.prewarmCount = 4;
            entry.maxActiveWarn = 32;
            // 한 호출로 두 파트가 delay 간격을 두고 순차 발화한다 (컴포지트 3막 검증용).
            entry.parts = new[]
            {
                Part(bluntPrefab, Vector3.zero, 0f),
                Part(sparkPrefab, new Vector3(0f, 0.3f, 0f), 0.15f),
            };
            entry.outroParts = new EffectPart[0];
        });

        EffectCatalog catalog = AssetDatabase.LoadAssetAtPath<EffectCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<EffectCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            Debug.Log($"[EffectSystemSetup] 생성: {CatalogPath}");
        }

        var serialized = new SerializedObject(catalog);
        SetIfEmpty(serialized, "<HitSpark>k__BackingField", spark);
        SetIfEmpty(serialized, "<HitBlunt>k__BackingField", blunt);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(catalog);

        CreateManagerPrefab(catalog);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[EffectSystemSetup] 완료.");
    }

    [MenuItem("Tools/Effects/모든 엔트리 수명 재계산")]
    public static void RecomputeAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:EffectEntry");

        for (int i = 0; i < guids.Length; i++)
        {
            var entry = AssetDatabase.LoadAssetAtPath<EffectEntry>(AssetDatabase.GUIDToAssetPath(guids[i]));
            if (entry == null) continue;

            entry.RecomputeLifetimes();

            // 값이 안 바뀌었어도 항상 dirty로 둔다. OnValidate가 임포트 중에 이미 계산해 둔 값은
            // 메모리에만 있고 에셋 파일에는 없을 수 있는데, 그러면 빌드가 0을 들고 나간다.
            EditorUtility.SetDirty(entry);

            Debug.Log($"[Effect] '{entry.name}' → duration {entry.ResolvedDuration:F2}s " +
                      $"(계산 {entry.ComputedDuration:F2}s / 오버라이드 {entry.duration:F2}s)", entry);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Effect] 엔트리 {guids.Length}개 갱신·저장.");
    }

    [MenuItem("Tools/Effects/스모크 테스트 (Play Mode)")]
    public static void Smoke()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[EffectSystemSetup] 플레이 모드에서만 실행할 수 있다.");
            return;
        }

        if (EffectManager.Instance == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[EffectSystemSetup] {ManagerPrefabPath}를 찾지 못했다.");
                return;
            }
            Object.Instantiate(prefab);
        }

        //EffectSmokeTestRunner.Launch();
    }

    private static void CreateManagerPrefab(EffectCatalog catalog)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath) != null) return;

        var root = new GameObject("EffectManager");
        var manager = root.AddComponent<EffectManager>();

        var serialized = new SerializedObject(manager);
        serialized.FindProperty("catalog").objectReferenceValue = catalog;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, ManagerPrefabPath);
        Object.DestroyImmediate(root);
        Debug.Log($"[EffectSystemSetup] 생성: {ManagerPrefabPath}");
    }

    private static EffectPart Part(GameObject prefab, Vector3 offset, float delay)
    {
        return new EffectPart { prefab = prefab, offset = offset, delay = delay };
    }

    private static EffectEntry CreateEntry(string path, System.Action<EffectEntry> configure)
    {
        var existing = AssetDatabase.LoadAssetAtPath<EffectEntry>(path);
        if (existing != null) return existing;

        var entry = ScriptableObject.CreateInstance<EffectEntry>();
        configure(entry);
        AssetDatabase.CreateAsset(entry, path);
        Debug.Log($"[EffectSystemSetup] 생성: {path}");
        return entry;
    }

    private static void SetIfEmpty(SerializedObject serialized, string propertyPath, Object value)
    {
        SerializedProperty property = serialized.FindProperty(propertyPath);
        if (property == null)
        {
            Debug.LogError($"[EffectSystemSetup] 프로퍼티를 찾지 못했다: {propertyPath}");
            return;
        }

        if (property.objectReferenceValue == null) property.objectReferenceValue = value;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        string leaf = Path.GetFileName(folder);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
