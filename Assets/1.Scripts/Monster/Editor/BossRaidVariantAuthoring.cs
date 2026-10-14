using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 저작 도구 — 23호 **레이드 변형**(PLAN-boss-raid.md W1). 기존 TwentyThree.prefab · No23.asset 은 읽기만 한다.
//
// 멱등이다. 없는 것만 만든다:
//   · No23_Raid.asset — No23 복제 + 레이드 초기값. **이미 있으면 수치는 안 건드린다**(튜닝값 보호).
//   · TwentyThree_Raid.prefab — TwentyThree 의 **Prefab Variant**(기존 프리팹 수정이 그대로 따라온다 — 비교가 공정하다).
//     Solo(복제본)와 다르게 Variant 인 이유: 레이드는 모델·리그·패턴이 같고 데이터만 다르다.
//   · DefaultNetworkPrefabs 등록 · TutorialGuideTexts 의 BOSS_RAID_* 자막 행 · 열린 씬의 BossEncounterDirector.raidBossPrefab.
public static class BossRaidVariantAuthoring
{
    public const string BuildMenu = "Tools/Boss/레이드 변형 — 만들기 (멱등)";

    const string SrcDataPath = "Assets/2.Prefabs/Monster/Data/No23.asset";
    const string RaidDataPath = "Assets/2.Prefabs/Monster/Data/No23_Raid.asset";
    const string SrcPrefabPath = "Assets/2.Prefabs/Monster/Boss/TwentyThree.prefab";
    const string RaidPrefabPath = "Assets/2.Prefabs/Monster/Boss/TwentyThree_Raid.prefab";
    const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";
    const string GuideTextsPath = "Assets/9.ScriptableObject/Tutorial/Guide/TutorialGuideTexts.asset";

    // 인원 HP: 1인 기준 저작 + 배수. 목표 1인 5분 · 3인 7분(팀장 10-14)에서 DPS ∝ 인원으로 역산 → 1 / 2.4 / 4.2.
    // 3인 HP 가 기존 No23(10000)과 같아지도록 1인 = 10000 / 4.2 ≈ 2400. 전부 W4 실측으로 조정한다.
    static readonly float[] PartyHpMultipliers = { 1f, 2.4f, 4.2f };
    const float HpBaseDivisor = 4.2f;
    const float HitCapRatio = 0.9f;

    // 웰즈 자막(초안 — 팀장 확인 전). id 는 BOSS_RAID_<BossPhaseSequence 이름>.
    static readonly (string id, string text, float seconds)[] Subtitles =
    {
        ("BOSS_RAID_RaidDroneCharge",  "웰즈: \"가족에게 전력을. 우리 아이, 아직 멈추면 안 돼.\"", 4f),
        ("BOSS_RAID_RaidRescue",       "웰즈: \"새 가족을 데려와. 하나쯤은 남겨 둬야지.\"", 4f),
        ("BOSS_RAID_RaidSelfDestruct", "웰즈: \"고장 난 가족은 필요 없어. 같이 사라져.\"", 4f),
    };

    [MenuItem(BuildMenu)]
    public static void Build()
    {
        var log = new StringBuilder("[레이드 변형] 만들기\n");

        BossDataSO data = EnsureData(log);
        GameObject prefab = data != null ? EnsurePrefab(data, log) : null;
        if (prefab != null)
        {
            EnsureNetworkPrefab(prefab, log);
            WireOpenScenes(prefab, log);
        }
        EnsureSubtitles(log);

        AssetDatabase.SaveAssets();
        Debug.Log(log.ToString());
    }

    static BossDataSO EnsureData(StringBuilder log)
    {
        var existing = AssetDatabase.LoadAssetAtPath<BossDataSO>(RaidDataPath);
        if (existing != null)
        {
            log.AppendLine("  · No23_Raid.asset 있음 — 수치는 건드리지 않음");
            return existing;
        }

        var src = AssetDatabase.LoadAssetAtPath<BossDataSO>(SrcDataPath);
        if (src == null) { log.AppendLine("  ✗ No23.asset 없음"); return null; }
        if (!AssetDatabase.CopyAsset(SrcDataPath, RaidDataPath)) { log.AppendLine("  ✗ 복제 실패"); return null; }

        var data = AssetDatabase.LoadAssetAtPath<BossDataSO>(RaidDataPath);
        data.maxHp = Mathf.RoundToInt(src.maxHp / HpBaseDivisor / 100f) * 100;
        data.partyHpMultipliers = (float[])PartyHpMultipliers.Clone();
        data.playerHitDamageCapRatio = HitCapRatio;
        // 구간 각본(PLAN §2): ②75 전력 공급 · ③45 새 가족 · ④20 폐기 처분. 배수 ① 1.0 → 1.15 → 1.3.
        data.phases = new[]
        {
            new BossPhaseEntry { hpThreshold = 0.75f, sequence = BossPhaseSequence.RaidDroneCharge,  damageMultiplier = 1.15f, speedMultiplier = 1.1f },
            new BossPhaseEntry { hpThreshold = 0.45f, sequence = BossPhaseSequence.RaidRescue,       damageMultiplier = 1.3f,  speedMultiplier = 1.2f },
            new BossPhaseEntry { hpThreshold = 0.20f, sequence = BossPhaseSequence.RaidSelfDestruct, damageMultiplier = 1.3f,  speedMultiplier = 1.2f },
        };
        EditorUtility.SetDirty(data);
        log.AppendLine($"  ✓ No23_Raid.asset 생성 — 1인 HP {data.maxHp} × [{string.Join(", ", PartyHpMultipliers)}] · 피해 상한 {HitCapRatio:P0} · 페이즈 3");
        return data;
    }

    static GameObject EnsurePrefab(BossDataSO data, StringBuilder log)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RaidPrefabPath);
        if (prefab == null)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(SrcPrefabPath);
            if (src == null) { log.AppendLine("  ✗ TwentyThree.prefab 없음"); return null; }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            try
            {
                inst.name = "TwentyThree_Raid";
                prefab = PrefabUtility.SaveAsPrefabAsset(inst, RaidPrefabPath, out bool ok);
                log.AppendLine($"  {(ok ? "✓" : "✗")} TwentyThree_Raid.prefab (Variant) 생성");
            }
            finally { Object.DestroyImmediate(inst); }
            if (prefab == null) return null;
        }

        var boss = prefab.GetComponent<TwentyThreeBoss>();
        var so = new SerializedObject(boss);
        SerializedProperty p = so.FindProperty("data");
        if (p.objectReferenceValue != data)
        {
            p.objectReferenceValue = data;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SavePrefabAsset(prefab);
            log.AppendLine("  ✓ Variant data = No23_Raid");
        }
        else log.AppendLine("  · Variant data 이미 No23_Raid");
        return prefab;
    }

    static void EnsureNetworkPrefab(GameObject prefab, StringBuilder log)
    {
        var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
        if (list == null) { log.AppendLine("  ✗ DefaultNetworkPrefabs 없음"); return; }
        if (list.PrefabList.Any(n => n != null && n.Prefab == prefab)) { log.AppendLine("  · NetworkPrefabs 이미 등록"); return; }

        list.Add(new NetworkPrefab { Prefab = prefab });
        EditorUtility.SetDirty(list);
        log.AppendLine("  ✓ NetworkPrefabs 등록");
    }

    // 열린 씬의 BossEncounterDirector 에 연결. 씬이 원래 깨끗했으면 저장까지, 아니면 표시만 하고 저장은 사람에게 맡긴다.
    static void WireOpenScenes(GameObject prefab, StringBuilder log)
    {
        bool found = false;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (var director in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<BossEncounterDirector>(true)))
            {
                found = true;
                var so = new SerializedObject(director);
                SerializedProperty p = so.FindProperty("raidBossPrefab");
                if (p.objectReferenceValue == prefab) { log.AppendLine($"  · {scene.name} 이미 연결"); continue; }

                bool wasDirty = scene.isDirty;
                p.objectReferenceValue = prefab;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!wasDirty) { EditorSceneManager.SaveScene(scene); log.AppendLine($"  ✓ {scene.name} raidBossPrefab 연결 · 저장"); }
                else log.AppendLine($"  ⚠ {scene.name} raidBossPrefab 연결 — 씬에 다른 미저장 변경이 있어 **저장 안 함**(직접 저장할 것)");
            }
        }
        if (!found) log.AppendLine("  – 열린 씬에 BossEncounterDirector 없음 — 4.MapScene 을 열고 다시 실행하면 연결된다");
    }

    static void EnsureSubtitles(StringBuilder log)
    {
        var table = AssetDatabase.LoadAssetAtPath<TutorialGuideTextTable>(GuideTextsPath);
        if (table == null) { log.AppendLine("  ✗ TutorialGuideTexts 없음"); return; }
        int added = Subtitles.Count(s => table.AddRowIfMissing(s.id, s.text, s.seconds));
        if (added > 0) EditorUtility.SetDirty(table);
        log.AppendLine($"  {(added > 0 ? "✓" : "·")} 웰즈 자막 행 추가 {added}개");
    }
}
