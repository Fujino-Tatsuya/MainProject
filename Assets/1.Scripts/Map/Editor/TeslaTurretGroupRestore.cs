using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <c>MapGenConfig</c> 의 <b>Tesla 임시대체</b> 그룹을 실제 <c>TeslaBot</c> 프리팹으로 되돌린다.
///
/// 🔴 <b>무엇이 문제였나</b>(2026-09-14 실측): <c>GroupID 3</c> 의 이름이
/// <c>"PeekA Turret (Tesla 임시대체)"</c> 이고 <c>MonsterPrefab</c> 이 <b>GroupID 2 와 같은
/// PeekABot</b> 을 가리키고 있었다. 그래서 씬 어디에도 TeslaBot 이 스폰되지 않았다
/// (애니메이터 119개를 세어 봐도 TeslaBot 은 0개였다). GroupID 3 은 존 프리팹 5곳 이상이
/// 참조하므로, 여기만 바꾸면 그 존들에서 TeslaBot 이 뜬다.
///
/// ⚠️ <b>이 에셋은 SVN 이다</b>(<c>Assets/50.Art/</c> 아래 · git 미추적).
///    적용하면 <b>git 이 아니라 SVN 커밋</b> 대상이고 팀 전체에 영향이 간다.
///
/// ⚠️ 1회용 복구 도구다 — SVN 반영이 끝나면 지워도 된다.
/// </summary>
public static class TeslaTurretGroupRestore
{
    const string ConfigPath = "Assets/50.Art/MapGen/MapObj/MapZoonSettingObj/MapGenConfig.asset";
    const string TeslaPath = "Assets/2.Prefabs/Monster/TeslaBot.prefab";
    const int TargetGroupId = 3;
    const string NewGroupName = "Tesla Turret";

    [MenuItem("Tools/Boss/터렛 스폰 복구 — MonsterGroup 3 을 TeslaBot 으로 (검증)")]
    public static void Validate() => Run(dryRun: true);

    [MenuItem("Tools/Boss/터렛 스폰 복구 — MonsterGroup 3 을 TeslaBot 으로 (적용)")]
    public static void Apply() => Run(dryRun: false);

    static void Run(bool dryRun)
    {
        var log = new StringBuilder($"[TeslaGroup/{(dryRun ? "검증" : "적용")}] MonsterGroup {TargetGroupId}\n");

        var cfg = AssetDatabase.LoadAssetAtPath<MapGenConfigSO>(ConfigPath);
        var tesla = AssetDatabase.LoadAssetAtPath<GameObject>(TeslaPath);

        if (cfg == null || tesla == null)
        {
            log.AppendLine($"  ✗ 로드 실패 — config {(cfg == null ? "없음" : "OK")} / TeslaBot {(tesla == null ? "없음" : "OK")}");
            Debug.LogError(log.ToString());
            return;
        }

        int index = -1;
        for (int i = 0; i < cfg.MonsterGroups.Count; i++)
            if (cfg.MonsterGroups[i].GroupID == TargetGroupId) { index = i; break; }

        if (index < 0)
        {
            log.AppendLine($"  ✗ GroupID {TargetGroupId} 을 찾지 못했다 (그룹 {cfg.MonsterGroups.Count}개)");
            Debug.LogError(log.ToString());
            return;
        }

        MonsterGroupData g = cfg.MonsterGroups[index];
        string beforePrefab = g.MonsterPrefab != null ? g.MonsterPrefab.name : "(없음)";

        if (g.MonsterPrefab == tesla)
        {
            log.Append($"  = 이미맞음 — [{index}] GroupID {g.GroupID} \"{g.GroupName}\" → {beforePrefab}");
            Debug.Log(log.ToString());
            return;
        }

        log.AppendLine($"  ▶ [{index}] GroupID {g.GroupID} \"{g.GroupName}\"");
        log.AppendLine($"      MonsterPrefab: {beforePrefab} → {tesla.name}");
        log.AppendLine($"      GroupName:     \"{g.GroupName}\" → \"{NewGroupName}\"");

        if (dryRun)
        {
            log.Append("  (검증만 — 적용하지 않았다. 이 에셋은 SVN 이다)");
            Debug.Log(log.ToString());
            return;
        }

        g.MonsterPrefab = tesla;
        g.GroupName = NewGroupName;
        cfg.MonsterGroups[index] = g;      // struct 라 반드시 되돌려 넣어야 한다

        EditorUtility.SetDirty(cfg);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 되읽어 확인한다 — 저장이 조용히 안 먹은 전례가 있다.
        var reread = AssetDatabase.LoadAssetAtPath<MapGenConfigSO>(ConfigPath);
        MonsterGroupData after = reread.MonsterGroups[index];
        bool ok = after.MonsterPrefab == tesla;

        log.Append(ok
            ? $"  ✓ 적용 (되읽기: {after.MonsterPrefab.name} ✓)\n" +
              "  ⚠️ 이 파일은 SVN 이다 — git 이 아니라 SVN 으로 커밋해야 팀에 반영된다."
            : "  🔴 적용했는데 되읽으니 그대로다");

        if (ok) Debug.Log(log.ToString());
        else Debug.LogError(log.ToString());
    }
}
