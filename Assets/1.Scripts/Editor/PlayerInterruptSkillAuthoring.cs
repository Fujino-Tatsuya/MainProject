using UnityEditor;
using UnityEngine;

/// <summary>
/// 단죄의 방패(우클릭 인터럽트 슬롯) 배선 도구. 재실행 안전(멱등) — 이미 있으면 건드리지 않는다.
///
/// 하는 일:
///  1) FirstMeleeInterruptSkillData SO 에셋 생성 (없을 때만)
///  2) 판정 앵커 InterruptAttack 노드에 BoxCollider + ColliderInfo 부착
///     (Paladin.prefab / TempPlayer_Armature.prefab — 두 플레이어 프리팹의 앵커 출처가 다르다)
///  3) 플레이어 루트에 FirstMeleeInterruptSkill 부착 + data/hitboxAnchor 배선
///     + PlayerSkillController.interruptSkill 슬롯 연결 (Paladin.prefab / Player.prefab)
///
/// ⚠️ Player.prefab의 앵커는 중첩된 TempPlayer_Armature 안에 있다 — 아마추어를 먼저 처리해야 한다.
/// </summary>
public static class PlayerInterruptSkillAuthoring
{
    private const string DataAssetPath =
        "Assets/9.ScriptableObject/Player/Garen/FirstMeleeInterruptSkillData.asset";

    private const string AnchorName = "InterruptAttack";

    // 앵커(BoxCollider)를 새로 만들 때만 쓰는 초기값. Q(MainSkill)의 2×1×2 / center z=0.8 보다 짧은 리치.
    private static readonly Vector3 DefaultBoxSize = new Vector3(1.6f, 1f, 1.6f);
    private static readonly Vector3 DefaultBoxCenter = new Vector3(0f, 0f, 0.9f);

    // 앵커 노드를 가진 프리팹 (판정 앵커 부착 대상)
    private static readonly string[] AnchorPrefabs =
    {
        "Assets/2.Prefabs/Player/TempPlayer_Armature.prefab",
        "Assets/2.Prefabs/Player/Paladin/Paladin.prefab",
    };

    // PlayerSkillController를 가진 플레이어 루트 프리팹 (스킬 컴포넌트 부착 대상)
    private static readonly string[] PlayerPrefabs =
    {
        "Assets/2.Prefabs/Player/Player.prefab",
        "Assets/2.Prefabs/Player/Paladin/Paladin.prefab",
    };

    [MenuItem("Tools/Player/Authoring/Wire Interrupt Skill (단죄의 방패)")]
    public static void Wire()
    {
        FirstMeleeInterruptSkillData data = EnsureDataAsset();

        foreach (string path in AnchorPrefabs)
            EnsureAnchor(path);

        foreach (string path in PlayerPrefabs)
            EnsureSkillComponent(path, data);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[InterruptSkillAuthoring] 완료.");
    }

    private static FirstMeleeInterruptSkillData EnsureDataAsset()
    {
        var existing = AssetDatabase.LoadAssetAtPath<FirstMeleeInterruptSkillData>(DataAssetPath);
        if (existing != null)
        {
            Debug.Log($"[InterruptSkillAuthoring] SO 이미 존재 — 유지: {DataAssetPath}");
            return existing;
        }

        var data = ScriptableObject.CreateInstance<FirstMeleeInterruptSkillData>();

        // 전부 임시값. 기획 확정 시 에셋에서만 조절한다.
        var so = new SerializedObject(data);
        so.FindProperty("inputType").enumValueIndex = (int)PlayerSkillInputType.Press;
        so.FindProperty("cooldownTime").floatValue = 8f;
        so.FindProperty("attackDamageMultiplier").floatValue = 0.5f;
        so.FindProperty("flatDamageBonus").intValue = 0;
        // 자체 종료(skillDuration)보다 넉넉해야 안전망이 먼저 끊지 않는다
        so.FindProperty("maxActiveDuration").floatValue = 2f;
        so.FindProperty("tickInterval").floatValue = 0f;
        so.FindProperty("usableWhileDead").boolValue = false;
        // 적 대상 마스크 = Enemy|EnemyHurtBox (프로젝트 관례, Q와 동일)
        so.FindProperty("hittableLayers").intValue = 17664;
        so.FindProperty("targetingMode").enumValueIndex = (int)SkillTargetingMode.None;
        so.FindProperty("animatorStateName").stringValue = "Interrupt";
        so.FindProperty("snapRotationOnStart").boolValue = true;
        so.FindProperty("hitDelay").floatValue = 0.15f;
        so.FindProperty("skillDuration").floatValue = 0.6f;
        so.FindProperty("maxHitResults").intValue = 8;
        so.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.CreateAsset(data, DataAssetPath);
        Debug.Log($"[InterruptSkillAuthoring] SO 생성: {DataAssetPath}");
        return data;
    }

    private static void EnsureAnchor(string prefabPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
        {
            Debug.LogError($"[InterruptSkillAuthoring] 프리팹을 열 수 없습니다: {prefabPath}");
            return;
        }

        try
        {
            Transform anchor = FindDescendant(root.transform, AnchorName);
            if (anchor == null)
            {
                Debug.LogWarning($"[InterruptSkillAuthoring] {prefabPath}에 '{AnchorName}' 노드가 없습니다 — 건너뜀.");
                return;
            }

            bool changed = false;

            if (anchor.GetComponent<BoxCollider>() == null)
            {
                BoxCollider box = anchor.gameObject.AddComponent<BoxCollider>();
                box.size = DefaultBoxSize;
                box.center = DefaultBoxCenter;
                // ColliderInfo가 Awake에서 어차피 끄지만, 에디터에서도 물리에 끼지 않도록 미리 끈다.
                box.enabled = false;
                changed = true;
                Debug.Log($"[InterruptSkillAuthoring] {prefabPath}: BoxCollider 부착 " +
                          $"(size {DefaultBoxSize}, center {DefaultBoxCenter})");
            }

            if (anchor.GetComponent<ColliderInfo>() == null)
            {
                anchor.gameObject.AddComponent<ColliderInfo>();
                changed = true;
                Debug.Log($"[InterruptSkillAuthoring] {prefabPath}: ColliderInfo 부착");
            }

            if (changed)
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            else
                Debug.Log($"[InterruptSkillAuthoring] {prefabPath}: 앵커 이미 구성됨 — 변경 없음");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void EnsureSkillComponent(string prefabPath, FirstMeleeInterruptSkillData data)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
        {
            Debug.LogError($"[InterruptSkillAuthoring] 프리팹을 열 수 없습니다: {prefabPath}");
            return;
        }

        try
        {
            var controller = root.GetComponent<PlayerSkillController>();
            if (controller == null)
            {
                Debug.LogWarning($"[InterruptSkillAuthoring] {prefabPath} 루트에 PlayerSkillController가 없습니다 — 건너뜀.");
                return;
            }

            var skill = root.GetComponent<FirstMeleeInterruptSkill>();
            if (skill == null)
            {
                skill = root.AddComponent<FirstMeleeInterruptSkill>();
                Debug.Log($"[InterruptSkillAuthoring] {prefabPath}: FirstMeleeInterruptSkill 부착");
            }

            Transform anchorTransform = FindDescendant(root.transform, AnchorName);
            ColliderInfo anchor = anchorTransform != null ? anchorTransform.GetComponent<ColliderInfo>() : null;
            if (anchor == null)
            {
                Debug.LogError(
                    $"[InterruptSkillAuthoring] {prefabPath}: '{AnchorName}' 앵커의 ColliderInfo를 찾지 못했습니다. " +
                    "아마추어 프리팹을 먼저 처리했는지 확인하세요.");
            }

            // data / hitboxAnchor 는 private [SerializeField] — SerializedObject로 쓴다.
            var skillSo = new SerializedObject(skill);
            skillSo.FindProperty("data").objectReferenceValue = data;
            if (anchor != null)
                skillSo.FindProperty("hitboxAnchor").objectReferenceValue = anchor;
            skillSo.ApplyModifiedPropertiesWithoutUndo();

            var controllerSo = new SerializedObject(controller);
            SerializedProperty slot = controllerSo.FindProperty("interruptSkill");
            slot.objectReferenceValue = skill;
            controllerSo.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[InterruptSkillAuthoring] {prefabPath}: 배선 완료 " +
                      $"(data={data != null}, anchor={anchor != null})");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Transform FindDescendant(Transform root, string name)
    {
        if (root.name == name)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDescendant(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }
}
