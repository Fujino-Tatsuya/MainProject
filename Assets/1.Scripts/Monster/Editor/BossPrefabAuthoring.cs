using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// 저작 도구 — 23호 보스 프리팹의 **레거시 컴포넌트 스택을 신규 스택으로 교체**한다 (2026-08-08).
//
// 배경: 코드·애니메이터·SO 는 재작성이 끝났는데 `TwentyThree.prefab` 은 레거시 BT 시대 그대로였다.
// `Enemy` / `EnemyBTActivator` / `ChargeController` / `GrabController` / `JumpController` …
// 즉 신규 컴포넌트가 **하나도 붙어 있지 않아** 보스가 한 번도 스폰된 적이 없다.
//
// 🔴 in-place 교체를 택한 이유: 이 프리팹은 씬 3개(`BossScene`·`4.MapScene`·`PlayerBossTest`)와
//    `DefaultNetworkPrefabs.asset` 이 참조한다. 새 프리팹을 만들면 그 참조를 전부 갈아야 하지만,
//    제자리에서 컴포넌트만 바꾸면 참조가 그대로 산다. 리그·중첩 Wells·공격 앵커도 재사용된다.
//
// ─────────────────────────────────────────────────────────────────────────────
// 실측으로 확인한 것 (추측 아님)
//
//   · 레이어는 **이미 맞다** — 루트 `TwentyThree`=Enemy(8) / `HurtBox`=EnemyHurtBox(14) /
//     공격 앵커 6개=Weapon(12). CONTEXT 체크리스트의 레이어 항목은 손댈 것이 없다.
//   · 공격 앵커 이름이 SO 의 `hitboxAnchorName` 과 그대로 맞는다 —
//     LeftHookAttack · RightHookAttack · UpperAttack · Grab · DashAttack · Rage.
//     각 앵커에 `ColliderInfo` 가 붙어 있어 `MonsterMeleeAttack` 이 바로 쓴다.
//   · `NetworkObject` · `NetworkTransform` · `NavMeshAgent` · `Animator` · `Rigidbody` 전부 있다.
//   · `MonsterStatusEffect` · `MonsterMeleeAttack` 은 **아예 없다.** MonsterBase 가 자동 탐색하지만
//     없으면 슈퍼아머와 공격 판정이 통째로 죽는다 — 널가드라 **조용히** 죽는다.
//
// ⚠️ 이 도구가 하지 않는 것 (후속):
//    · `BossDirectionIndicator` 의 호 머티리얼(투명 URP Unlit) — 애셋 저작이라 별도.
//    · Wells / 폭탄 / 장판 / 송전탑 — 각각 다음 단계. 이 도구는 **보스 프리팹만** 만진다.
//    · 씬 3개의 인스턴스 오버라이드 — 프리팹을 바꾼 뒤 씬에서 확인해야 한다.
public static class BossPrefabAuthoring
{
    const string BossPrefabPath = "Assets/2.Prefabs/Wells&No.23/TwentyThree.prefab";
    const string BossDataPath   = "Assets/2.Prefabs/Monster/Data/No23.asset";

    const string PlayerLayerName  = "Player";
    const string DefaultAnchorName = "LeftHookAttack";   // MonsterMeleeAttack 의 기본 히트박스 형상

    // 공격 앵커(Weapon 레이어 자식). SO 의 hitboxAnchorName 이 이 이름으로 찾는다.
    static readonly string[] AnchorNames =
    {
        "LeftHookAttack", "RightHookAttack", "UpperAttack", "Grab", "DashAttack", "Rage",
    };

    // 🔴 제거 대상. **의존하는 것이 먼저 빠져야** 하는 경우가 있어(RequireComponent) 아래 순서를
    //    지키되, 실패하면 진전이 없을 때까지 반복한다(순서를 몰라도 되게).
    static readonly Type[] LegacyToRemove =
    {
        typeof(EnemyBTActivator),            // BT 글루
        typeof(ServerSetAnimState),
        typeof(RunningOnlyOnServer),
        typeof(TwentyThreeAnimEvents),       // 레거시 애니 이벤트 수신 — 신규는 MonsterAnimationEventRelay
        typeof(TwentyThreeBasicAttackChoice),
        typeof(TwentyThreeWells_Initializer),
        typeof(ChargeController),            // → BossChargeSequence
        typeof(GrabController),              // → TwentyThreeBoss 의 Grab 체인
        typeof(JumpController),              // → TwentyThreeBoss 의 Jump 체인
        typeof(BombLauncher),                // → BossWells
        typeof(FloorAreaEffect),             // → AreaZone
        typeof(SpawnPointer),                // 주석에 "디버깅을 위해 임시" (팀장 확정: 제거)
        typeof(EnableCollider),              // MonsterMeleeAttack 의 히트 윈도우와 중복 (팀장 확정: 제거)
        typeof(Enemy),                       // 🔴 마지막 — 나머지가 이걸 요구할 수 있다
    };

    // 추가 대상. 순서 = 의존 순서(먼저 붙은 것을 뒤가 참조할 수 있게).
    static readonly Type[] ToAdd =
    {
        typeof(MonsterStatusEffect),
        typeof(MonsterMeleeAttack),
        typeof(TwentyThreeBoss),
        typeof(BossChargeSequence),
        typeof(BossDirectionIndicator),
    };

    [MenuItem("Tools/Boss/23호 — 보스 프리팹 검증만 (변경 없음)")]
    public static void Validate() => Run(dryRun: true);

    static void Run(bool dryRun)
    {
        var log = new StringBuilder(dryRun
            ? "[23호 프리팹] 검증 모드 — 아무것도 바꾸지 않는다.\n"
            : "[23호 프리팹] 컴포넌트 교체\n");
        bool ok = true;

        // 🔴 프리팹 애셋은 LoadPrefabContents 로 열고 SaveAsPrefabAsset 으로 되쓴다.
        //    LoadAssetAtPath 로 얻어 직접 고치면 저장이 보장되지 않는다(교훈 #67).
        GameObject root = PrefabUtility.LoadPrefabContents(BossPrefabPath);
        if (root == null)
        {
            Debug.LogError($"[23호 프리팹] 열 수 없다: {BossPrefabPath}");
            return;
        }

        try
        {
            // 🔴 **추가가 먼저다.** BossHudTarget 이 `[RequireComponent(typeof(Unit))]` 인데
            //    교체 전에는 그 Unit 이 `Enemy` 하나뿐이라, 제거를 먼저 하면 Unity 가
            //    "Can't remove Enemy because BossHudTarget depends on it" 으로 거부한다.
            //    TwentyThreeBoss 도 Unit 파생이므로 **먼저 붙이면** Enemy 가 제거 가능해진다.
            //    (Unit 에 DisallowMultipleComponent 가 없어 잠깐 공존해도 된다 — 확인함.)
            ok &= AddNew(root, log, dryRun);
            ok &= RemoveLegacy(root, log, dryRun);
            ok &= DisableBehaviorAgents(root, log, dryRun);
            ok &= EnsureAnchorColliderInfo(root, log, dryRun);
            ok &= Wire(root, log, dryRun);

            if (!dryRun)
            {
                PrefabUtility.SaveAsPrefabAsset(root, BossPrefabPath);
                log.AppendLine("\n  💾 저장 완료");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        if (!dryRun)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ok &= VerifyOnDisk(log);
        }

        log.AppendLine(ok
            ? "\n✅ 완료. 다음 = 씬에서 보스를 스폰시켜 ValidateContract 로그를 읽는다."
            : "\n🔴 실패 항목 있음 — 위 로그 확인.");
        if (ok) Debug.Log(log.ToString()); else Debug.LogError(log.ToString());
    }

    // ── 제거 ──────────────────────────────────────────────────────────────
    static bool RemoveLegacy(GameObject root, StringBuilder log, bool dryRun)
    {
        log.AppendLine("\n── 레거시 제거 ──");

        var pending = new List<Component>();
        foreach (Type t in LegacyToRemove)
            pending.AddRange(root.GetComponentsInChildren(t, true)
                                 .Where(c => c != null && !IsNested(root, c)));

        // NetworkAnimator 도 제거 대상 — 보스 애니는 ClientRpc CrossFade 로 몬다(정본).
        foreach (Component c in root.GetComponentsInChildren<Unity.Netcode.Components.NetworkAnimator>(true))
            if (!IsNested(root, c)) pending.Add(c);

        if (pending.Count == 0)
        {
            log.AppendLine("  = 제거할 레거시 없음(이미 교체됨)");
            return true;
        }

        foreach (Component c in pending)
            log.AppendLine($"  - {c.GetType().Name}  ({Path(c.transform, root.transform)})");

        if (dryRun) return true;

        // RequireComponent 의존 때문에 순서가 틀리면 실패한다 — 진전이 없을 때까지 반복한다.
        int guard = 0;
        while (pending.Count > 0 && guard++ < 10)
        {
            int before = pending.Count;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Component c = pending[i];
                if (c == null) { pending.RemoveAt(i); continue; }
                try { UnityEngine.Object.DestroyImmediate(c, true); pending.RemoveAt(i); }
                catch (Exception) { /* 다음 라운드에 재시도 */ }
            }
            if (pending.Count == before) break;   // 더 이상 진전이 없다
        }

        if (pending.Count == 0) return true;

        foreach (Component c in pending.Where(c => c != null))
            log.AppendLine($"🔴 제거 실패: {c.GetType().Name} — 다른 컴포넌트가 RequireComponent 로 잡고 있다");
        return false;
    }

    // ── BehaviorGraphAgent OFF ────────────────────────────────────────────
    // 제거가 아니라 비활성이다(정본 R5). 그래프 자산 참조를 남겨 두어야 레거시 대조가 가능하다.
    static bool DisableBehaviorAgents(GameObject root, StringBuilder log, bool dryRun)
    {
        log.AppendLine("\n── BehaviorGraphAgent ──");

        var agents = root.GetComponentsInChildren<Unity.Behavior.BehaviorGraphAgent>(true)
                         .Where(a => a != null && !IsNested(root, a)).ToArray();
        if (agents.Length == 0)
        {
            log.AppendLine("  = 없음(중첩 Wells 의 것은 Wells.prefab 에서 따로 끈다)");
            return true;
        }

        foreach (Unity.Behavior.BehaviorGraphAgent a in agents)
        {
            log.AppendLine(a.enabled ? $"  ○ OFF  ({Path(a.transform, root.transform)})"
                                     : $"  = 이미 OFF ({Path(a.transform, root.transform)})");
            if (!dryRun) a.enabled = false;
        }
        return true;
    }

    // ── 추가 ──────────────────────────────────────────────────────────────
    static bool AddNew(GameObject root, StringBuilder log, bool dryRun)
    {
        log.AppendLine("\n── 신규 컴포넌트 ──");

        foreach (Type t in ToAdd)
        {
            if (root.GetComponent(t) != null)
            {
                log.AppendLine($"  = {t.Name} — 이미 있음");
                continue;
            }
            log.AppendLine($"  + {t.Name}");
            if (!dryRun) root.AddComponent(t);
        }
        return true;
    }

    // ── 공격 앵커에 ColliderInfo 보장 ─────────────────────────────────────
    // 🔴 처음엔 "앵커 6개에 ColliderInfo 가 이미 붙어 있다"고 적었는데 **틀렸다.**
    //    컴포넌트 집합 grep 에 ColliderInfo 가 보인 것만으로 단정했고, 실제 배치는 확인하지 않았다.
    //    실측하니 붙어 있는 것은 `Grab` **하나뿐**이었다(나머지는 레거시 Attack/KnockbackAttack 계열).
    //    → MonsterMeleeAttack.CacheHitboxAnchors 가 이름으로 색인하는 대상이 ColliderInfo 라서,
    //      없으면 SO 의 hitboxAnchorName 이 조용히 안 먹는다.
    //
    // ColliderInfo 는 직렬화 필드가 없고 Awake 에서 같은 오브젝트의 콜라이더를 읽는다.
    // 앵커 6개 모두 콜라이더가 **정확히 1개씩**인 것을 확인했으므로 붙이기만 하면 된다.
    static bool EnsureAnchorColliderInfo(GameObject root, StringBuilder log, bool dryRun)
    {
        log.AppendLine("\n── 공격 앵커 ColliderInfo ──");

        bool ok = true;
        foreach (string anchorName in AnchorNames)
        {
            Transform t = FindByName(root.transform, anchorName);
            if (t == null)
            {
                log.AppendLine($"🔴 앵커 오브젝트 없음: {anchorName}");
                ok = false;
                continue;
            }

            if (t.GetComponent<ColliderInfo>() != null)
            {
                log.AppendLine($"  = {anchorName} — 이미 있음");
                continue;
            }

            // ColliderInfo 는 콜라이더가 하나도 없으면 Awake 에서 LogError 를 낸다 — 미리 막는다.
            if (t.GetComponent<Collider>() == null)
            {
                log.AppendLine($"🔴 {anchorName} 에 콜라이더가 없다 — ColliderInfo 를 붙이면 런타임 에러가 난다");
                ok = false;
                continue;
            }

            log.AppendLine($"  + {anchorName} ← ColliderInfo ({t.GetComponent<Collider>().GetType().Name})");
            if (!dryRun) t.gameObject.AddComponent<ColliderInfo>();
        }
        return ok;
    }

    static Transform FindByName(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root.GetComponentsInChildren<Transform>(true))
            if (c.name == name) return c;
        return null;
    }

    // ── 배선 ──────────────────────────────────────────────────────────────
    // MonsterBase 의 필드는 protected 라 직접 못 넣는다 → SerializedObject 로 넣는다.
    // agent/animator/status/meleeAttack 은 OnNetworkSpawn 이 자동 탐색하지만,
    // **data 와 playerMask 는 자동 탐색이 없다** — 비어 있으면 보스가 초기화조차 못 한다.
    static bool Wire(GameObject root, StringBuilder log, bool dryRun)
    {
        log.AppendLine("\n── 배선 ──");
        if (dryRun) { log.AppendLine("  (검증 모드 — 생략)"); return true; }

        bool ok = true;

        var boss = root.GetComponent<TwentyThreeBoss>();
        if (boss == null) { log.AppendLine("🔴 TwentyThreeBoss 가 없다 — 배선 불가"); return false; }

        var bossData = AssetDatabase.LoadAssetAtPath<BossDataSO>(BossDataPath);
        if (bossData == null) { log.AppendLine($"🔴 {BossDataPath} 를 못 읽었다"); ok = false; }

        int playerLayer = LayerMask.NameToLayer(PlayerLayerName);
        if (playerLayer < 0) { log.AppendLine($"🔴 레이어 '{PlayerLayerName}' 가 없다"); ok = false; }

        var so = new SerializedObject(boss);
        if (bossData != null) SetRef(so, "data", bossData, log);
        if (playerLayer >= 0)
        {
            SerializedProperty mask = so.FindProperty("playerMask");
            if (mask != null) { mask.intValue = 1 << playerLayer; log.AppendLine($"  ~ playerMask = {PlayerLayerName}({playerLayer})"); }
            else { log.AppendLine("🔴 playerMask 프로퍼티를 못 찾았다"); ok = false; }
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        // MonsterMeleeAttack 의 기본 히트박스 = 앵커 하나. 공격별 스왑은 SO 의 hitboxAnchorName 이 한다.
        var melee = root.GetComponent<MonsterMeleeAttack>();
        ColliderInfo anchor = root.GetComponentsInChildren<ColliderInfo>(true)
                                  .FirstOrDefault(c => c != null && c.name == DefaultAnchorName);
        if (melee != null && anchor != null)
        {
            var mso = new SerializedObject(melee);
            SetRef(mso, "colliderInfo", anchor, log);
            mso.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            log.AppendLine($"🔴 기본 히트박스 배선 실패 — melee={(melee != null)} / 앵커 '{DefaultAnchorName}'={(anchor != null)}");
            ok = false;
        }

        // 🔴 Hurtbox.ownerUnit 이 방금 지운 Enemy 를 가리키고 있었다. null 이면
        //    GetComponentInParent<Unit>() 로 폴백해 살긴 하지만, 명시적으로 새 보스를 가리키게 한다.
        foreach (Hurtbox h in root.GetComponentsInChildren<Hurtbox>(true))
        {
            if (IsNested(root, h)) continue;
            var hso = new SerializedObject(h);
            SetRef(hso, "ownerUnit", boss, log);
            hso.ApplyModifiedPropertiesWithoutUndo();
        }

        return ok;
    }

    static void SetRef(SerializedObject so, string field, UnityEngine.Object value, StringBuilder log)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p == null) { log.AppendLine($"🔴 프로퍼티 없음: {field}"); return; }
        if (p.objectReferenceValue == value) { log.AppendLine($"  = {field} — 이미 {value.name}"); return; }
        p.objectReferenceValue = value;
        log.AppendLine($"  ~ {field} = {value.name}");
    }

    // ── 되읽기 검증 ───────────────────────────────────────────────────────
    // 🔴 호출이 성공했다는 것과 파일이 바뀌었다는 것은 별개다(교훈 #61·#67).
    static bool VerifyOnDisk(StringBuilder log)
    {
        log.AppendLine("\n── 되읽기 검증 ──");

        var saved = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);
        if (saved == null) { log.AppendLine("🔴 저장본을 못 읽었다"); return false; }

        bool ok = true;
        foreach (Type t in ToAdd)
        {
            bool has = saved.GetComponent(t) != null;
            log.AppendLine($"  {(has ? "✅" : "🔴")} {t.Name}");
            ok &= has;
        }
        foreach (Type t in LegacyToRemove)
        {
            // ⚠️ 중첩(Wells) 것은 세지 않는다 — 처음엔 안 걸러서 "BombLauncher 가 1개 남아 있다"는
            //    **오탐**이 떴다. 그건 웰즈의 것이고 이 도구의 관할이 아니다.
            Component[] left = saved.GetComponentsInChildren(t, true)
                                    .Where(c => c != null && !IsNested(saved, c)).ToArray();
            if (left.Length == 0) continue;
            log.AppendLine($"  🔴 {t.Name} 가 {left.Length}개 남아 있다");
            ok = false;
        }

        foreach (string anchorName in AnchorNames)
        {
            Transform t = FindByName(saved.transform, anchorName);
            bool has = t != null && t.GetComponent<ColliderInfo>() != null;
            if (!has) { log.AppendLine($"  🔴 앵커 {anchorName} 에 ColliderInfo 가 없다"); ok = false; }
        }

        var boss = saved.GetComponent<TwentyThreeBoss>();
        if (boss != null)
        {
            var so = new SerializedObject(boss);
            SerializedProperty data = so.FindProperty("data");
            SerializedProperty mask = so.FindProperty("playerMask");
            bool dataOk = data != null && data.objectReferenceValue != null;
            bool maskOk = mask != null && mask.intValue != 0;
            log.AppendLine($"  {(dataOk ? "✅" : "🔴")} data = {(dataOk ? data.objectReferenceValue.name : "(비어 있음)")}");
            log.AppendLine($"  {(maskOk ? "✅" : "🔴")} playerMask = {(mask != null ? mask.intValue : 0)}");
            ok &= dataOk && maskOk;
        }

        return ok;
    }

    // 중첩 프리팹 인스턴스(= Wells)의 컴포넌트는 건드리지 않는다 — 그쪽은 원본에서 따로 한다.
    // 🔴 IsPartOfPrefabInstance / GetNearestPrefabInstanceRoot 는 여기서 못 쓴다(교훈 #67) —
    //    프리팹 애셋 내부에서는 자기 소유 오브젝트까지 "인스턴스"로 보고한다.
    //    자기 소유 컴포넌트는 `m_CorrespondingSourceObject` 가 비어 있다는 사실을 그대로 읽는다.
    static bool IsNested(GameObject root, Component c) =>
        PrefabUtility.GetCorrespondingObjectFromSource(c) != null;

    static string Path(Transform t, Transform root)
    {
        if (t == root) return "(루트)";
        string s = t.name;
        for (Transform p = t.parent; p != null && p != root; p = p.parent) s = p.name + "/" + s;
        return s;
    }
}
