using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Legacy Stage/Zone의 런타임 저작 데이터를 V3 프리팹에 안전하게 이관한다.
///
/// 원본 에셋 참조를 공유하지 않고 V3 내부 오브젝트를 새로 만든 뒤 V3 참조로 재배선한다.
/// </summary>
public static class LevelDeliveryV3ZoneLayoutWiring
{
    private const string LegacyRoot = "Assets/2.Prefabs/Map/Zoneprefab/";
    private const string V3Root = "Assets/2.Prefabs/Map/LevelDeliveryV3/Zones/";
    private const string LegacyStagePath = "Assets/2.Prefabs/Map/Stage1.prefab";
    private const string V3StagePath = "Assets/2.Prefabs/Map/LevelDeliveryV3/Stage/PF_Stage_01_V3.prefab";
    private const string CatalogPath = "Assets/50.Art/MapGen/MapObj/ZoneLayout/ZoneLayoutCatalog.asset";
    private const string MainMapScenePath = "Assets/0.Scenes/MainFlow/4.MapScene.unity";

    private readonly struct ZonePair
    {
        public readonly string LegacyName;
        public readonly string V3Name;

        public ZonePair(string legacyName, string v3Name)
        {
            LegacyName = legacyName;
            V3Name = v3Name;
        }
    }

    private static readonly ZonePair[] Pairs =
    {
        new("ZoneL_typeA", "PF_Zone_L_Type_A_V3"),
        new("ZoneL_typeB", "PF_Zone_L_Type_B_V3"),
        new("ZoneL_typeC", "PF_Zone_L_Type_C_V3"),
        new("ZoneM_typeA", "PF_Zone_M_Type_A_V3"),
        new("ZoneM_typeB", "PF_Zone_M_Type_B_V3"),
        new("ZoneM_typeC", "PF_Zone_M_Type_C_V3"),
        new("Zone_typeQuest01", "PF_Zone_Quest_01_V3"),
        new("Zone_typeQuest02", "PF_Zone_Quest_02_V3"),
        new("ZoneS_typeA", "PF_Zone_S_Type_A_V3"),
        new("ZoneS_typeBossEnter", "PF_Zone_S_Type_Boss_Enter_V3"),
        new("ZoneS_typeStart", "PF_Zone_S_Type_Start_V3"),
    };

    [MenuItem("Tools/Map/Level Delivery V3/Register ZoneLayout Classification")]
    public static void RegisterClassificationData()
    {
        int changed = 0;
        var errors = new List<string>();

        foreach (ZonePair pair in Pairs)
        {
            string legacyPath = LegacyRoot + pair.LegacyName + ".prefab";
            string v3Path = V3Root + pair.V3Name + ".prefab";

            GameObject legacyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(legacyPath);
            if (legacyPrefab == null)
            {
                errors.Add($"Legacy 프리팹 없음: {legacyPath}");
                continue;
            }

            ZoneLayout source = legacyPrefab.GetComponent<ZoneLayout>();
            if (source == null)
            {
                errors.Add($"Legacy 루트 ZoneLayout 없음: {legacyPath}");
                continue;
            }

            GameObject v3Root = PrefabUtility.LoadPrefabContents(v3Path);
            if (v3Root == null)
            {
                errors.Add($"V3 프리팹 로드 실패: {v3Path}");
                continue;
            }

            try
            {
                ZoneLayout target = v3Root.GetComponent<ZoneLayout>();
                if (target == null)
                    target = v3Root.AddComponent<ZoneLayout>();

                target.Size = source.Size;
                target.Role = source.Role;
                target.Difficulty = source.Difficulty;
                target.ThemeName = source.ThemeName;
                target.MonsterGroupID = source.MonsterGroupID;

                // V3에서 새로 저작할 데이터. Legacy 계층의 Transform 참조를 가져오지 않는다.
                target.MonsterSpawnPoints = new List<Transform>();
                target.MonsterSpawnEntries = new List<MonsterSpawnEntry>();
                target.Nodes = new List<NodeMarker>();

                PrefabUtility.SaveAsPrefabAsset(v3Root, v3Path);
                changed++;
                Debug.Log(
                    $"[LevelDeliveryV3] ZoneLayout 등록: {pair.V3Name} " +
                    $"(Size:{target.Size}, Role:{target.Role}, Difficulty:{target.Difficulty}, " +
                    $"DefaultGroup:{target.MonsterGroupID})");
            }
            catch (Exception exception)
            {
                errors.Add($"{v3Path}: {exception.Message}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(v3Root);
            }
        }

        AssetDatabase.SaveAssets();

        foreach (string error in errors)
            Debug.LogError("[LevelDeliveryV3] " + error);

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"V3 ZoneLayout 등록 실패 — 성공 {changed}, 오류 {errors.Count}. Console을 확인하세요.");

        Debug.Log($"[LevelDeliveryV3] ZoneLayout 분류 등록 완료 — {changed}/{Pairs.Length}.");
    }

    [MenuItem("Tools/Map/Level Delivery V3/Validate ZoneLayout Classification")]
    public static void ValidateClassificationData()
    {
        int valid = 0;
        var errors = new List<string>();

        foreach (ZonePair pair in Pairs)
        {
            string legacyPath = LegacyRoot + pair.LegacyName + ".prefab";
            string v3Path = V3Root + pair.V3Name + ".prefab";
            ZoneLayout source = AssetDatabase.LoadAssetAtPath<GameObject>(legacyPath)?.GetComponent<ZoneLayout>();
            ZoneLayout target = AssetDatabase.LoadAssetAtPath<GameObject>(v3Path)?.GetComponent<ZoneLayout>();

            if (source == null || target == null)
            {
                errors.Add($"컴포넌트 누락: {pair.LegacyName} -> {pair.V3Name}");
                continue;
            }

            bool classificationMatches =
                source.Size == target.Size &&
                source.Role == target.Role &&
                source.Difficulty == target.Difficulty &&
                source.MonsterGroupID == target.MonsterGroupID;

            bool authoringDataIsEmpty =
                target.MonsterSpawnPoints != null && target.MonsterSpawnPoints.Count == 0 &&
                target.MonsterSpawnEntries != null && target.MonsterSpawnEntries.Count == 0 &&
                target.Nodes != null && target.Nodes.Count == 0;

            if (!classificationMatches || !authoringDataIsEmpty)
            {
                errors.Add(
                    $"데이터 불일치: {pair.V3Name} " +
                    $"(분류일치:{classificationMatches}, 신규저작목록비움:{authoringDataIsEmpty})");
                continue;
            }

            valid++;
        }

        foreach (string error in errors)
            Debug.LogError("[LevelDeliveryV3] " + error);

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"V3 ZoneLayout 검증 실패 — 정상 {valid}, 오류 {errors.Count}. Console을 확인하세요.");

        Debug.Log($"[LevelDeliveryV3] ZoneLayout 분류 검증 통과 — {valid}/{Pairs.Length}.");
    }

    [MenuItem("Tools/Map/Level Delivery V3/Copy Legacy Monster Spawn Authoring")]
    public static void CopyLegacyMonsterSpawnAuthoring()
    {
        int changed = 0;
        int copiedMarkers = 0;
        var errors = new List<string>();

        foreach (ZonePair pair in Pairs)
        {
            string legacyPath = LegacyRoot + pair.LegacyName + ".prefab";
            string v3Path = V3Root + pair.V3Name + ".prefab";
            GameObject legacyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(legacyPath);
            ZoneLayout source = legacyPrefab != null ? legacyPrefab.GetComponent<ZoneLayout>() : null;

            if (source == null)
            {
                errors.Add($"Legacy ZoneLayout 없음: {legacyPath}");
                continue;
            }

            GameObject v3Root = PrefabUtility.LoadPrefabContents(v3Path);
            if (v3Root == null)
            {
                errors.Add($"V3 프리팹 로드 실패: {v3Path}");
                continue;
            }

            try
            {
                ZoneLayout target = v3Root.GetComponent<ZoneLayout>();
                if (target == null)
                {
                    errors.Add($"V3 ZoneLayout 없음: {v3Path}");
                    continue;
                }

                bool alreadyAuthored =
                    (target.MonsterSpawnPoints != null && target.MonsterSpawnPoints.Count > 0) ||
                    (target.MonsterSpawnEntries != null && target.MonsterSpawnEntries.Count > 0) ||
                    v3Root.transform.Find("MonsterSpawnPoints") != null;

                if (alreadyAuthored)
                {
                    errors.Add($"기존 V3 마커 데이터가 있어 덮어쓰지 않음: {v3Path}");
                    continue;
                }

                int sourcePointCount = source.MonsterSpawnPoints?.Count ?? 0;
                int sourceEntryCount = source.MonsterSpawnEntries?.Count ?? 0;
                if (sourcePointCount == 0)
                {
                    if (sourceEntryCount != 0)
                        errors.Add($"Legacy Entries는 있지만 포인트가 없음: {legacyPath}");
                    continue;
                }

                Transform sourceMarkerRoot = legacyPrefab.transform.Find("MonsterSpawnPoints");
                if (sourceMarkerRoot == null)
                {
                    errors.Add($"Legacy MonsterSpawnPoints 루트 없음: {legacyPath}");
                    continue;
                }

                GameObject copiedRootObject = UnityEngine.Object.Instantiate(
                    sourceMarkerRoot.gameObject, v3Root.transform);
                copiedRootObject.name = sourceMarkerRoot.name;
                Transform copiedRoot = copiedRootObject.transform;

                var markerMap = new Dictionary<Transform, Transform>();
                var copiedPoints = new List<Transform>(sourcePointCount);
                foreach (Transform sourceMarker in source.MonsterSpawnPoints)
                {
                    if (sourceMarker == null)
                    {
                        copiedPoints.Add(null);
                        continue;
                    }

                    string relativePath = AnimationUtility.CalculateTransformPath(
                        sourceMarker, sourceMarkerRoot);
                    Transform copiedMarker = string.IsNullOrEmpty(relativePath)
                        ? copiedRoot
                        : copiedRoot.Find(relativePath);

                    if (copiedMarker == null)
                    {
                        errors.Add($"복제 마커 경로를 찾을 수 없음: {pair.V3Name}/{relativePath}");
                        copiedPoints.Add(null);
                        continue;
                    }

                    markerMap[sourceMarker] = copiedMarker;
                    copiedPoints.Add(copiedMarker);
                }

                var copiedEntries = new List<MonsterSpawnEntry>(sourceEntryCount);
                if (source.MonsterSpawnEntries != null)
                {
                    foreach (MonsterSpawnEntry sourceEntry in source.MonsterSpawnEntries)
                    {
                        if (sourceEntry.Marker == null || !markerMap.TryGetValue(sourceEntry.Marker, out Transform copiedMarker))
                        {
                            errors.Add($"Entry 마커 매핑 실패: {pair.LegacyName}");
                            copiedEntries.Add(new MonsterSpawnEntry
                            {
                                Marker = null,
                                MonsterGroupID = sourceEntry.MonsterGroupID,
                            });
                            continue;
                        }

                        copiedEntries.Add(new MonsterSpawnEntry
                        {
                            Marker = copiedMarker,
                            MonsterGroupID = sourceEntry.MonsterGroupID,
                        });
                    }
                }

                if (copiedPoints.Exists(marker => marker == null) ||
                    copiedEntries.Exists(entry => entry.Marker == null))
                {
                    UnityEngine.Object.DestroyImmediate(copiedRootObject);
                    continue;
                }

                target.MonsterSpawnPoints = copiedPoints;
                target.MonsterSpawnEntries = copiedEntries;
                target.Nodes = new List<NodeMarker>();

                PrefabUtility.SaveAsPrefabAsset(v3Root, v3Path);
                changed++;
                copiedMarkers += copiedPoints.Count;
                Debug.Log(
                    $"[LevelDeliveryV3] 몬스터 마커 복제: {pair.V3Name} " +
                    $"(Points:{copiedPoints.Count}, Entries:{copiedEntries.Count})");
            }
            catch (Exception exception)
            {
                errors.Add($"{v3Path}: {exception.Message}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(v3Root);
            }
        }

        AssetDatabase.SaveAssets();

        foreach (string error in errors)
            Debug.LogError("[LevelDeliveryV3] " + error);

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"V3 몬스터 마커 복제 실패 — 저장 {changed}, 오류 {errors.Count}. Console을 확인하세요.");

        Debug.Log(
            $"[LevelDeliveryV3] Legacy 몬스터 마커 복제 완료 — Zone {changed}, Marker {copiedMarkers}.");
    }

    [MenuItem("Tools/Map/Level Delivery V3/Validate Legacy Monster Spawn Authoring")]
    public static void ValidateLegacyMonsterSpawnAuthoring()
    {
        int validZones = 0;
        int validMarkers = 0;
        var errors = new List<string>();

        foreach (ZonePair pair in Pairs)
        {
            string legacyPath = LegacyRoot + pair.LegacyName + ".prefab";
            string v3Path = V3Root + pair.V3Name + ".prefab";
            ZoneLayout source = AssetDatabase.LoadAssetAtPath<GameObject>(legacyPath)?.GetComponent<ZoneLayout>();
            ZoneLayout target = AssetDatabase.LoadAssetAtPath<GameObject>(v3Path)?.GetComponent<ZoneLayout>();

            if (source == null || target == null)
            {
                errors.Add($"ZoneLayout 누락: {pair.LegacyName} -> {pair.V3Name}");
                continue;
            }

            int sourcePointCount = source.MonsterSpawnPoints?.Count ?? 0;
            int targetPointCount = target.MonsterSpawnPoints?.Count ?? 0;
            int sourceEntryCount = source.MonsterSpawnEntries?.Count ?? 0;
            int targetEntryCount = target.MonsterSpawnEntries?.Count ?? 0;
            bool zoneValid = sourcePointCount == targetPointCount && sourceEntryCount == targetEntryCount;

            if (!zoneValid)
            {
                errors.Add(
                    $"목록 수 불일치: {pair.V3Name} " +
                    $"(Points {sourcePointCount}/{targetPointCount}, Entries {sourceEntryCount}/{targetEntryCount})");
                continue;
            }

            for (int i = 0; i < sourcePointCount; i++)
            {
                Transform sourceMarker = source.MonsterSpawnPoints[i];
                Transform targetMarker = target.MonsterSpawnPoints[i];
                if (sourceMarker == null || targetMarker == null)
                {
                    errors.Add($"Null 마커: {pair.V3Name}[{i}]");
                    zoneValid = false;
                    continue;
                }

                Vector3 sourceLocalPosition = source.transform.InverseTransformPoint(sourceMarker.position);
                Vector3 targetLocalPosition = target.transform.InverseTransformPoint(targetMarker.position);
                Quaternion sourceLocalRotation = Quaternion.Inverse(source.transform.rotation) * sourceMarker.rotation;
                Quaternion targetLocalRotation = Quaternion.Inverse(target.transform.rotation) * targetMarker.rotation;

                if (sourceMarker.name != targetMarker.name ||
                    Vector3.Distance(sourceLocalPosition, targetLocalPosition) > 0.0001f ||
                    Quaternion.Angle(sourceLocalRotation, targetLocalRotation) > 0.01f)
                {
                    errors.Add($"Transform 불일치: {pair.V3Name}[{i}] {targetMarker.name}");
                    zoneValid = false;
                    continue;
                }

                validMarkers++;
            }

            for (int i = 0; i < sourceEntryCount; i++)
            {
                MonsterSpawnEntry sourceEntry = source.MonsterSpawnEntries[i];
                MonsterSpawnEntry targetEntry = target.MonsterSpawnEntries[i];
                int targetMarkerIndex = target.MonsterSpawnPoints.IndexOf(targetEntry.Marker);
                int sourceMarkerIndex = source.MonsterSpawnPoints.IndexOf(sourceEntry.Marker);
                if (sourceEntry.MonsterGroupID != targetEntry.MonsterGroupID ||
                    sourceMarkerIndex != targetMarkerIndex)
                {
                    errors.Add($"Entry 불일치: {pair.V3Name}[{i}]");
                    zoneValid = false;
                }
            }

            if (zoneValid)
                validZones++;
        }

        foreach (string error in errors)
            Debug.LogError("[LevelDeliveryV3] " + error);

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"V3 몬스터 마커 검증 실패 — 정상 Zone {validZones}, 오류 {errors.Count}. Console을 확인하세요.");

        Debug.Log(
            $"[LevelDeliveryV3] Legacy 몬스터 마커 검증 통과 — Zone {validZones}/{Pairs.Length}, " +
            $"Marker {validMarkers}.");
    }

    [MenuItem("Tools/Map/Level Delivery V3/Copy Legacy Stage Slots")]
    public static void CopyLegacyStageSlots()
    {
        var errors = new List<string>();
        Dictionary<GameObject, GameObject> zoneMap = BuildZonePrefabMap(errors);
        GameObject legacyStage = AssetDatabase.LoadAssetAtPath<GameObject>(LegacyStagePath);
        Transform sourceSlots = legacyStage != null ? legacyStage.transform.Find("Slots") : null;

        if (sourceSlots == null)
            errors.Add($"Legacy Slots 루트 없음: {LegacyStagePath}");

        GameObject v3Stage = null;
        if (errors.Count == 0)
            v3Stage = PrefabUtility.LoadPrefabContents(V3StagePath);
        if (errors.Count == 0 && v3Stage == null)
            errors.Add($"V3 Stage 로드 실패: {V3StagePath}");

        try
        {
            if (errors.Count == 0 && v3Stage.transform.Find("Slots") != null)
                errors.Add($"V3 Stage에 Slots가 이미 있어 덮어쓰지 않음: {V3StagePath}");

            if (errors.Count == 0)
            {
                GameObject copiedSlotsObject = UnityEngine.Object.Instantiate(
                    sourceSlots.gameObject, v3Stage.transform);
                copiedSlotsObject.name = sourceSlots.name;

                ZoneSlot[] slots = copiedSlotsObject.GetComponentsInChildren<ZoneSlot>(true);
                foreach (ZoneSlot slot in slots)
                {
                    slot.FixedPrefab = RemapZonePrefab(
                        slot.FixedPrefab, zoneMap, errors, $"Slot {slot.SlotID} FixedPrefab");
                    slot.QuestPrefab = RemapZonePrefab(
                        slot.QuestPrefab, zoneMap, errors, $"Slot {slot.SlotID} QuestPrefab");

                    for (int i = 0; i < slot.Rotations.Count; i++)
                    {
                        ZoneSlot.RotationEntry entry = slot.Rotations[i];
                        entry.Prefab = RemapZonePrefab(
                            entry.Prefab, zoneMap, errors, $"Slot {slot.SlotID} Rotations[{i}]");
                        slot.Rotations[i] = entry;
                    }
                }

                if (errors.Count == 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(v3Stage, V3StagePath);
                    AssetDatabase.SaveAssets();
                    Debug.Log(
                        $"[LevelDeliveryV3] Legacy Stage Slots 복제 완료 — Slot {slots.Length}, " +
                        "모든 Zone 참조를 V3로 교체.");
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(copiedSlotsObject);
                }
            }
        }
        catch (Exception exception)
        {
            errors.Add($"{V3StagePath}: {exception.Message}");
        }
        finally
        {
            if (v3Stage != null)
                PrefabUtility.UnloadPrefabContents(v3Stage);
        }

        foreach (string error in errors)
            Debug.LogError("[LevelDeliveryV3] " + error);

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"V3 Stage Slots 복제 실패 — 오류 {errors.Count}. Console을 확인하세요.");
    }

    [MenuItem("Tools/Map/Level Delivery V3/Validate Legacy Stage Slots")]
    public static void ValidateLegacyStageSlots()
    {
        var errors = new List<string>();
        Dictionary<GameObject, GameObject> zoneMap = BuildZonePrefabMap(errors);
        GameObject legacyStage = AssetDatabase.LoadAssetAtPath<GameObject>(LegacyStagePath);
        GameObject v3Stage = AssetDatabase.LoadAssetAtPath<GameObject>(V3StagePath);
        ZoneSlot[] sourceSlots = legacyStage != null
            ? legacyStage.GetComponentsInChildren<ZoneSlot>(true)
            : Array.Empty<ZoneSlot>();
        ZoneSlot[] targetSlots = v3Stage != null
            ? v3Stage.GetComponentsInChildren<ZoneSlot>(true)
            : Array.Empty<ZoneSlot>();

        if (sourceSlots.Length != targetSlots.Length)
            errors.Add($"Slot 수 불일치: Legacy {sourceSlots.Length}, V3 {targetSlots.Length}");

        var targetById = new Dictionary<int, ZoneSlot>();
        foreach (ZoneSlot target in targetSlots)
        {
            if (!targetById.TryAdd(target.SlotID, target))
                errors.Add($"V3 SlotID 중복: {target.SlotID}");
        }

        int valid = 0;
        foreach (ZoneSlot source in sourceSlots)
        {
            if (!targetById.TryGetValue(source.SlotID, out ZoneSlot target))
            {
                errors.Add($"V3 Slot 누락: {source.SlotID}");
                continue;
            }

            bool slotValid =
                source.Size == target.Size &&
                source.Footprint == target.Footprint &&
                source.IsQuestCandidate == target.IsQuestCandidate &&
                source.IsBossCandidate == target.IsBossCandidate &&
                source.IsSpawnCandidate == target.IsSpawnCandidate &&
                Vector3.Distance(
                    legacyStage.transform.InverseTransformPoint(source.transform.position),
                    v3Stage.transform.InverseTransformPoint(target.transform.position)) < 0.0001f &&
                Quaternion.Angle(
                    Quaternion.Inverse(legacyStage.transform.rotation) * source.transform.rotation,
                    Quaternion.Inverse(v3Stage.transform.rotation) * target.transform.rotation) < 0.01f;

            GameObject expectedFixed = ExpectedMappedPrefab(source.FixedPrefab, zoneMap);
            GameObject expectedQuest = ExpectedMappedPrefab(source.QuestPrefab, zoneMap);
            slotValid &= target.FixedPrefab == expectedFixed && target.QuestPrefab == expectedQuest;
            slotValid &= source.Rotations.Count == target.Rotations.Count;

            int rotationCount = Math.Min(source.Rotations.Count, target.Rotations.Count);
            for (int i = 0; i < rotationCount; i++)
            {
                ZoneSlot.RotationEntry sourceEntry = source.Rotations[i];
                ZoneSlot.RotationEntry targetEntry = target.Rotations[i];
                slotValid &=
                    targetEntry.Prefab == ExpectedMappedPrefab(sourceEntry.Prefab, zoneMap) &&
                    sourceEntry.YawSteps == targetEntry.YawSteps &&
                    sourceEntry.HasPosition == targetEntry.HasPosition &&
                    Vector3.Distance(sourceEntry.Position, targetEntry.Position) < 0.0001f;
            }

            if (!slotValid)
            {
                errors.Add($"Slot 데이터 불일치: {source.SlotID} ({source.name})");
                continue;
            }

            valid++;
        }

        foreach (string error in errors)
            Debug.LogError("[LevelDeliveryV3] " + error);

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"V3 Stage Slots 검증 실패 — 정상 {valid}, 오류 {errors.Count}. Console을 확인하세요.");

        Debug.Log($"[LevelDeliveryV3] Legacy Stage Slots 검증 통과 — Slot {valid}/{sourceSlots.Length}.");
    }

    private static Dictionary<GameObject, GameObject> BuildZonePrefabMap(List<string> errors)
    {
        var map = new Dictionary<GameObject, GameObject>();
        foreach (ZonePair pair in Pairs)
        {
            GameObject legacy = AssetDatabase.LoadAssetAtPath<GameObject>(
                LegacyRoot + pair.LegacyName + ".prefab");
            GameObject v3 = AssetDatabase.LoadAssetAtPath<GameObject>(
                V3Root + pair.V3Name + ".prefab");
            if (legacy == null || v3 == null)
            {
                errors.Add($"Zone 대응 프리팹 누락: {pair.LegacyName} -> {pair.V3Name}");
                continue;
            }

            map[legacy] = v3;
        }

        return map;
    }

    private static GameObject RemapZonePrefab(
        GameObject source,
        IReadOnlyDictionary<GameObject, GameObject> zoneMap,
        List<string> errors,
        string context)
    {
        if (source == null)
            return null;
        if (zoneMap.TryGetValue(source, out GameObject mapped))
            return mapped;

        errors.Add($"알 수 없는 Legacy Zone 참조: {context} = {AssetDatabase.GetAssetPath(source)}");
        return null;
    }

    private static GameObject ExpectedMappedPrefab(
        GameObject source,
        IReadOnlyDictionary<GameObject, GameObject> zoneMap)
    {
        if (source == null)
            return null;
        return zoneMap.TryGetValue(source, out GameObject mapped) ? mapped : null;
    }

    [MenuItem("Tools/Map/Level Delivery V3/Switch Main Flow Stage and Catalog to V3")]
    public static void SwitchMainFlowStageAndCatalogToV3()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Play Mode에서는 V3 메인 플로우를 전환할 수 없습니다.");

        var errors = new List<string>();
        Dictionary<GameObject, GameObject> zoneMap = BuildZonePrefabMap(errors);
        GameObject legacyStageAsset = AssetDatabase.LoadAssetAtPath<GameObject>(LegacyStagePath);
        GameObject v3StageAsset = AssetDatabase.LoadAssetAtPath<GameObject>(V3StagePath);
        ZoneLayoutCatalogSO catalog = AssetDatabase.LoadAssetAtPath<ZoneLayoutCatalogSO>(CatalogPath);

        if (legacyStageAsset == null) errors.Add($"Legacy Stage 없음: {LegacyStagePath}");
        if (v3StageAsset == null) errors.Add($"V3 Stage 없음: {V3StagePath}");
        if (catalog == null) errors.Add($"ZoneLayoutCatalog 없음: {CatalogPath}");
        if (v3StageAsset != null && v3StageAsset.GetComponentsInChildren<ZoneSlot>(true).Length != 10)
            errors.Add("V3 Stage의 ZoneSlot이 10개가 아닙니다. Stage Slots 복제·검증을 먼저 실행하세요.");

        var mappedCatalogEntries = new List<ZoneLayoutCatalogSO.Entry>();
        if (catalog != null && catalog.Entries != null)
        {
            foreach (ZoneLayoutCatalogSO.Entry sourceEntry in catalog.Entries)
            {
                ZoneLayoutCatalogSO.Entry mappedEntry = sourceEntry;
                mappedEntry.Prefab = MapCatalogPrefab(sourceEntry.Prefab, zoneMap, errors);
                mappedCatalogEntries.Add(mappedEntry);
            }
        }

        if (mappedCatalogEntries.Count != Pairs.Length)
            errors.Add($"Catalog Entry 수가 {Pairs.Length}개가 아님: {mappedCatalogEntries.Count}");

        ThrowIfErrors(errors, "V3 메인 플로우 전환 사전 검증 실패");

        Scene scene = SceneManager.GetSceneByPath(MainMapScenePath);
        bool openedByTool = !scene.IsValid() || !scene.isLoaded;
        if (openedByTool)
            scene = EditorSceneManager.OpenScene(MainMapScenePath, OpenSceneMode.Additive);
        else if (scene.isDirty)
            throw new InvalidOperationException(
                "4.MapScene에 저장하지 않은 변경이 있어 전환을 중단합니다. 씬을 저장하거나 되돌린 뒤 다시 실행하세요.");

        GameObject newStageInstance = null;
        bool saved = false;
        try
        {
            List<GameObject> legacyInstances = FindPrefabInstanceRoots(scene, LegacyStagePath);
            List<GameObject> v3Instances = FindPrefabInstanceRoots(scene, V3StagePath);
            if (legacyInstances.Count != 1)
                errors.Add($"4.MapScene의 Legacy Stage 인스턴스가 정확히 1개가 아님: {legacyInstances.Count}");
            if (v3Instances.Count != 0)
                errors.Add($"4.MapScene에 V3 Stage 인스턴스가 이미 존재함: {v3Instances.Count}");
            ThrowIfErrors(errors, "V3 Stage 씬 교체 실패");

            GameObject legacyInstance = legacyInstances[0];
            newStageInstance = (GameObject)PrefabUtility.InstantiatePrefab(v3StageAsset, scene);
            newStageInstance.name = v3StageAsset.name;
            newStageInstance.transform.SetPositionAndRotation(
                legacyInstance.transform.position,
                legacyInstance.transform.rotation);
            newStageInstance.transform.localScale = legacyInstance.transform.localScale;
            newStageInstance.transform.SetSiblingIndex(legacyInstance.transform.GetSiblingIndex());
            newStageInstance.SetActive(legacyInstance.activeSelf);

            CopySceneSlotOverrides(legacyInstance, newStageInstance, zoneMap, errors);
            ThrowIfErrors(errors, "V3 Stage 슬롯 오버라이드 복사 실패");

            UnityEngine.Object.DestroyImmediate(legacyInstance);
            catalog.Entries = mappedCatalogEntries;
            EditorUtility.SetDirty(catalog);
            EditorSceneManager.MarkSceneDirty(scene);

            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"씬 저장 실패: {MainMapScenePath}");
            AssetDatabase.SaveAssets();
            saved = true;

            Debug.Log(
                "[LevelDeliveryV3] 메인 플로우 V3 전환 완료 — " +
                "Stage 1개 교체, Catalog 11개 교체, Scene Slot 오버라이드 10개 보존, " +
                "구형 씬 추가 MeshCollider 40개 제외.");
        }
        finally
        {
            if (!saved && newStageInstance != null)
                UnityEngine.Object.DestroyImmediate(newStageInstance);
            if (openedByTool && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    [MenuItem("Tools/Map/Level Delivery V3/Validate Main Flow V3 Connection")]
    public static void ValidateMainFlowV3Connection()
    {
        var errors = new List<string>();
        Dictionary<GameObject, GameObject> zoneMap = BuildZonePrefabMap(errors);
        var v3Prefabs = new HashSet<GameObject>(zoneMap.Values);
        ZoneLayoutCatalogSO catalog = AssetDatabase.LoadAssetAtPath<ZoneLayoutCatalogSO>(CatalogPath);

        if (catalog == null || catalog.Entries == null || catalog.Entries.Count != Pairs.Length)
        {
            errors.Add("ZoneLayoutCatalog Entry가 11개가 아닙니다.");
        }
        else
        {
            foreach (ZoneLayoutCatalogSO.Entry entry in catalog.Entries)
                if (entry.Prefab == null || !v3Prefabs.Contains(entry.Prefab))
                    errors.Add($"Catalog에 V3가 아닌 참조가 남음: {AssetDatabase.GetAssetPath(entry.Prefab)}");
        }

        Scene scene = SceneManager.GetSceneByPath(MainMapScenePath);
        bool openedByTool = !scene.IsValid() || !scene.isLoaded;
        if (openedByTool)
            scene = EditorSceneManager.OpenScene(MainMapScenePath, OpenSceneMode.Additive);

        try
        {
            List<GameObject> legacyInstances = FindPrefabInstanceRoots(scene, LegacyStagePath);
            List<GameObject> v3Instances = FindPrefabInstanceRoots(scene, V3StagePath);
            if (legacyInstances.Count != 0)
                errors.Add($"Legacy Stage 인스턴스 잔존: {legacyInstances.Count}");
            if (v3Instances.Count != 1)
                errors.Add($"V3 Stage 인스턴스가 정확히 1개가 아님: {v3Instances.Count}");

            int slotCount = v3Instances.Count == 1
                ? v3Instances[0].GetComponentsInChildren<ZoneSlot>(true).Length
                : 0;
            if (slotCount != 10)
                errors.Add($"메인 씬 V3 ZoneSlot 수 불일치: {slotCount}");

            if (v3Instances.Count == 1)
            {
                foreach (ZoneSlot slot in v3Instances[0].GetComponentsInChildren<ZoneSlot>(true))
                {
                    ValidateV3Reference(slot.FixedPrefab, v3Prefabs, errors, $"Slot {slot.SlotID} FixedPrefab");
                    ValidateV3Reference(slot.QuestPrefab, v3Prefabs, errors, $"Slot {slot.SlotID} QuestPrefab");
                    foreach (ZoneSlot.RotationEntry rotation in slot.Rotations)
                        ValidateV3Reference(rotation.Prefab, v3Prefabs, errors, $"Slot {slot.SlotID} Rotation");
                }
            }

            ThrowIfErrors(errors, "V3 메인 플로우 연결 검증 실패");
            Debug.Log(
                "[LevelDeliveryV3] 메인 플로우 V3 연결 검증 통과 — " +
                "Catalog 11/11, Stage 1/1, Slot 10/10, Legacy 참조 0.");
        }
        finally
        {
            if (openedByTool && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static GameObject MapCatalogPrefab(
        GameObject source,
        IReadOnlyDictionary<GameObject, GameObject> zoneMap,
        List<string> errors)
    {
        if (source != null && zoneMap.TryGetValue(source, out GameObject mapped))
            return mapped;
        if (source != null)
        {
            foreach (GameObject v3Prefab in zoneMap.Values)
                if (v3Prefab == source)
                    return source;
        }

        errors.Add($"Catalog의 알 수 없는 Zone 참조: {AssetDatabase.GetAssetPath(source)}");
        return null;
    }

    private static List<GameObject> FindPrefabInstanceRoots(Scene scene, string prefabPath)
    {
        var result = new List<GameObject>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (PrefabUtility.GetOutermostPrefabInstanceRoot(root) != root)
                continue;
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == prefabPath)
                result.Add(root);
        }

        return result;
    }

    private static void CopySceneSlotOverrides(
        GameObject sourceStage,
        GameObject targetStage,
        IReadOnlyDictionary<GameObject, GameObject> zoneMap,
        List<string> errors)
    {
        ZoneSlot[] sourceSlots = sourceStage.GetComponentsInChildren<ZoneSlot>(true);
        ZoneSlot[] targetSlots = targetStage.GetComponentsInChildren<ZoneSlot>(true);
        var targetById = new Dictionary<int, ZoneSlot>();
        foreach (ZoneSlot target in targetSlots)
        {
            if (!targetById.TryAdd(target.SlotID, target))
                errors.Add($"V3 씬 SlotID 중복: {target.SlotID}");
        }

        if (sourceSlots.Length != 10 || targetSlots.Length != 10)
            errors.Add($"씬 Slot 수 불일치: Legacy {sourceSlots.Length}, V3 {targetSlots.Length}");

        foreach (ZoneSlot source in sourceSlots)
        {
            if (!targetById.TryGetValue(source.SlotID, out ZoneSlot target))
            {
                errors.Add($"V3 씬 Slot 누락: {source.SlotID}");
                continue;
            }

            target.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            target.transform.localScale = source.transform.localScale;
            target.SlotID = source.SlotID;
            target.Size = source.Size;
            target.Footprint = source.Footprint;
            target.IsQuestCandidate = source.IsQuestCandidate;
            target.IsBossCandidate = source.IsBossCandidate;
            target.IsSpawnCandidate = source.IsSpawnCandidate;
            target.FixedPrefab = RemapZonePrefab(
                source.FixedPrefab, zoneMap, errors, $"Scene Slot {source.SlotID} FixedPrefab");
            target.QuestPrefab = RemapZonePrefab(
                source.QuestPrefab, zoneMap, errors, $"Scene Slot {source.SlotID} QuestPrefab");
            target.AssignedRole = source.AssignedRole;
            target.IsFilled = source.IsFilled;
            target.Rotations = new List<ZoneSlot.RotationEntry>(source.Rotations.Count);

            for (int i = 0; i < source.Rotations.Count; i++)
            {
                ZoneSlot.RotationEntry entry = source.Rotations[i];
                entry.Prefab = RemapZonePrefab(
                    entry.Prefab, zoneMap, errors, $"Scene Slot {source.SlotID} Rotations[{i}]");
                target.Rotations.Add(entry);
            }

            ValidateCopiedSceneSlot(source, target, zoneMap, errors);
        }
    }

    private static void ValidateCopiedSceneSlot(
        ZoneSlot source,
        ZoneSlot target,
        IReadOnlyDictionary<GameObject, GameObject> zoneMap,
        List<string> errors)
    {
        bool valid =
            source.SlotID == target.SlotID &&
            source.Size == target.Size &&
            source.Footprint == target.Footprint &&
            source.IsQuestCandidate == target.IsQuestCandidate &&
            source.IsBossCandidate == target.IsBossCandidate &&
            source.IsSpawnCandidate == target.IsSpawnCandidate &&
            source.Rotations.Count == target.Rotations.Count &&
            target.FixedPrefab == ExpectedMappedPrefab(source.FixedPrefab, zoneMap) &&
            target.QuestPrefab == ExpectedMappedPrefab(source.QuestPrefab, zoneMap);

        int count = Math.Min(source.Rotations.Count, target.Rotations.Count);
        for (int i = 0; i < count; i++)
        {
            ZoneSlot.RotationEntry a = source.Rotations[i];
            ZoneSlot.RotationEntry b = target.Rotations[i];
            valid &=
                b.Prefab == ExpectedMappedPrefab(a.Prefab, zoneMap) &&
                a.YawSteps == b.YawSteps &&
                a.HasPosition == b.HasPosition &&
                Vector3.Distance(a.Position, b.Position) < 0.0001f;
        }

        if (!valid)
            errors.Add($"Scene Slot 복사 검증 실패: {source.SlotID}");
    }

    private static void ValidateV3Reference(
        GameObject value,
        ISet<GameObject> v3Prefabs,
        List<string> errors,
        string context)
    {
        if (value != null && !v3Prefabs.Contains(value))
            errors.Add($"{context}에 V3가 아닌 참조: {AssetDatabase.GetAssetPath(value)}");
    }

    private static void ThrowIfErrors(List<string> errors, string title)
    {
        if (errors.Count == 0)
            return;
        foreach (string error in errors)
            Debug.LogError("[LevelDeliveryV3] " + error);
        throw new InvalidOperationException($"{title} — 오류 {errors.Count}. Console을 확인하세요.");
    }

    [MenuItem("Tools/Map/Level Delivery V3/Validate V3 NavMesh (seed 12345)")]
    public static void ValidateV3NavMesh()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Play Mode에서는 편집기 NavMesh 검증을 실행할 수 없습니다.");

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != MainMapScenePath)
            throw new InvalidOperationException(
                $"4.MapScene을 단독으로 연 뒤 실행하세요. 현재 활성 씬: {scene.path}");
        if (SceneManager.sceneCount != 1)
            throw new InvalidOperationException(
                $"다른 씬의 Collider/NavMesh가 섞이지 않도록 4.MapScene만 열어야 합니다. 현재 로드 씬: {SceneManager.sceneCount}");

        MapGenerator generator = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        MapNavMeshBaker baker = UnityEngine.Object.FindFirstObjectByType<MapNavMeshBaker>();
        NavMeshSurface surface = baker != null ? baker.GetComponent<NavMeshSurface>() : null;
        if (generator == null || generator.ContentSpawner == null || baker == null || surface == null)
            throw new InvalidOperationException(
                "MapGenerator, MapContentSpawner, MapNavMeshBaker 또는 NavMeshSurface 배선이 누락됐습니다.");

        var errors = new List<string>();
        int placementCount = 0;
        int zoneCount = 0;
        int markerCount = 0;
        int validMarkerCount = 0;
        int triangleCount = 0;

        generator.ContentSpawner.ClearGenerated();
        surface.RemoveData();

        try
        {
            List<ZonePlacement> placements = generator.ComputePlacements(12345, 0);
            placementCount = placements.Count;
            if (placementCount != 10)
                errors.Add($"Placement 수 불일치: {placementCount}/10");

            generator.ContentSpawner.SpawnPlacements(generator, placements);
            baker.RebakeNow("V3 편집기 검증 seed 12345");

            GameObject generatedRoot = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == MapContentSpawner.RootName)
                    generatedRoot = root;

            if (generatedRoot == null)
            {
                errors.Add("GeneratedMap 루트를 찾을 수 없습니다.");
            }
            else
            {
                ZoneLayout[] layouts = generatedRoot.GetComponentsInChildren<ZoneLayout>(true);
                zoneCount = layouts.Length;
                if (zoneCount != 10)
                    errors.Add($"생성 Zone 수 불일치: {zoneCount}/10");

                foreach (ZoneLayout layout in layouts)
                {
                    int index = -1;
                    foreach (MonsterSpawnEntry entry in layout.ResolveSpawnEntries())
                    {
                        index++;
                        markerCount++;
                        if (entry.Marker == null)
                        {
                            errors.Add($"Null 마커: {layout.name}[{index}]");
                            continue;
                        }

                        if (!NavMesh.SamplePosition(
                                entry.Marker.position,
                                out NavMeshHit hit,
                                1.5f,
                                NavMesh.AllAreas))
                        {
                            errors.Add(
                                $"NavMesh 없음: {layout.name}/{entry.Marker.name} @ {entry.Marker.position}");
                            continue;
                        }

                        float drift = Vector3.Distance(entry.Marker.position, hit.position);
                        if (drift > 1.5f)
                        {
                            errors.Add(
                                $"NavMesh 거리 초과: {layout.name}/{entry.Marker.name} = {drift:F2}m");
                            continue;
                        }

                        validMarkerCount++;
                    }
                }
            }

            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            triangleCount = triangulation.indices.Length / 3;
            if (triangleCount == 0)
                errors.Add("생성된 NavMesh 삼각형이 0개입니다.");
            if (markerCount != 57)
                errors.Add($"생성 마커 수 불일치: {markerCount}/57");

            ThrowIfErrors(errors, "V3 NavMesh 검증 실패");
            Debug.Log(
                $"[LevelDeliveryV3] V3 NavMesh 검증 통과 — Placement {placementCount}/10, " +
                $"Zone {zoneCount}/10, Marker {validMarkerCount}/{markerCount}, " +
                $"NavMesh Triangles {triangleCount}.");
        }
        finally
        {
            generator.ContentSpawner.ClearGenerated();
            surface.RemoveData();
            foreach (ZoneSlot slot in UnityEngine.Object.FindObjectsByType<ZoneSlot>(FindObjectsSortMode.None))
                slot.ResetRuntime();
        }
    }
}
