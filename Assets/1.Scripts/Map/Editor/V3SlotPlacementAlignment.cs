#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 열린 MapScene의 V3 슬롯 저작 좌표를 조사하고, 서로 다른 SlotID가 같은 물리 구역을
/// 사용하는 실수를 검출한다. 저작 정본은 프리팹 에셋이 아니라 씬 인스턴스다.
/// </summary>
public static class V3SlotPlacementAlignment
{
    const string MainMapScenePath = "Assets/0.Scenes/MainFlow/4.MapScene.unity";
    const string V3StagePrefabGuid = "9e5ec8ccf89822c42b0e42c35867ba40";
    const float TransformTolerance = 0.001f;
    const float SameBayDistance = 2f;
    const float MaxTrustedRelativeOffset = 2f;

    readonly struct BaySpec
    {
        public readonly int SlotID;
        public readonly ZoneSize Size;
        public readonly Vector3 Position;
        public readonly string ReferencePrefabName;

        public BaySpec(int slotID, ZoneSize size, Vector3 position, string referencePrefabName)
        {
            SlotID = slotID;
            Size = size;
            Position = position;
            ReferencePrefabName = referencePrefabName;
        }
    }

    // D:/unity/p_MT/level_project/Assets/level/Scenes/all_mesh.unity에서 확인한
    // V3 Stage 원본의 10개 실제 구역. M_Type_C의 원본 x=129.4 위치는 Stage 밖의
    // 제작용 배치이므로 제외하고, 런타임 고정 M_Type_C는 M_Type_B 구역을 사용한다.
    static readonly BaySpec[] V3Bays =
    {
        new BaySpec(0, ZoneSize.Large,  new Vector3(-39.602085f, 0f,  20.000006f), "PF_Zone_L_Type_B_V3"),
        new BaySpec(1, ZoneSize.Large,  new Vector3( 39.597910f, 0f, -29.600002f), "PF_Zone_L_Type_B_V3"),
        new BaySpec(2, ZoneSize.Large,  new Vector3(  9.997964f, 0f,  49.600030f), "PF_Zone_L_Type_B_V3"),
        new BaySpec(3, ZoneSize.Medium, new Vector3(-68.995480f, 0f, -19.800306f), "PF_Zone_M_Type_C_V3"),
        new BaySpec(4, ZoneSize.Medium, new Vector3( 39.399770f, 0f,   9.795486f), "PF_Zone_M_Type_B_V3"),
        new BaySpec(5, ZoneSize.Small,  new Vector3(  0.001200f, 0f, -39.799713f), "PF_Zone_S_Type_Start_V3"),
        new BaySpec(6, ZoneSize.Small,  new Vector3( 49.601220f, 0f,  39.400280f), "PF_Zone_S_Type_A_V3"),
        new BaySpec(7, ZoneSize.Small,  new Vector3(-29.598747f, 0f,  59.400290f), "PF_Zone_S_Type_Start_V3"),
        new BaySpec(8, ZoneSize.Medium, Vector3.zero,                                  "PF_Zone_Quest_02_V3"),
        new BaySpec(9, ZoneSize.Medium, new Vector3(-29.599980f, 0f, -29.600010f), "PF_Zone_M_Type_A_V3"),
    };

    [MenuItem("Tools/Map/Authoring/V3 Slots/Align Slot 4 and 9 Baselines From Authored M Zones")]
    public static void AlignRecentlyAuthoredMediumSlotBaselines()
    {
        ZoneLayoutCatalogSO catalog = SlotAuthoringModel.LoadCatalog();
        if (catalog == null) return;

        List<ZoneSlot> slots = SlotAuthoringModel.GatherSceneSlots();
        Dictionary<int, SlotAuthoringModel.SlotPlan> planByID =
            SlotAuthoringModel.BuildPlans(slots, catalog).ToDictionary(plan => plan.Slot.SlotID);
        BaySpec[] targets = V3Bays.Where(bay => bay.SlotID == 4 || bay.SlotID == 9).ToArray();
        var errors = new List<string>();

        foreach (BaySpec target in targets)
        {
            if (!planByID.TryGetValue(target.SlotID, out SlotAuthoringModel.SlotPlan plan))
            {
                errors.Add($"Slot {target.SlotID} is missing.");
                continue;
            }

            GameObject reference = plan.Reachable.FirstOrDefault(prefab => prefab.name == target.ReferencePrefabName);
            if (reference == null)
            {
                errors.Add($"Slot {target.SlotID} cannot reach {target.ReferencePrefabName}.");
                continue;
            }

            if (!plan.Slot.TryGetPosition(reference, out Vector3 authoredPosition))
            {
                errors.Add($"Slot {target.SlotID}/{target.ReferencePrefabName} has no authored position.");
                continue;
            }

            Vector2 delta = new Vector2(authoredPosition.x - target.Position.x, authoredPosition.z - target.Position.z);
            if (delta.sqrMagnitude > TransformTolerance * TransformTolerance)
                errors.Add($"Slot {target.SlotID}/{target.ReferencePrefabName} position changed after capture: {Format(authoredPosition)}.");
        }

        if (errors.Count > 0)
        {
            foreach (string error in errors)
                Debug.LogError("[V3SlotAlignment] " + error);
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Align V3 Slot 4 and 9 Baselines");

        foreach (BaySpec target in targets)
        {
            ZoneSlot slot = planByID[target.SlotID].Slot;
            Undo.RecordObject(slot.transform, "Align V3 Slot Baseline");
            slot.transform.position = target.Position;
            EditorUtility.SetDirty(slot.transform);
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(targets.Select(target => planByID[target.SlotID].Slot)
            .First().gameObject.scene);

        Debug.Log(
            $"[V3SlotAlignment] Authored M Zone baselines aligned: " +
            $"Slot 4={Format(planByID[4].Slot.transform.position)}, " +
            $"Slot 9={Format(planByID[9].Slot.transform.position)}. " +
            "Stored prefab positions and rotations were not changed. Scene save is required.");
    }

    [MenuItem("Tools/Map/Authoring/V3 Slots/Rotate Main V3 Map 180 (One Time)")]
    public static void RotateMainV3Map180OneTime()
    {
        if (!TryGetMainV3Stage(out GameObject stage, out List<ZoneSlot> slots, requireUnrotated: true))
            return;

        var oldSlotWorldPositions = slots.ToDictionary(slot => slot, slot => slot.transform.position);
        var oldEntries = new Dictionary<ZoneSlot, List<ZoneSlot.RotationEntry>>();
        foreach (ZoneSlot slot in slots)
            oldEntries.Add(slot, new List<ZoneSlot.RotationEntry>(slot.Rotations));

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Rotate Main V3 Map 180");
        Undo.RecordObject(stage.transform, "Rotate V3 Stage 180");

        foreach (ZoneSlot slot in slots)
        {
            Undo.RecordObject(slot, "Rotate V3 Zone Placements 180");
            for (int i = 0; i < slot.Rotations.Count; i++)
            {
                ZoneSlot.RotationEntry entry = slot.Rotations[i];
                entry.YawSteps = (entry.YawSteps + 2) & 3;
                if (entry.HasPosition)
                    entry.Position = RotateAroundWorldOrigin180(entry.Position);
                slot.Rotations[i] = entry;
            }

            EditorUtility.SetDirty(slot);
        }

        stage.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        EditorUtility.SetDirty(stage.transform);

        if (!ValidateOneTimeRotation(stage, slots, oldSlotWorldPositions, oldEntries, out string validationError))
        {
            Undo.RevertAllDownToGroup(undoGroup);
            Debug.LogError("[V3MapOrientation] 180-degree rotation validation failed; all changes were reverted.\n" + validationError);
            return;
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(stage.scene);

        int placementCount = slots.Sum(slot => slot.Rotations.Count(entry => entry.HasPosition));
        int yawCount = slots.Sum(slot => slot.Rotations.Count);
        Debug.Log(
            $"[V3MapOrientation] Main V3 map rotated 180 degrees around world origin. " +
            $"Stage 1, Slots {slots.Count}, Positions {placementCount}, Yaws {yawCount}. " +
            "Scene is dirty and has not been saved.", stage);
    }

    [MenuItem("Tools/Map/Authoring/V3 Slots/Validate Main V3 Map 180 Orientation")]
    public static void ValidateMainV3Map180Orientation()
    {
        if (!TryGetMainV3Stage(out GameObject stage, out List<ZoneSlot> slots, requireUnrotated: false))
            return;

        if (!IsRotation(stage.transform, Quaternion.Euler(0f, 180f, 0f)))
        {
            Debug.LogError(
                $"[V3MapOrientation] Stage rotation must be exactly Y=180 degrees. Current={stage.transform.eulerAngles}.",
                stage);
            return;
        }

        int invalidEntries = 0;
        foreach (ZoneSlot slot in slots)
        foreach (ZoneSlot.RotationEntry entry in slot.Rotations)
        {
            if (entry.YawSteps < 0 || entry.YawSteps > 3 ||
                (entry.HasPosition && !IsFinite(entry.Position)))
                invalidEntries++;
        }

        if (invalidEntries > 0)
        {
            Debug.LogError($"[V3MapOrientation] Invalid placement entries: {invalidEntries}.", stage);
            return;
        }

        Debug.Log(
            $"[V3MapOrientation] Validation passed - Stage Y=180, Slots {slots.Count}, " +
            $"Positions {slots.Sum(slot => slot.Rotations.Count(entry => entry.HasPosition))}.",
            stage);
    }

    static bool TryGetMainV3Stage(
        out GameObject stage,
        out List<ZoneSlot> slots,
        bool requireUnrotated)
    {
        stage = null;
        slots = new List<ZoneSlot>();

        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != MainMapScenePath)
        {
            Debug.LogError(
                $"[V3MapOrientation] Open '{MainMapScenePath}' as the active scene first. Current='{scene.path}'.");
            return false;
        }

        string stagePath = AssetDatabase.GUIDToAssetPath(V3StagePrefabGuid);
        List<GameObject> candidates = scene.GetRootGameObjects()
            .Where(root => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == stagePath)
            .ToList();

        if (candidates.Count != 1)
        {
            Debug.LogError($"[V3MapOrientation] Expected exactly one V3 Stage root, found {candidates.Count}.");
            return false;
        }

        stage = candidates[0];
        Transform stageTransform = stage.transform;
        if (stageTransform.position.sqrMagnitude > TransformTolerance * TransformTolerance)
        {
            Debug.LogError($"[V3MapOrientation] Stage must be at world origin. Current={stageTransform.position}.", stage);
            return false;
        }
        if ((stageTransform.lossyScale - Vector3.one).sqrMagnitude > TransformTolerance * TransformTolerance)
        {
            Debug.LogError($"[V3MapOrientation] Stage scale must be (1,1,1). Current={stageTransform.lossyScale}.", stage);
            return false;
        }
        if (requireUnrotated && !IsRotation(stageTransform, Quaternion.identity))
        {
            Debug.LogError(
                $"[V3MapOrientation] One-time rotation refused: Stage is already rotated. " +
                $"Current={stageTransform.eulerAngles}.", stage);
            return false;
        }

        List<ZoneSlot> stageSlots = stage.GetComponentsInChildren<ZoneSlot>(true)
            .OrderBy(slot => slot.SlotID)
            .ToList();
        List<ZoneSlot> allSceneSlots = SlotAuthoringModel.GatherSceneSlots();
        if (stageSlots.Count != V3Bays.Length || allSceneSlots.Count != stageSlots.Count ||
            allSceneSlots.Any(slot => !stageSlots.Contains(slot)))
        {
            Debug.LogError(
                $"[V3MapOrientation] All scene slots must be direct descendants of the single V3 Stage. " +
                $"StageSlots={stageSlots.Count}, SceneSlots={allSceneSlots.Count}, Expected={V3Bays.Length}.", stage);
            return false;
        }

        slots = stageSlots;
        return true;
    }

    static bool ValidateOneTimeRotation(
        GameObject stage,
        List<ZoneSlot> slots,
        Dictionary<ZoneSlot, Vector3> oldSlotWorldPositions,
        Dictionary<ZoneSlot, List<ZoneSlot.RotationEntry>> oldEntries,
        out string error)
    {
        var errors = new List<string>();
        if (!IsRotation(stage.transform, Quaternion.Euler(0f, 180f, 0f)))
            errors.Add($"Stage rotation is {stage.transform.eulerAngles}, expected (0,180,0).");

        foreach (ZoneSlot slot in slots)
        {
            Vector3 expectedSlotPosition = RotateAroundWorldOrigin180(oldSlotWorldPositions[slot]);
            if ((slot.transform.position - expectedSlotPosition).sqrMagnitude > TransformTolerance * TransformTolerance)
                errors.Add($"Slot {slot.SlotID} position mismatch: {slot.transform.position} != {expectedSlotPosition}.");

            List<ZoneSlot.RotationEntry> before = oldEntries[slot];
            if (before.Count != slot.Rotations.Count)
            {
                errors.Add($"Slot {slot.SlotID} entry count changed: {before.Count} -> {slot.Rotations.Count}.");
                continue;
            }

            for (int i = 0; i < before.Count; i++)
            {
                ZoneSlot.RotationEntry oldEntry = before[i];
                ZoneSlot.RotationEntry newEntry = slot.Rotations[i];
                if (newEntry.YawSteps != ((oldEntry.YawSteps + 2) & 3))
                    errors.Add($"Slot {slot.SlotID} entry {i} yaw was not rotated 180 degrees.");

                Vector3 expectedPosition = oldEntry.HasPosition
                    ? RotateAroundWorldOrigin180(oldEntry.Position)
                    : oldEntry.Position;
                if ((newEntry.Position - expectedPosition).sqrMagnitude > TransformTolerance * TransformTolerance)
                    errors.Add($"Slot {slot.SlotID} entry {i} position mismatch.");
            }
        }

        error = string.Join("\n", errors);
        return errors.Count == 0;
    }

    static Vector3 RotateAroundWorldOrigin180(Vector3 position) =>
        new Vector3(-position.x, position.y, -position.z);

    static bool IsRotation(Transform transform, Quaternion expected) =>
        Quaternion.Angle(transform.rotation, expected) <= TransformTolerance;

    static bool IsFinite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z);

    [MenuItem("Tools/Map/Authoring/V3 Slots/Apply Canonical V3 Stage Bays")]
    public static void ApplyCanonicalV3StageBays()
    {
        ZoneLayoutCatalogSO catalog = SlotAuthoringModel.LoadCatalog();
        if (catalog == null) return;

        List<ZoneSlot> slots = SlotAuthoringModel.GatherSceneSlots();
        List<SlotAuthoringModel.SlotPlan> plans = SlotAuthoringModel.BuildPlans(slots, catalog);
        Dictionary<int, SlotAuthoringModel.SlotPlan> planByID = plans.ToDictionary(p => p.Slot.SlotID);
        var errors = new List<string>();

        if (slots.Count != V3Bays.Length)
            errors.Add($"ZoneSlot 수가 {V3Bays.Length}개가 아님: {slots.Count}");

        foreach (BaySpec bay in V3Bays)
        {
            if (!planByID.TryGetValue(bay.SlotID, out SlotAuthoringModel.SlotPlan plan))
            {
                errors.Add($"Slot {bay.SlotID} 누락");
                continue;
            }

            if (plan.Slot.Size != bay.Size)
                errors.Add($"Slot {bay.SlotID} Size 불일치: {plan.Slot.Size} != {bay.Size}");

            GameObject reference = plan.Reachable.FirstOrDefault(p => p.name == bay.ReferencePrefabName);
            if (reference == null)
                errors.Add($"Slot {bay.SlotID} 기준 프리팹 누락: {bay.ReferencePrefabName}");
            else if (!plan.Slot.TryGetPosition(reference, out _))
                errors.Add($"Slot {bay.SlotID} 기준 프리팹 위치 미저작: {bay.ReferencePrefabName}");

            foreach (GameObject prefab in plan.Reachable)
            {
                if (!plan.Slot.TryGetYaw(prefab, out _))
                    errors.Add($"Slot {bay.SlotID} 회전 미저작: {prefab.name}");
                if (!plan.Slot.TryGetPosition(prefab, out _))
                    errors.Add($"Slot {bay.SlotID} 위치 미저작: {prefab.name}");
            }
        }

        if (planByID.Count != V3Bays.Length)
            errors.Add("SlotID 중복 또는 0~9 이외 SlotID가 존재함");

        if (errors.Count > 0)
        {
            foreach (string error in errors)
                Debug.LogError("[V3SlotAlignment] " + error);
            Debug.LogError($"[V3SlotAlignment] 정렬 중단 — 사전 검증 오류 {errors.Count}개.");
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Align V3 Stage Slot Bays");
        var resetOffsets = new List<string>();

        foreach (BaySpec bay in V3Bays)
        {
            SlotAuthoringModel.SlotPlan plan = planByID[bay.SlotID];
            ZoneSlot slot = plan.Slot;
            GameObject reference = plan.Reachable.First(p => p.name == bay.ReferencePrefabName);
            slot.TryGetPosition(reference, out Vector3 referencePosition);

            Undo.RecordObject(slot.transform, "Align V3 Slot Transform");
            Undo.RecordObject(slot, "Align V3 Slot Placements");
            slot.transform.position = bay.Position;

            foreach (GameObject prefab in plan.Reachable)
            {
                slot.TryGetYaw(prefab, out int yaw);
                slot.TryGetPosition(prefab, out Vector3 oldPosition);
                Vector3 relativeOffset = oldPosition - referencePosition;

                // 같은 Slot인데 다른 구형 구역 좌표가 섞인 항목은 수십 m짜리 오프셋이 된다.
                // 그 값은 피벗 미세 보정이 아니므로 기준점으로 안전하게 초기화한다.
                if (new Vector2(relativeOffset.x, relativeOffset.z).magnitude > MaxTrustedRelativeOffset)
                {
                    resetOffsets.Add(
                        $"Slot {bay.SlotID}/{prefab.name}: 기존 상대 오프셋 {Format(relativeOffset)} 초기화");
                    relativeOffset = Vector3.zero;
                }

                slot.SetPlacement(prefab, yaw, bay.Position + relativeOffset);
            }

            EditorUtility.SetDirty(slot);
            EditorUtility.SetDirty(slot.transform);
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(slots[0].gameObject.scene);

        List<string> collisions = FindCrossSlotBayCollisions(plans);
        if (collisions.Count > 0)
        {
            foreach (string collision in collisions)
                Debug.LogError("[V3SlotAlignment] 정렬 후 중복: " + collision);
            Debug.LogError($"[V3SlotAlignment] 정렬 후 검증 실패 — 중복 {collisions.Count}개. Undo로 되돌리세요.");
            return;
        }

        foreach (string reset in resetOffsets)
            Debug.LogWarning("[V3SlotAlignment] " + reset);

        Debug.Log(
            $"[V3SlotAlignment] V3 Stage 슬롯 좌표 재정렬 완료 — Slot {V3Bays.Length}, " +
            $"구형 좌표 오프셋 초기화 {resetOffsets.Count}, Slot 간 좌표 중복 0. 씬 저장이 필요합니다.");
    }

    [MenuItem("Tools/Map/Authoring/V3 Slots/Report Placement Bays")]
    public static void ReportPlacementBays()
    {
        ZoneLayoutCatalogSO catalog = SlotAuthoringModel.LoadCatalog();
        if (catalog == null) return;

        List<ZoneSlot> slots = SlotAuthoringModel.GatherSceneSlots();
        if (slots.Count == 0)
        {
            Debug.LogError("[V3SlotAlignment] 열린 씬에 ZoneSlot이 없습니다.");
            return;
        }

        List<SlotAuthoringModel.SlotPlan> plans = SlotAuthoringModel.BuildPlans(slots, catalog);
        var report = new StringBuilder();
        report.AppendLine($"[V3SlotAlignment] 씬 '{slots[0].gameObject.scene.name}' 저장 좌표 보고");

        foreach (SlotAuthoringModel.SlotPlan plan in plans)
        {
            ZoneSlot slot = plan.Slot;
            report.AppendLine($"Slot {slot.SlotID} [{slot.Size}] baseline={Format(slot.transform.position)}");
            foreach (GameObject prefab in plan.Reachable.OrderBy(p => p.name))
            {
                bool hasYaw = slot.TryGetYaw(prefab, out int yaw);
                bool hasPosition = slot.TryGetPosition(prefab, out Vector3 position);
                report.AppendLine(
                    $"    {prefab.name}: pos={(hasPosition ? Format(position) : "MISSING")}, " +
                    $"yaw={(hasYaw ? (yaw * 90).ToString() : "MISSING")}");
            }
        }

        List<string> collisions = FindCrossSlotBayCollisions(plans);
        if (collisions.Count == 0)
            report.AppendLine("서로 다른 SlotID의 저장 좌표 중복: 0");
        else
        {
            report.AppendLine($"서로 다른 SlotID의 저장 좌표 중복: {collisions.Count}");
            foreach (string collision in collisions)
                report.AppendLine("    " + collision);
        }

        if (collisions.Count > 0)
            Debug.LogWarning(report.ToString());
        else
            Debug.Log(report.ToString());
    }

    static List<string> FindCrossSlotBayCollisions(List<SlotAuthoringModel.SlotPlan> plans)
    {
        var authored = new List<(ZoneSlot slot, GameObject prefab, Vector3 position)>();
        foreach (SlotAuthoringModel.SlotPlan plan in plans)
            foreach (GameObject prefab in plan.Reachable)
                if (plan.Slot.TryGetPosition(prefab, out Vector3 position))
                    authored.Add((plan.Slot, prefab, position));

        var collisions = new List<string>();
        for (int i = 0; i < authored.Count; i++)
        for (int j = i + 1; j < authored.Count; j++)
        {
            var a = authored[i];
            var b = authored[j];
            if (a.slot.SlotID == b.slot.SlotID || a.slot.Size != b.slot.Size)
                continue;

            Vector2 axz = new Vector2(a.position.x, a.position.z);
            Vector2 bxz = new Vector2(b.position.x, b.position.z);
            float distance = Vector2.Distance(axz, bxz);
            if (distance > SameBayDistance)
                continue;

            collisions.Add(
                $"Slot {a.slot.SlotID}/{a.prefab.name} {Format(a.position)} ↔ " +
                $"Slot {b.slot.SlotID}/{b.prefab.name} {Format(b.position)} (XZ {distance:F2}m)");
        }

        return collisions;
    }

    static string Format(Vector3 value) => $"({value.x:F6},{value.y:F6},{value.z:F6})";
}
#endif
