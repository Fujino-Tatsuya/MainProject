using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// 저작 도구 — 플레이어 프리팹의 PlayerEncounterLock 부착과 배선을 복구한다 (2026-07-30).
//
// 배경: 이 배선이 프리팹 머지에서 세 번 유실됐다.
//   ① feature/PlayerSkill 머지 → PlayerEncounterLock 블록 + 루트 등록이 통째로 사라짐
//   ② 같은 머지에서 PlayerLifeInputPolicy 직렬화 필드 6개가 전부 비어 있었음
//   ③ Paladin 마이그레이션 → Player.prefab 에 복원한 것이 Paladin.prefab 에는 없음
//
// 두 클래스 모두 사용처가 전부 null 가드라 **예외 없이 조용히 무동작**한다. 그래서 유실되면
// "보스 등장 연출 중에 움직여진다" / "사망·유령 상태 입력이 안 막힌다" 같은 증상만 남고,
// 콘솔에는 아무것도 안 뜬다. 손으로 YAML 을 고치는 대신 도구로 만들어 둔 이유가 이것이다.
//
// 멱등이다. 이미 붙어 있고 배선이 채워져 있으면 아무것도 하지 않는다.
// 머지 후 프리팹이 의심되면 그냥 다시 돌리면 된다.
public static class PlayerEncounterLockAuthoring
{
    const string PlayerPrefabFolder = "Assets/2.Prefabs/Player";

    [MenuItem("Tools/Player/Authoring/Repair PlayerEncounterLock Wiring")]
    public static void Repair()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PlayerPrefabFolder });
        var sb = new StringBuilder("[EncounterLock] 배선 점검\n");
        int touched = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                // Player 컴포넌트가 없으면 플레이어 프리팹이 아니다 (무기·이펙트 등은 건너뜀).
                Player player = root.GetComponentInChildren<Player>(true);
                if (player == null)
                    continue;

                var changes = new List<string>();
                bool changed = Wire(root, player, changes);

                sb.AppendLine($"  {(changed ? "수정" : "정상")}  {path}");
                foreach (string line in changes)
                    sb.AppendLine($"          {line}");

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    touched++;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        sb.AppendLine($"  → 프리팹 {touched}개 수정");
        Debug.Log(sb.ToString());
    }

    static bool Wire(GameObject root, Player player, List<string> changes)
    {
        bool changed = false;

        // PlayerEncounterLock 은 Player 컴포넌트와 같은 GameObject 에 둔다 (Player.prefab 과 동일 배치).
        GameObject host = player.gameObject;
        var encounterLock = root.GetComponentInChildren<PlayerEncounterLock>(true);
        if (encounterLock == null)
        {
            encounterLock = host.AddComponent<PlayerEncounterLock>();
            changes.Add("PlayerEncounterLock 컴포넌트 추가");
            changed = true;
        }

        // 7개 참조 — 프리팹 계층에서 찾아 채운다. fileID 를 하드코딩하지 않는다.
        changed |= SetIfEmpty(encounterLock, "player", player, changes);
        changed |= SetIfEmpty(encounterLock, "stateController",
                              root.GetComponentInChildren<PlayerStateController>(true), changes);
        changed |= SetIfEmpty(encounterLock, "invulnerability",
                              root.GetComponentInChildren<PlayerInvulnerability>(true), changes);
        changed |= SetIfEmpty(encounterLock, "statusEffects",
                              root.GetComponentInChildren<StatusEffectController>(true), changes);
        changed |= SetIfEmpty(encounterLock, "defaultAttack",
                              root.GetComponentInChildren<DefaultAttackController>(true), changes);
        changed |= SetIfEmpty(encounterLock, "skillController",
                              root.GetComponentInChildren<PlayerSkillController>(true), changes);
        changed |= SetIfEmpty(encounterLock, "body",
                              root.GetComponentInChildren<Rigidbody>(true), changes);

        // PlayerLifeInputPolicy 쪽 배선 — encounterLock 이 비어 있으면 여기서 채운다.
        var policy = root.GetComponentInChildren<PlayerLifeInputPolicy>(true);
        if (policy != null)
        {
            changed |= SetIfEmpty(policy, "encounterLock", encounterLock, changes);
            changed |= SetIfEmpty(policy, "player", player, changes);
            changed |= SetIfEmpty(policy, "inputReader",
                                  root.GetComponentInChildren<PlayerInputReader>(true), changes);
            changed |= SetIfEmpty(policy, "stateController",
                                  root.GetComponentInChildren<PlayerStateController>(true), changes);
        }

        return changed;
    }

    /// <summary>비어 있는 참조만 채운다. 이미 값이 있으면 손대지 않는다(저작자 의도 보존).</summary>
    static bool SetIfEmpty(Object target, string fieldName, Object value, List<string> changes)
    {
        var so = new SerializedObject(target);
        SerializedProperty prop = so.FindProperty(fieldName);

        if (prop == null)
        {
            changes.Add($"⚠️ 필드 없음: {target.GetType().Name}.{fieldName} (스크립트가 바뀌었는지 확인)");
            return false;
        }

        if (prop.objectReferenceValue != null)
            return false;

        if (value == null)
        {
            changes.Add($"⚠️ 대상 없음: {target.GetType().Name}.{fieldName} — 프리팹에 해당 컴포넌트가 없다");
            return false;
        }

        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
        changes.Add($"{target.GetType().Name}.{fieldName} → {value.GetType().Name}");
        return true;
    }
}
