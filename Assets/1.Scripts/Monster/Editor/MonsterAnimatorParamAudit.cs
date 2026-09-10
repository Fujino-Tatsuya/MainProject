using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 감사 도구(읽기 전용) — 몬스터 데이터에 적힌 **애니메이터 배선이 실제 컨트롤러에 있는지** 본다.
// 파라미터 이름 + **파라미터 타입** + **상태 이름** 셋을 함께 검사한다.
//
// 🔴 왜 필요한가 — `MonsterBase` 의 접근자는 전부 **조용히 no-op** 한다(경고도 없다):
//    `SafeSetTrigger/SafeSetBool/SafeSetFloat` 는 `HasParameter` 로 막고,
//    `SafeCrossFade` 는 `animator.HasState(0, hash)` 로 막는다. 그래서 데이터에 없는 이름이
//    적혀 있어도 아무 신호가 없고 "그로기인데 애니가 안 바뀐다" 같은 증상으로 나중에 발견된다.
//    실제로 2026-09-08 에 GauntletBot·WallBot 의 `groggyBool`, SpinnerBot·WallBot 의 `deathTrigger` 가
//    존재하지 않는 파라미터를 가리키고 있었다 — 중간보스 카운터를 붙이다가 발견했다.
//
// 🔴 2026-09-10 확장 — 이 도구가 **절반만 보고 있었다.** PeekABot 의 파트 분리를 조사하다가
//    `Controller_PeekABot` 이 아트 팩의 터렛 전용(Hide·Idle·Raise·Shoot)이라 **로코모션 상태 자체가
//    없다**는 것이 드러났는데, 도구는 "Hit 파라미터 하나 없음"만 보고했다. 이유는 셋이었다:
//      ① `locomotionState` 는 파라미터가 아니라 **상태**라서 검사 대상 밖이었다
//      ② `attackFinishTrigger` 는 목록에서 아예 빠져 있었다(WallBot 이 실제로 쓴다)
//      ③ 이름만 비교해 **타입 불일치**를 못 봤다 — Float 자리에 Trigger 가 있으면 `SetFloat` 이
//         런타임 경고만 남기고 값이 안 들어간다
//    교훈: 감사 도구가 초록을 냈을 때 **그 도구가 무엇을 안 보는지** 한 번은 물어야 한다.
//
// ⚠️ 메뉴 이름이 「파라미터 감사」에서 「배선 감사」로 바뀌었다(2026-09-10). 상태까지 보므로
//    옛 이름은 내용과 어긋났다.
//
// 사용법: 메뉴를 누르면 콘솔에 표가 하나 뜬다. ❌ 가 있으면 둘 중 하나다 —
//   ① 컨트롤러에 파라미터/상태를 만들어야 한다(아트/SVN)  ② 데이터의 이름을 비워야 한다(더 이상 쓰지 않는 것)
//   상태(`locomotionState`)는 비울 수 없으므로 ①만 답이다 — 비우면 액션 클립에서 복귀할 곳이 없다.
public static class MonsterAnimatorParamAudit
{
    const string PrefabFolder = "Assets/2.Prefabs/Monster";

    [MenuItem("Tools/Boss/몬스터 — 애니메이터 배선 감사 (읽기 전용)")]
    public static void Audit()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder });
        var report = new StringBuilder("[WiringAudit] 몬스터 데이터 ↔ 컨트롤러 (파라미터 이름·타입 + 상태 이름)\n");
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

            // 🔴 **실효** 컨트롤러를 봐야 한다 — `MonsterBase.Awake` 는 데이터의
            //    `animatorControllerOverride` 가 있으면 그것으로 덮는다. 프리팹 쪽만 보면
            //    런타임에 쓰이지 않는 컨트롤러를 감사해 거짓 신호를 낸다.
            var controller = data != null ? data.animatorControllerOverride as AnimatorController : null;
            string source = controller != null ? "데이터 오버라이드" : "프리팹 배선";
            if (controller == null)
                controller = animator != null ? animator.runtimeAnimatorController as AnimatorController : null;

            if (data == null || controller == null)
            {
                report.AppendLine($"  {prefab.name}: 데이터 {(data == null ? "없음" : "있음")} · " +
                                  $"컨트롤러 {(controller == null ? "없음" : "있음")} → 감사 불가");
                continue;
            }

            var types = new Dictionary<string, AnimatorControllerParameterType>();
            foreach (AnimatorControllerParameter p in controller.parameters) types[p.name] = p.type;

            // 🔴 레이어 0 만 본다 — 런타임의 `SafeCrossFade` 가 `HasState(0, hash)` 로 판정하므로
            //    다른 레이어에 같은 이름이 있어도 그 경로에서는 쓰이지 않는다.
            HashSet<string> states = CollectStateNames(controller, 0);

            var missing = new List<string>();
            CheckParam(types, missing, "animSpeedParam", data.animSpeedParam, AnimatorControllerParameterType.Float);
            CheckParam(types, missing, "attackTrigger", data.attackTrigger, AnimatorControllerParameterType.Trigger);
            CheckParam(types, missing, "attackFinishTrigger", data.attackFinishTrigger, AnimatorControllerParameterType.Trigger);
            CheckParam(types, missing, "hitTrigger", data.hitTrigger, AnimatorControllerParameterType.Trigger);
            CheckParam(types, missing, "groggyBool", data.groggyBool, AnimatorControllerParameterType.Bool);
            CheckParam(types, missing, "deathTrigger", data.deathTrigger, AnimatorControllerParameterType.Trigger);
            CheckState(states, missing, "locomotionState", data.locomotionState);

            missingTotal += missing.Count;
            report.AppendLine(missing.Count == 0
                ? $"  {prefab.name} ({controller.name} · {source}): 전부 있음  [파라미터 {types.Count} / 상태 {states.Count}]"
                : $"  ❌ {prefab.name} ({controller.name} · {source}): {string.Join(", ", missing)}");
        }

        report.Append($"\n  합계: 죽은 이름 {missingTotal}개. " +
                      "0 이 아니면 컨트롤러에 파라미터를 만들거나 데이터의 이름을 비울 것.");

        if (missingTotal > 0) Debug.LogWarning(report.ToString());
        else Debug.Log(report.ToString());
    }

    // 플레이어 HurtBox 반경(Paladin.prefab 실측, layer 13 CapsuleCollider). 접촉 거리 계산의 상수다.
    // 🔴 이 값을 눈으로 추정하면 안 된다 — 0.4 로 어림잡았다가 WallBot 이 최대 거리에서 항상
    //    헛스윙하는 결함을 만들었다(2026-09-08). 실제 값은 0.2 다.
    const float PlayerHurtboxRadius = 0.2f;

    /// <summary>
    /// 근접 공격의 **개시 거리 ↔ 히트박스 실도달** 정합 감사.
    ///
    /// 실효 접촉 = 히트박스 전방 도달 + 플레이어 반경. <c>attackRange</c> 가 그보다 크면
    /// 그 구간에서 시작한 공격은 **판정이 없다** — 팔만 휘두르고 데미지가 안 나간다.
    /// 히트가 선딜 뒤에 나오므로(예: WallBot 0.33초) 실제로는 마진이 더 필요하다.
    /// </summary>
    [MenuItem("Tools/Boss/몬스터 — 근접 공격 거리 정합 감사 (읽기 전용)")]
    public static void AuditMeleeReach()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder });
        var sb = new StringBuilder("[ReachAudit] 개시 거리 vs 히트박스 실도달 (실효 접촉 = 도달 + 플레이어 반경 0.2)\n");
        int bad = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponent<MonsterBase>() == null) continue;

            MonsterDataSO data = FindData(prefab);
            var melee = prefab.GetComponentInChildren<MonsterMeleeAttack>(true);
            if (data == null || melee == null) continue;

            // 🔴 원거리 아키타입은 제외한다 — 그 `attackRange` 는 **사격 거리**다(PeekABot 10 · TeslaBot 11 ·
            //    MortarBot 9). 근접 히트박스도 달려 있어서 그냥 재면 "8m 부족"으로 오탐이 뜬다.
            if (data.archetype == MonsterArchetype.RangedTurret ||
                data.archetype == MonsterArchetype.RangedMobile) continue;

            var box = melee.GetComponent<BoxCollider>();
            var sphere = melee.GetComponent<SphereCollider>();
            float reach;
            string shape;
            if (box != null) { reach = box.center.z + box.size.z * 0.5f; shape = $"Box z {box.size.z} @ {box.center.z}"; }
            else if (sphere != null) { reach = sphere.center.z + sphere.radius; shape = $"Sphere r {sphere.radius} @ {sphere.center.z}"; }
            else continue;

            float contact = reach + PlayerHurtboxRadius;
            bool over = data.attackRange > contact;
            if (over) bad++;

            sb.AppendLine($"  {(over ? "❌" : "  ")} {prefab.name,-14} 개시 {data.attackRange,5} / 도달 {reach,5} / " +
                          $"접촉 {contact,5} ({shape})" + (over ? $"  → {contact - data.attackRange:0.##}m 부족" : ""));
        }

        sb.Append($"\n  개시 거리가 접촉보다 먼 몹: {bad}종. 0 이 아니면 그 구간에서 판정 없는 공격이 나간다.");
        if (bad > 0) Debug.LogWarning(sb.ToString());
        else Debug.Log(sb.ToString());
    }

    // 데이터 값이 **에디터에 실제로 로드된 값**인지 본다.
    // 🔴 YAML 을 손으로 고치면 에디터가 들고 있는 인스턴스는 그대로일 수 있다(Assets/Refresh 전).
    //    "고쳤는데 Play 에서 안 먹는다"의 흔한 원인이라, 튜닝값은 이 창으로 확인한다.
    [MenuItem("Tools/Boss/몬스터 — 인지·전투 값 점검 (읽기 전용)")]
    public static void ReportCombatValues()
    {
        string[] guids = AssetDatabase.FindAssets("t:MonsterDataSO", new[] { "Assets/2.Prefabs/Monster/Data" });
        var sb = new StringBuilder("[ValueAudit] 인지 반경 / 높이 허용 / 공격 거리 / 회전 속도 / 재선정 / 그로기 누적\n");

        foreach (string guid in guids)
        {
            var d = AssetDatabase.LoadAssetAtPath<MonsterDataSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (d == null) continue;

            sb.AppendLine($"  {d.name,-18} 인지 {d.detectionRadius,4} / 높이 {d.detectionHeightTolerance,4} / " +
                          $"공격 {d.attackRange,4} / 회전 {d.turnSpeed,4} / 재선정 {d.retargetInterval,4} / " +
                          $"그로기누적 {d.maxGroggyCount}");
        }

        Debug.Log(sb.ToString());
    }

    // 비어 있는 이름은 "안 쓴다"는 뜻이라 정상이다 — 채워져 있는데 없는 것만 잡는다.
    //
    // 🔴 타입까지 본다. 이름만 맞고 타입이 다르면 런타임에 `SetFloat`/`SetBool` 이 값을 못 넣는데
    //    `MonsterBase.HasParameter` 는 **이름만** 확인하므로 그 경로는 통과해 버린다.
    static void CheckParam(Dictionary<string, AnimatorControllerParameterType> types, List<string> bad,
                           string field, string value, AnimatorControllerParameterType expected)
    {
        if (string.IsNullOrEmpty(value)) return;

        if (!types.TryGetValue(value, out AnimatorControllerParameterType actual))
        {
            bad.Add($"파라미터 없음 {field}=\"{value}\"");
            return;
        }

        if (actual != expected)
            bad.Add($"파라미터 타입 불일치 {field}=\"{value}\" ({actual} ≠ 기대 {expected})");
    }

    // 상태는 비울 수 없다 — 비우면 `SafeCrossFade` 가 복귀할 곳이 없다. 그래서 빈 값도 결함으로 잡는다.
    static void CheckState(HashSet<string> states, List<string> bad, string field, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            bad.Add($"상태 이름이 비었다 {field} — 액션 클립에서 복귀할 대상이 없다");
            return;
        }

        if (!states.Contains(value)) bad.Add($"상태 없음 {field}=\"{value}\"");
    }

    /// <summary>
    /// 레이어의 상태 이름 집합. **하위 스테이트 머신까지 재귀**한다 — `Animator.HasState` 가
    /// 레이어 전체를 보므로 감사도 같은 범위여야 한다(하위 머신만 보고 "없음"이라고 하면 거짓 양성).
    /// </summary>
    static HashSet<string> CollectStateNames(AnimatorController controller, int layer)
    {
        var set = new HashSet<string>();
        if (controller.layers == null || layer < 0 || layer >= controller.layers.Length) return set;

        CollectInto(controller.layers[layer].stateMachine, set);
        return set;
    }

    static void CollectInto(UnityEditor.Animations.AnimatorStateMachine sm, HashSet<string> set)
    {
        if (sm == null) return;

        foreach (ChildAnimatorState cs in sm.states)
            if (cs.state != null) set.Add(cs.state.name);

        foreach (ChildAnimatorStateMachine child in sm.stateMachines)
            CollectInto(child.stateMachine, set);
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
