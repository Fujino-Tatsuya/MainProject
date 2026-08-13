using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// 저작·진단 도구 — `BossDataSO`(보스 데이터 애셋)의 프리팹 참조 배선과 현재 값 확인 (2026-08-10).
//
// ── 왜 필요했나 ──────────────────────────────────────────────────────────────
// P9 첫 Play 에서 `bombPrefab 이 비어 있어 Wells 가 빈손으로 던진다` 경고가 떴다.
// P2 에서 `Bomb.prefab` 을 만들었는데 **어디에도 배선하지 않았기 때문**이다.
// 🔴 그리고 이 필드는 **프리팹이 아니라 SO 에 있다** — `BossDataSO.bombPrefab`.
//    보스 프리팹 인스펙터를 뒤져도 안 나온다. 폭탄·장판·예고 프리팹 전부 SO 쪽이다.
//
// ── 진단을 도구로 만든 이유 ──────────────────────────────────────────────────
// 같은 Play 에서 `maxHp` 가 디스크(2000)와 런타임(100)이 달랐다. 애셋 파일을 grep 하는 것으로는
// **Unity 가 메모리에 들고 있는 값**을 알 수 없다(교훈 #22·#69). 그래서 읽기도 엔진 안에서 한다.
public static class BossDataWiring
{
    const string BossDataPath = "Assets/2.Prefabs/Monster/Data/No23.asset";
    const string BombPrefabPath = "Assets/2.Prefabs/Monster/Boss/Bomb.prefab";
    const string FireFloorPrefabPath = "Assets/2.Prefabs/Monster/Boss/FireFloor.prefab";

    [MenuItem("Tools/Boss/보스 데이터 — 배선 검증 (읽기 전용)")]
    public static void Verify()
    {
        var data = AssetDatabase.LoadAssetAtPath<BossDataSO>(BossDataPath);
        if (data == null) { Debug.LogError($"[BossData] {BossDataPath} 를 못 찾았다."); return; }

        var sb = new StringBuilder($"[BossData] {data.name} — Unity 가 들고 있는 값\n");
        sb.AppendLine($"  archetype={data.archetype} · maxHp={data.maxHp} · defense={data.defense} · attackDamage={data.attackDamage}");
        sb.AppendLine($"  {Mark(data.bombPrefab != null)} bombPrefab        = {Name(data.bombPrefab)}");
        sb.AppendLine($"  {Mark(data.jumpTelegraphPrefab != null)} jumpTelegraphPrefab = {Name(data.jumpTelegraphPrefab)}");
        sb.AppendLine($"  {Mark(data.chargeZonePrefab != null)} chargeZonePrefab  = {Name(data.chargeZonePrefab)}");

        sb.AppendLine($"  attacks {data.attacks?.Length ?? 0}개:");
        if (data.attacks != null)
            foreach (BossAttackEntry a in data.attacks)
                sb.AppendLine($"      {a.attackId,-14} state='{a.animatorStateName}' anchor='{a.hitboxAnchorName}'" +
                              $"{(a.opensCounterWindow ? " [카운터]" : "")}{(a.superArmor ? " [슈퍼아머]" : "")}");

        sb.AppendLine($"  phases {data.phases?.Length ?? 0}개:");
        if (data.phases != null)
            foreach (BossPhaseEntry p in data.phases)
                sb.AppendLine($"      threshold={p.hpThreshold} sequence={p.sequence} 데미지×{p.damageMultiplier}");

        Debug.Log(sb.ToString());
    }

    // 차징 오라 범위 표시 — 점프 예고와 **같은 프리팹**(`TelegraphPrefabPath`)을 재사용한다.
    // 둘 다 바닥에 눕는 원이라 새로 만들 이유가 없다.
    //
    // 🔴 오라 범위가 **보여야** 플레이어가 피한다. 비워 두면 판정만 있고 표시가 없어
    //    "갑자기 밀린다"가 된다 — 예고 없는 판정은 이 프로젝트에서 금지에 가깝다.
    [MenuItem("Tools/Boss/보스 데이터 — 차징 오라 예고 배선")]
    public static void WireChargeAura()
    {
        var telegraph = AssetDatabase.LoadAssetAtPath<GameObject>(TelegraphPrefabPath);
        if (telegraph == null) { Debug.LogError($"[BossData] {TelegraphPrefabPath} 를 못 찾았다."); return; }

        int changed = 0;
        foreach (string path in new[] { BossDataPath, SoloDataPath })
        {
            var data = AssetDatabase.LoadAssetAtPath<BossDataSO>(path);
            if (data == null) { Debug.LogWarning($"[BossData] {path} 없음 — 건너뛴다."); continue; }

            var so = new SerializedObject(data);
            SerializedProperty p = so.FindProperty("chargeAuraTelegraphPrefab");
            if (p == null) { Debug.LogError($"[BossData] {data.name}: chargeAuraTelegraphPrefab 필드가 없다."); continue; }
            if (p.objectReferenceValue == telegraph) continue;

            p.objectReferenceValue = telegraph;
            so.ApplyModifiedPropertiesWithoutUndo();
            changed++;
            Debug.Log($"[BossData] {data.name}.chargeAuraTelegraphPrefab → {telegraph.name}", data);
        }

        if (changed > 0) AssetDatabase.SaveAssets();
        Debug.Log($"[BossData] 차징 오라 예고 배선 완료 — 변경 {changed}건(멱등, 이미 맞으면 0건).");
    }

    [MenuItem("Tools/Boss/보스 데이터 — 프리팹 참조 배선 (bombPrefab)")]
    public static void Wire()
    {
        var data = AssetDatabase.LoadAssetAtPath<BossDataSO>(BossDataPath);
        if (data == null) { Debug.LogError($"[BossData] {BossDataPath} 를 못 찾았다."); return; }

        var bomb = AssetDatabase.LoadAssetAtPath<GameObject>(BombPrefabPath);
        if (bomb == null) { Debug.LogError($"[BossData] {BombPrefabPath} 를 못 찾았다."); return; }

        var so = new SerializedObject(data);
        SerializedProperty bombProp = so.FindProperty("bombPrefab");
        if (bombProp.objectReferenceValue != bomb)
        {
            string was = Name(bombProp.objectReferenceValue);
            bombProp.objectReferenceValue = bomb;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"[BossData] bombPrefab: {was} → {bomb.name}. " +
                      "Wells 의 ThrowBombEvent 가 이제 실물을 던진다.");
        }
        else
        {
            Debug.Log("[BossData] bombPrefab 이 이미 배선돼 있다 — 건너뛴다.");
        }

        // 폭탄이 터질 때 깔 장판은 Bomb.prefab 의 zonePrefab 이 갖는다(P2 에서 배선함). 되읽어 확인만.
        var bombBehaviour = bomb.GetComponent<BossBomb>();
        if (bombBehaviour != null)
        {
            var bso = new SerializedObject(bombBehaviour);
            SerializedProperty zone = bso.FindProperty("zonePrefab");
            if (zone != null)
                Debug.Log($"[BossData] Bomb.zonePrefab = {Name(zone.objectReferenceValue)} " +
                          $"(기대 {System.IO.Path.GetFileNameWithoutExtension(FireFloorPrefabPath)})");
        }

        Verify();
    }

    // `ValidateContract` 가 스폰마다 LogError 로 잡는 항목 (P9 첫 Play 에서 확인).
    //
    // base(`MonsterBase`)는 `hasSuperArmorWhileAttacking` 이 켜져 있으면 **모든 공격**에 슈퍼아머를
    // 걸어 버린다. 그러면 `BossAttackEntry.superArmor` 의 공격별 설정이 전부 무의미해진다.
    // 지금 No23 의 공격 8종은 **이미 전부 superArmor=true** 라, 이 플래그만 끄면
    // **거동은 그대로이면서** 에러가 사라지고 이후 공격별 조정이 가능해진다(팀장 확정).
    [MenuItem("Tools/Boss/보스 데이터 — 계약 위반 수정 (hasSuperArmorWhileAttacking)")]
    public static void FixContract()
    {
        var data = AssetDatabase.LoadAssetAtPath<BossDataSO>(BossDataPath);
        if (data == null) { Debug.LogError($"[BossData] {BossDataPath} 를 못 찾았다."); return; }

        int armored = data.attacks?.Count(a => a != null && a.superArmor) ?? 0;
        int total = data.attacks?.Length ?? 0;

        var so = new SerializedObject(data);
        SerializedProperty flag = so.FindProperty("hasSuperArmorWhileAttacking");
        if (flag == null) { Debug.LogError("[BossData] hasSuperArmorWhileAttacking 필드를 못 찾았다."); return; }

        if (!flag.boolValue)
        {
            Debug.Log("[BossData] hasSuperArmorWhileAttacking 이 이미 꺼져 있다 — 건너뛴다.");
            return;
        }

        flag.boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();

        Debug.Log($"[BossData] hasSuperArmorWhileAttacking: on → off. " +
                  $"공격별 superArmor = {armored}/{total} 종이 true 이므로 거동은 그대로다. " +
                  "이제 카운터 창이 있는 Grab·Dash 만 따로 해제하는 조정이 가능하다.");
        Verify();
    }

    // 장판 생명주기·크기 (팀장 확정 2026-08-10): **10초 유지 후 사라진다.**
    //
    // 병합 규칙("겹치면 하나의 더 큰 장판으로 만들고 생명주기를 10초로 리셋")은 **이미 구현돼 있다** —
    // `AreaZone.SpawnOrGrow` 가 같은 자리·같은 타입을 찾으면 `Grow()` 로 갈음하고,
    // `refreshLifetimeOnGrow = true` 가 그때 수명을 다시 채운다. 그래서 여기서는 **값만** 맞춘다.
    //
    // ⚠️ 아직 **SO 이관은 하지 않았다.** 지금 값은 FireFloor 프리팹에 있다. SO 로 빼려면
    //    `BossDataSO` 스키마에 필드를 추가하고 `AreaZone` 이 스폰 후 그 값을 받도록 해야 하는데,
    //    수명 타이머가 이미 프리팹 값으로 시작한 뒤에 덮어쓰면 타이밍 의미가 깨진다.
    //    그 설계는 다음 슬라이스에서 한다.
    [MenuItem("Tools/Boss/장판 — 생명주기·크기 저작 (FireFloor)")]
    public static void AuthorFireFloorZone()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FireFloorPrefabPath);
        if (prefab == null) { Debug.LogError($"[장판] {FireFloorPrefabPath} 를 못 찾았다."); return; }

        var zone = prefab.GetComponent<AreaZone>();
        if (zone == null) { Debug.LogError("[장판] FireFloor 에 AreaZone 이 없다."); return; }

        var so = new SerializedObject(zone);
        SerializedProperty life = so.FindProperty("lifetime");
        SerializedProperty refresh = so.FindProperty("refreshLifetimeOnGrow");
        SerializedProperty radius = so.FindProperty("radius");
        SerializedProperty maxRadius = so.FindProperty("maxRadius");

        float wasLife = life.floatValue;
        life.floatValue = 10f;
        refresh.boolValue = true;   // 병합 시 수명 리셋 — 확정 규칙
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();

        Debug.Log($"[장판] FireFloor: lifetime {wasLife} → {life.floatValue}초 · " +
                  $"refreshLifetimeOnGrow={refresh.boolValue} · radius={radius.floatValue} · " +
                  $"maxRadius={maxRadius.floatValue} (겹치면 이 크기까지 성장하며 수명이 리셋된다).");
    }

    const string TelegraphPrefabPath = "Assets/2.Prefabs/Monster/Boss/JumpTelegraph.prefab";
    const string TelegraphMatPath = "Assets/3.Materials/MA_AoeTelegraph_Red.mat";
    const string SoloDataPath = "Assets/2.Prefabs/Monster/Data/No23_Solo.asset";

    // 점프 착지 예고 프리팹 제작 + 두 SO 에 배선 (팀장 확정 2026-08-10, 로스트아크 방식).
    //
    // ── 코드는 이미 원 2개를 그린다 ─────────────────────────────────────────────
    // `TwentyThreeBoss.ShowJumpTelegraphClientRpc` 가 **같은 프리팹을 2개** 인스턴스화해서
    //   · `Show(radius, growTime)`            → **큰 원 고정**(어디에 떨어지는가)
    //   · `ShowGrowing(0.1f, radius, growTime, 0f)` → **작은 원이 차오름**(언제 떨어지는가)
    // 로 쓴다. 즉 막혀 있던 것은 `jumpTelegraphPrefab` **프리팹 하나뿐**이었다.
    //
    // ── 방향을 추측하지 않고 실측으로 정했다 ────────────────────────────────────
    // `AoeTelegraph.BuildDiscMesh` 는 디스크를 **XY 평면**에 만든다 — 정점이 `(cos, sin, 0)` 이고
    // 지름 1, 노멀이 `Vector3.back`(−Z). Unity 기본 Quad 와 같은 평면이다.
    // `Show()` 는 `localScale = (지름, 지름, 1)` 로 **루트를 직접 스케일**하고, 회전은 건드리지 않는다.
    // `SpawnLocalTelegraph` 는 `Instantiate(prefab)` 이라 **프리팹 회전이 보존된다.**
    // → 그래서 **루트 회전 X = 90** 이어야 바닥에 눕고, 그때 −Z 노멀이 월드 +Y(위)를 향한다.
    //
    // ⚠️ `MeshFilter` 는 **AoeTelegraph 와 같은 오브젝트**에 있어야 한다(`GetComponent<MeshFilter>()`).
    // ⚠️ 프리팹은 **비활성**으로 저장한다 — `Show()` 가 켠다. 활성으로 두면 스폰 순간 한 프레임 번쩍인다.
    // ⚠️ 콜라이더를 붙이지 않는다 — 예고는 순수 비주얼이다.
    [MenuItem("Tools/Boss/점프 예고 — AoeTelegraph 프리팹 제작 + 배선")]
    public static void AuthorJumpTelegraph()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(TelegraphMatPath);
        if (mat == null) { Debug.LogError($"[예고] {TelegraphMatPath} 를 못 찾았다."); return; }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(TelegraphPrefabPath) == null)
        {
            // Quad 프리미티브로 시작한다 — AoeTelegraph 가 원본 Quad 메시를 `_squareMesh` 로 보존한다.
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            try
            {
                temp.name = "JumpTelegraph";
                Object.DestroyImmediate(temp.GetComponent<Collider>()); // 예고는 콜라이더 없음
                temp.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // 바닥에 눕힌다(위 주석)

                var mr = temp.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                temp.AddComponent<AoeTelegraph>(); // shape=Circle, segments=48 이 기본값
                temp.SetActive(false);

                PrefabUtility.SaveAsPrefabAsset(temp, TelegraphPrefabPath);
                Debug.Log($"[예고] {System.IO.Path.GetFileName(TelegraphPrefabPath)} 신규 — " +
                          "회전 X=90(바닥), 콜라이더 없음, 비활성 저장.");
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TelegraphPrefabPath);
        if (prefab == null) { Debug.LogError("[예고] 프리팹 로드 실패."); return; }

        foreach (string dataPath in new[] { BossDataPath, SoloDataPath })
        {
            var data = AssetDatabase.LoadAssetAtPath<BossDataSO>(dataPath);
            if (data == null) { Debug.LogWarning($"[예고] {dataPath} 없음 — 건너뛴다."); continue; }

            var so = new SerializedObject(data);
            SerializedProperty p = so.FindProperty("jumpTelegraphPrefab");
            if (p.objectReferenceValue == prefab) { Debug.Log($"[예고] {data.name}: 이미 배선됨."); continue; }

            p.objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[예고] {data.name}.jumpTelegraphPrefab = {prefab.name}.");
        }

        AssetDatabase.SaveAssets();

        var check = AssetDatabase.LoadAssetAtPath<GameObject>(TelegraphPrefabPath);
        var tg = check.GetComponent<AoeTelegraph>();
        var mf = check.GetComponent<MeshFilter>();
        Debug.Log($"[예고] 검증 — AoeTelegraph {Mark(tg != null)} · MeshFilter {Mark(mf != null)} " +
                  $"(같은 오브젝트여야 한다) · 회전 {check.transform.eulerAngles} · " +
                  $"콜라이더 {check.GetComponent<Collider>() == null} 없음 · activeSelf={check.activeSelf}");
    }

    static string Mark(bool ok) => ok ? "✓" : "✗";
    static string Name(Object o) => o != null ? o.name : "(비어 있음)";
}
