#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// 슬롯 저작 데이터 청소 도구.
//
// 존 프리팹이 재생성되면 GUID가 바뀌어 기존 저작 항목이 참조를 잃는다(인스펙터에 Missing으로 남는다).
// 런타임에는 무해하지만(null은 프리팹 비교에 절대 걸리지 않는다) 검증 리포트를 흐리고 인스펙터를
// 부풀려서, 다음 사람이 "저작이 깨졌다"고 착각하고 이미 끝난 재저작을 다시 하게 만든다.
//
// 두 동작을 **일부러 분리**했다:
//   1) Cleanup — 참조를 잃은 잔재만 제거. 되돌릴 게 없는 안전한 청소.
//   2) Trim   — 도달 불가 조합의 저작 제거. 유효한 프리팹의 **손으로 맞춘 위치**가 사라지므로
//               플래그를 되돌리면 다시 맞춰야 한다. 그래서 별도 클릭으로 둔다.
//
// 대상은 열린 씬의 ZoneSlot이다(저작 정본). 실행 후 **씬 저장이 필요하다**.
public static class SlotAuthoringCleanup
{
    [MenuItem("Tools/Map/Authoring/Cleanup Slot Authoring (dead refs)")]
    public static void CleanupDeadRefs()
    {
        List<ZoneSlot> slots = SlotAuthoringModel.GatherSceneSlots();
        if (!Guard(slots)) return;

        var log = new StringBuilder("[SlotCleanup] 참조 잃은 잔재 제거:\n");
        int removedEntries = 0;
        int clearedFields = 0;
        int touched = 0;

        foreach (ZoneSlot slot in slots)
        {
            int before = slot.Rotations?.Count ?? 0;
            int dead = SlotAuthoringModel.CountDeadEntries(slot);
            int fields = CountMissingFields(slot);
            if (dead == 0 && fields == 0) continue;

            Undo.RecordObject(slot, "Cleanup Slot Authoring");

            if (dead > 0) slot.Rotations.RemoveAll(e => e.Prefab == null);
            if (fields > 0) ClearMissingFields(slot);

            EditorUtility.SetDirty(slot);
            removedEntries += dead;
            clearedFields += fields;
            touched++;

            log.AppendLine($"  Slot {slot.SlotID} [{slot.Size}]: 저작 {before} → {slot.Rotations.Count}" +
                           (fields > 0 ? $" / 끊긴 필드 {fields}개 초기화" : ""));
        }

        Finish(slots, log,
            $"완료: 슬롯 {touched}곳에서 잔재 항목 {removedEntries}개 + 끊긴 필드 {clearedFields}개 제거.",
            removedEntries + clearedFields);
    }

    [MenuItem("Tools/Map/Authoring/Trim Unreachable Placements")]
    public static void TrimUnreachable()
    {
        ZoneLayoutCatalogSO catalog = SlotAuthoringModel.LoadCatalog();
        if (catalog == null) return;

        List<ZoneSlot> slots = SlotAuthoringModel.GatherSceneSlots();
        if (!Guard(slots)) return;

        List<SlotAuthoringModel.SlotPlan> plans = SlotAuthoringModel.BuildPlans(slots, catalog);

        // 먼저 무엇이 사라지는지 전부 보여준다 — 손으로 맞춘 위치가 날아가는 동작이라 조용히 하면 안 된다.
        var preview = new StringBuilder();
        int total = 0;
        foreach (SlotAuthoringModel.SlotPlan plan in plans)
        {
            List<string> stray = StrayNames(plan);
            if (stray.Count == 0) continue;
            total += stray.Count;
            preview.AppendLine($"  Slot {plan.Slot.SlotID} [{plan.Slot.Size}] (가능역할 {string.Join("/", plan.PossibleRoles)}): " +
                               string.Join(", ", stray));
        }

        if (total == 0)
        {
            Debug.Log("[SlotCleanup] 도달 불가 저작 없음 — 정리할 것이 없습니다.");
            return;
        }

        bool ok = EditorUtility.DisplayDialog(
            "도달 불가 저작 제거",
            $"현재 플래그·카탈로그 기준으로 절대 뽑히지 않는 저작 {total}개를 제거합니다.\n\n" +
            preview +
            "\n손으로 맞춘 위치가 사라집니다. 슬롯 플래그(Quest/Boss/Spawn 후보)나 FixedPrefab을 " +
            "되돌리면 그 조합은 다시 맞춰야 합니다.\n\n계속할까요?",
            "제거", "취소");
        if (!ok) return;

        var log = new StringBuilder("[SlotCleanup] 도달 불가 저작 제거:\n");
        int removed = 0;
        foreach (SlotAuthoringModel.SlotPlan plan in plans)
        {
            ZoneSlot slot = plan.Slot;
            if (StrayNames(plan).Count == 0) continue;

            int before = slot.Rotations.Count;
            Undo.RecordObject(slot, "Trim Unreachable Placements");
            slot.Rotations.RemoveAll(e => e.Prefab != null && !plan.Reachable.Contains(e.Prefab));
            EditorUtility.SetDirty(slot);

            removed += before - slot.Rotations.Count;
            log.AppendLine($"  Slot {slot.SlotID} [{slot.Size}]: 저작 {before} → {slot.Rotations.Count}");
        }

        Finish(slots, log, $"완료: 도달 불가 저작 {removed}개 제거.", removed);
    }

    static List<string> StrayNames(SlotAuthoringModel.SlotPlan plan)
    {
        var stray = new List<string>();
        if (plan.Slot.Rotations == null) return stray;

        foreach (ZoneSlot.RotationEntry entry in plan.Slot.Rotations)
            if (entry.Prefab != null && !plan.Reachable.Contains(entry.Prefab))
                stray.Add(entry.Prefab.name);
        return stray;
    }

    static bool Guard(List<ZoneSlot> slots)
    {
        if (slots.Count > 0) return true;

        Debug.LogError(
            "[SlotCleanup] 열린 씬에 ZoneSlot이 없습니다. 저작 정본은 씬의 Stage1 인스턴스이므로 " +
            "4.MapScene을 열고 다시 실행하세요.");
        return false;
    }

    static void Finish(List<ZoneSlot> slots, StringBuilder log, string tail, int changed)
    {
        if (changed == 0)
        {
            Debug.Log("[SlotCleanup] 제거할 항목 없음.");
            return;
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(slots[0].gameObject.scene);
        log.AppendLine(tail + " — **씬 저장 필요**(Ctrl+S).");
        Debug.Log(log.ToString());
    }

    /// <summary>
    /// 삭제된 에셋을 가리키는 필드 수. `== null`만으로는 "비어 있음"과 "참조가 끊김"을 구분할 수 없어
    /// 직렬화된 instanceID를 본다(끊긴 참조는 instanceID가 남고 objectReferenceValue만 null이다).
    /// </summary>
    static int CountMissingFields(ZoneSlot slot)
    {
        int n = 0;
        var so = new SerializedObject(slot);
        foreach (string path in new[] { "FixedPrefab", "QuestPrefab" })
        {
            SerializedProperty p = so.FindProperty(path);
            if (p != null && p.objectReferenceValue == null && p.objectReferenceInstanceIDValue != 0) n++;
        }
        return n;
    }

    static void ClearMissingFields(ZoneSlot slot)
    {
        var so = new SerializedObject(slot);
        bool dirty = false;
        foreach (string path in new[] { "FixedPrefab", "QuestPrefab" })
        {
            SerializedProperty p = so.FindProperty(path);
            if (p == null || p.objectReferenceValue != null || p.objectReferenceInstanceIDValue == 0) continue;

            p.objectReferenceInstanceIDValue = 0;
            dirty = true;
        }
        if (dirty) so.ApplyModifiedProperties();
    }
}
#endif
