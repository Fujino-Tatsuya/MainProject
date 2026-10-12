using UnityEditor;
using UnityEngine;

/// <summary>
/// 존1 평타 연습 허수아비 저작(S7 Z1-3, 논의 D10).
///  · 간파 사이클 없는 변형 <c>TrainingDummy_Tutorial.prefab</c> = <c>TrainingDummy.prefab</c> 의 Prefab Variant 에서
///    <see cref="TrainingDummyInterrupt"/> 만 뺀 것(노랗게 빛나면 존3 간파 신호와 헷갈린다 — §3.1). 원본·Debug 연습장은 그대로.
///  · <c>StageTutorial.prefab</c> 에 자리(<see cref="TutorialZone1Dummies"/>, 비네트워크)만 둔다 — 서버가 런타임에 스폰.
///    공용 존 프리팹(ZoneS_typeStart)은 절차 맵도 써서 건드리지 않는다.
/// <see cref="TutorialStageAuthoring.BuildStagePrefab"/>(2번)가 다시 만들 때도 <see cref="AddTo"/> 로 같이 만든다.
/// </summary>
public static class TutorialZone1Authoring
{
    const string StagePath = "Assets/2.Prefabs/Environment/Layouts/Stages/StageTutorial.prefab";
    const string DummyBasePath = "Assets/2.Prefabs/TrainingDummy.prefab";
    public const string DummyVariantPath = "Assets/2.Prefabs/TrainingDummy_Tutorial.prefab";
    const string RootName = "TutorialZone1Dummies";

    // 존1(ZoneS_typeStart) 존 로컬 좌표(존 회전 전). 중앙(스폰 낙하 지점)은 덮개 깔린 구덩이, 반대쪽은 배럴·컨테이너가 있어
    // 복도1 쪽 빈 바닥(존 로컬 +x 3~8, −z 6)에 한 줄로 둔다(2026-10-11 프리팹 소품 위치 확인).
    static readonly Vector3[] ZoneLocalSpots =
    {
        new Vector3(2.5f, 0f, -6f),
        new Vector3(5f, 0f, -6.5f),
        new Vector3(7.5f, 0f, -6f),
    };

    [MenuItem("Tools/Map/Authoring/Tutorial/8. Place Zone1 Dummies (StageTutorial Prefab)")]
    public static void PlaceInStagePrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(StagePath);
        try
        {
            if (!AddTo(root)) return;
            PrefabUtility.SaveAsPrefabAsset(root, StagePath, out bool ok);
            Debug.Log($"[TutorialStage] 존1 허수아비 자리 {ZoneLocalSpots.Length}개 → {StagePath} 저장 {(ok ? "완료" : "실패")}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>스테이지 루트에 자리 묶음을 (다시) 만든다. 슬롯 0(존1)이 없으면 false.</summary>
    public static bool AddTo(GameObject stageRoot)
    {
        ZoneSlot slot = FindSlot(stageRoot, 0);
        if (slot == null)
        {
            Debug.LogError("[TutorialStage] 슬롯 0(존1)을 못 찾았다 — 허수아비 자리를 만들지 않는다.");
            return false;
        }

        GameObject variant = EnsureDummyVariant();
        if (variant == null) return false;

        Transform old = stageRoot.transform.Find(RootName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var group = new GameObject(RootName);
        group.transform.SetParent(stageRoot.transform, false);
        var comp = group.AddComponent<TutorialZone1Dummies>();
        var so = new SerializedObject(comp);
        so.FindProperty("dummyPrefab").objectReferenceValue = variant;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 존이 실제로 놓이는 위치·회전(MapContentSpawner 와 같은 조회) — 스테이지 180° 회전이 YawSteps 에 들어 있다.
        slot.TryGetPosition(slot.FixedPrefab, out Vector3 zonePos);
        slot.TryGetYaw(slot.FixedPrefab, out int yawSteps);
        Quaternion zoneRot = Quaternion.Euler(0f, yawSteps * 90f, 0f);
        Vector3 center = stageRoot.transform.InverseTransformPoint(zonePos);

        for (int i = 0; i < ZoneLocalSpots.Length; i++)
        {
            var spot = new GameObject($"Spot_{i}");
            spot.transform.SetParent(group.transform, false);
            Vector3 p = center + zoneRot * ZoneLocalSpots[i];
            spot.transform.localPosition = p;
            Vector3 toCenter = center - p;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude > 0.01f) spot.transform.localRotation = Quaternion.LookRotation(toCenter);
        }
        return true;
    }

    /// <summary>변형 프리팹이 없으면 만든다(있으면 그대로).</summary>
    public static GameObject EnsureDummyVariant()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(DummyVariantPath);
        if (existing != null) return existing;

        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DummyBasePath);
        if (basePrefab == null)
        {
            Debug.LogError($"[TutorialStage] {DummyBasePath} 없음 — 허수아비 변형을 만들 수 없다.");
            return null;
        }

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        try
        {
            inst.name = "TrainingDummy_Tutorial";
            var interrupt = inst.GetComponent<TrainingDummyInterrupt>();
            if (interrupt != null) Object.DestroyImmediate(interrupt);   // 변형의 '제거된 컴포넌트' 오버라이드

            // 임시 씬 인스턴스라 NGO 가 씬 배치용 값(InScenePlacedSourceGlobalObjectIdHash 등)을 오버라이드로 써 넣는다 — 원본 값으로 되돌린다.
            var noSo = new SerializedObject(inst.GetComponent<Unity.Netcode.NetworkObject>());
            foreach (string name in new[] { "InScenePlacedSourceGlobalObjectIdHash", "SceneMigrationSynchronization" })
            {
                SerializedProperty prop = noSo.FindProperty(name);
                if (prop != null && prop.prefabOverride) PrefabUtility.RevertPropertyOverride(prop, InteractionMode.AutomatedAction);
            }

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(inst, DummyVariantPath, out bool ok);
            if (ok)
            {
                // 에셋으로 임포트되며 다시 계산된 GlobalObjectIdHash 를 디스크에도 남긴다(빌드는 디스크 값을 쓴다).
                EditorUtility.SetDirty(saved.GetComponent<Unity.Netcode.NetworkObject>());
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"[TutorialStage] 허수아비 변형 {DummyVariantPath} 저장 {(ok ? "완료" : "실패")} — 간파 사이클(TrainingDummyInterrupt) 없음. " +
                      "NetworkPrefabs(DefaultNetworkPrefabs.asset) 등록을 확인할 것.");
            return ok ? saved : null;
        }
        finally
        {
            Object.DestroyImmediate(inst);
        }
    }

    static ZoneSlot FindSlot(GameObject stageRoot, int id)
    {
        foreach (ZoneSlot s in stageRoot.GetComponentsInChildren<ZoneSlot>(true))
            if (s.SlotID == id) return s;
        return null;
    }
}
