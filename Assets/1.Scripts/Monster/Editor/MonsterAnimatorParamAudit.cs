using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 감사 도구(읽기 전용) — 몬스터 데이터에 적힌 **애니메이터 파라미터 이름이 실제 컨트롤러에 있는지** 본다.
//
// 🔴 왜 필요한가 — `MonsterBase.SafeSetTrigger/SafeSetBool` 은 `HasParameter` 로 막고 **조용히 no-op**
//    한다(경고도 없다). 그래서 데이터에 없는 파라미터 이름이 적혀 있어도 아무 신호가 없고,
//    "그로기인데 애니가 안 바뀐다" 같은 증상으로 나중에 발견된다.
//    실제로 2026-09-08 에 GauntletBot·WallBot 의 `groggyBool`, SpinnerBot·WallBot 의 `deathTrigger` 가
//    존재하지 않는 파라미터를 가리키고 있었다 — 중간보스 카운터를 붙이다가 발견했다.
//
// 사용법: 메뉴를 누르면 콘솔에 표가 하나 뜬다. ❌ 가 있으면 둘 중 하나다 —
//   ① 컨트롤러에 파라미터를 만들어야 한다(아트/SVN)  ② 데이터의 이름을 비워야 한다(더 이상 쓰지 않는 것)
public static class MonsterAnimatorParamAudit
{
    const string PrefabFolder = "Assets/2.Prefabs/Monster";

    [MenuItem("Tools/Boss/몬스터 — 애니메이터 파라미터 감사 (읽기 전용)")]
    public static void Audit()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder });
        var report = new StringBuilder("[ParamAudit] 몬스터 데이터 ↔ 컨트롤러 파라미터\n");
        int missingTotal = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            var monster = prefab.GetComponent<MonsterBase>();
            if (monster == null) continue;

            MonsterDataSO data = FindData(prefab);
            Animator animator = prefab.GetComponentInChildren<Animator>(true);
            var controller = animator != null ? animator.runtimeAnimatorController as AnimatorController : null;

            if (data == null || controller == null)
            {
                report.AppendLine($"  {prefab.name}: 데이터 {(data == null ? "없음" : "있음")} · " +
                                  $"컨트롤러 {(controller == null ? "없음" : "있음")} → 감사 불가");
                continue;
            }

            var names = new HashSet<string>();
            foreach (AnimatorControllerParameter p in controller.parameters) names.Add(p.name);

            var missing = new List<string>();
            Check(names, missing, "animSpeedParam", data.animSpeedParam);
            Check(names, missing, "attackTrigger", data.attackTrigger);
            Check(names, missing, "hitTrigger", data.hitTrigger);
            Check(names, missing, "groggyBool", data.groggyBool);
            Check(names, missing, "deathTrigger", data.deathTrigger);

            missingTotal += missing.Count;
            report.AppendLine(missing.Count == 0
                ? $"  {prefab.name} ({controller.name}): 전부 있음"
                : $"  ❌ {prefab.name} ({controller.name}): 없는 파라미터 → {string.Join(", ", missing)}");
        }

        report.Append($"\n  합계: 죽은 이름 {missingTotal}개. " +
                      "0 이 아니면 컨트롤러에 파라미터를 만들거나 데이터의 이름을 비울 것.");

        if (missingTotal > 0) Debug.LogWarning(report.ToString());
        else Debug.Log(report.ToString());
    }

    // 비어 있는 이름은 "안 쓴다"는 뜻이라 정상이다 — 채워져 있는데 없는 것만 잡는다.
    static void Check(HashSet<string> names, List<string> missing, string field, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (!names.Contains(value)) missing.Add($"{field}=\"{value}\"");
    }

    static MonsterDataSO FindData(GameObject prefab)
    {
        var monster = prefab.GetComponent<MonsterBase>();
        if (monster == null) return null;

        // data 는 protected 라 직렬화 경로로 읽는다 — 감사 도구가 코드의 접근 수준을 바꾸게 하지 않는다.
        var so = new SerializedObject(monster);
        SerializedProperty prop = so.FindProperty("data");
        return prop != null ? prop.objectReferenceValue as MonsterDataSO : null;
    }
}
