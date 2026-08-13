using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 저작 도구 — 보스 변형 **No23 단독(중간보스)** 을 만든다 (PLAN 「보스 변형 2종 분리」 V1~V4, 2026-08-10).
//
// 멱등이다. 여러 번 실행해도 같은 결과가 나오고, 이미 있는 애셋은 다시 만들지 않고 값만 다시 맞춘다.
//
// ── 왜 데이터만으로 되나 (실측 근거) ────────────────────────────────────────
// · 보스 코드는 Wells 를 **널안전**하게만 쓴다 — `_wells = GetComponentInChildren<BossWells>(true)` 이고
//   소비가 전부 `_wells?.` 다. 없으면 조용히 건너뛴다.
// · **잡기는 Wells 와 무관하다** — `BeginRestrainedByInstigator(gameObject, …)` 의 주체가 보스 자신이다.
//   Wells 가 가진 소켓은 `bombSocket` 하나뿐이고, 그건 폭탄 전용이다.
// · 폭탄 투척은 **Wells 의 애니 이벤트**(`BossWells.ThrowBombEvent`)가 시작한다 →
//   Wells 를 빼면 폭탄이 자동으로 빠진다. SO 의 `bombPrefab` 도 같이 비운다(이중 안전).
// 그래서 이 도구는 **코드를 한 줄도 안 만진다.**
//
// ── 승계에 대한 판단 ─────────────────────────────────────────────────────────
// 프리팹을 **복제**해서 Wells 만 뺀다. 리그·앵커(`Hand_L`/`Hand_R`/`DashBody`)·콜라이더·Animator 가
// 그대로 승계된다. 같은 모델·같은 리그이므로 이번엔 **의도된 승계**다 — 다만 그 값들이 아직 Play 로
// 검증된 적 없다는 사실은 변하지 않는다(교훈 #68).
public static class BossVariantAuthoring
{
    const string SrcDataPath = "Assets/2.Prefabs/Monster/Data/No23.asset";
    const string SoloDataPath = "Assets/2.Prefabs/Monster/Data/No23_Solo.asset";
    const string SrcPrefabPath = "Assets/2.Prefabs/Monster/Boss/TwentyThree.prefab";
    const string SoloPrefabPath = "Assets/2.Prefabs/Monster/Boss/TwentyThree_Solo.prefab";
    const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";

    const string SceneName = "MonsterScene";
    const string SpawnerName = "MidBossSpawner";
    const string PointName = "MidBossSpawnPoint";

    // 중간보스 체력. 기존 중간보스급 실측값 기준(WallBot 600 · GauntletBot 300 · SpinnerBot 260).
    // 최종 보스 No23 은 2000. 임시값이며 인스펙터에서 조정하면 된다.
    const int SoloMaxHp = 600;

    // 단독 변형에서 빼는 패턴. 송전기와, 그 실패 벌칙인 레이지 돌진 — 둘은 한 쌍이다(팀장 확정).
    static readonly string[] ExcludedAttackIds = { "ChargeSequence", "RageDash" };

    // 플레이어는 원점, 최종 보스는 +Z 5.49. 중간보스는 반대쪽에 둬서 로그가 섞여도 구분된다.
    static readonly Vector3 SoloSpawnPos = new Vector3(0.49f, 0f, -5.49f);

    [MenuItem("Tools/Boss/변형 — No23 단독(중간보스) 저작 (V1~V4)")]
    public static void Author()
    {
        BossDataSO solo = AuthorData();
        if (solo == null) return;

        GameObject prefab = AuthorPrefab(solo);
        if (prefab == null) return;

        RegisterNetworkPrefab(prefab);
        AuthorSceneSpawner(prefab);

        AssetDatabase.SaveAssets();
        Verify();
    }

    // ── V1: 데이터 ──────────────────────────────────────────────────────────
    static BossDataSO AuthorData()
    {
        if (AssetDatabase.LoadAssetAtPath<BossDataSO>(SoloDataPath) == null)
        {
            if (!AssetDatabase.CopyAsset(SrcDataPath, SoloDataPath))
            {
                Debug.LogError($"[변형] {SrcDataPath} → {SoloDataPath} 복제 실패.");
                return null;
            }
            Debug.Log($"[변형] {System.IO.Path.GetFileName(SoloDataPath)} 를 복제로 새로 만들었다 — " +
                      "애니 상태명·타이밍 등 나머지 값이 전부 승계된다.");
        }

        var solo = AssetDatabase.LoadAssetAtPath<BossDataSO>(SoloDataPath);
        if (solo == null) { Debug.LogError($"[변형] {SoloDataPath} 로드 실패."); return null; }

        // archetype 은 Boss 로 유지해야 한다 — 바꾸면 ValidateContract 가 첫 줄에서 LogError 를 낸다.
        solo.maxHp = SoloMaxHp;
        solo.hasSuperArmorWhileAttacking = false;
        solo.bombPrefab = null;        // Wells 가 없으니 던질 주체도 없다. 이중 안전.
        solo.chargeZonePrefab = null;  // 송전기를 빼므로 장판도 필요 없다.

        int before = solo.attacks?.Length ?? 0;
        if (solo.attacks != null)
            solo.attacks = solo.attacks
                .Where(a => a != null && !ExcludedAttackIds.Contains(a.attackId.ToString()))
                .ToArray();
        int after = solo.attacks?.Length ?? 0;

        // 페이즈는 개수를 유지하고 시퀀스만 없앤다 — 임계값 내림차순 검증(ValidateContract)을 그대로 통과한다.
        if (solo.phases != null)
            foreach (BossPhaseEntry p in solo.phases)
                p.sequence = BossPhaseSequence.None;

        EditorUtility.SetDirty(solo);
        AssetDatabase.SaveAssets();

        Debug.Log($"[변형] {solo.name}: maxHp={SoloMaxHp} · 패턴 {before}종 → {after}종 " +
                  $"(제외: {string.Join(", ", ExcludedAttackIds)}) · 페이즈 시퀀스 전부 None · bombPrefab 비움.");
        return solo;
    }

    // ── V2: 프리팹 ──────────────────────────────────────────────────────────
    static GameObject AuthorPrefab(BossDataSO solo)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SoloPrefabPath) == null)
        {
            if (!AssetDatabase.CopyAsset(SrcPrefabPath, SoloPrefabPath))
            {
                Debug.LogError($"[변형] {SrcPrefabPath} → {SoloPrefabPath} 복제 실패.");
                return null;
            }
            Debug.Log($"[변형] {System.IO.Path.GetFileName(SoloPrefabPath)} 를 복제로 새로 만들었다.");
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(SoloPrefabPath);
        try
        {
            // Wells 중첩 제거. SK_23.fbx 중첩은 **남겨야 한다** — 모델·리그가 거기 있다.
            var wells = contents.GetComponentInChildren<BossWells>(true);
            if (wells != null)
            {
                GameObject victim = wells.gameObject;
                if (PrefabUtility.IsPartOfPrefabInstance(victim))
                {
                    GameObject outer = PrefabUtility.GetOutermostPrefabInstanceRoot(victim);
                    // 우리 프리팹 루트 자체를 지우면 안 된다.
                    if (outer != null && outer != contents) victim = outer;
                }

                string victimName = victim.name;
                Object.DestroyImmediate(victim);
                Debug.Log($"[변형] Wells 중첩 제거 — '{victimName}'. 폭탄 투척 경로가 함께 사라진다.");
            }
            else
            {
                Debug.Log("[변형] BossWells 가 이미 없다 — 건너뛴다.");
            }

            var boss = contents.GetComponent<TwentyThreeBoss>();
            if (boss == null)
            {
                Debug.LogError("[변형] 복제본에 TwentyThreeBoss 가 없다 — 중단한다.");
                return null;
            }

            var bso = new SerializedObject(boss);
            SerializedProperty dataProp = bso.FindProperty("data");
            if (dataProp == null) { Debug.LogError("[변형] data 필드를 못 찾았다."); return null; }
            dataProp.objectReferenceValue = solo;
            bso.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(contents, SoloPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        AssetDatabase.ImportAsset(SoloPrefabPath, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<GameObject>(SoloPrefabPath);
    }

    // ── V3: NetworkPrefabs 등록 ─────────────────────────────────────────────
    // 빠뜨리면 서버에서는 스폰돼도 **클라에서 프리팹을 못 찾아** 실패한다.
    static void RegisterNetworkPrefab(GameObject prefab)
    {
        var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
        if (list == null) { Debug.LogError($"[변형] {NetworkPrefabsPath} 로드 실패."); return; }

        var so = new SerializedObject(list);
        SerializedProperty arr = so.FindProperty("List");
        if (arr == null) { Debug.LogError("[변형] NetworkPrefabsList 의 List 필드를 못 찾았다."); return; }

        for (int i = 0; i < arr.arraySize; i++)
        {
            SerializedProperty p = arr.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab");
            if (p != null && p.objectReferenceValue == prefab)
            {
                Debug.Log($"[변형] NetworkPrefabs 에 이미 등록돼 있다 (총 {arr.arraySize}개) — 건너뛴다.");
                return;
            }
        }

        arr.InsertArrayElementAtIndex(arr.arraySize);
        SerializedProperty e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
        e.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
        e.FindPropertyRelative("Override").enumValueIndex = 0;
        e.FindPropertyRelative("SourcePrefabToOverride").objectReferenceValue = null;
        e.FindPropertyRelative("SourceHashToOverride").uintValue = 0;
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();

        Debug.Log($"[변형] NetworkPrefabs 에 {prefab.name} 등록 (총 {arr.arraySize}개).");
    }

    // ── V4a: MonsterScene 배치 (MonsterSpawner 경로) ────────────────────────
    // PLAN 의 권고대로 먼저 테스트 씬에서 검증한다. 실제 맵 편입(MapContentSpawner + MonsterGroupID
    // → MapGenConfig)은 V4b 로 남긴다 — 그 애셋이 SVN 관할이라 커밋이 팀장 손에 있다.
    static void AuthorSceneSpawner(GameObject prefab)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != SceneName)
        {
            Debug.LogWarning($"[변형] 활성 씬이 '{scene.name}' 이라 배치를 건너뛴다. " +
                             $"{SceneName} 을 열고 다시 실행하면 배치까지 된다.");
            return;
        }

        GameObject spawnerGo = scene.GetRootGameObjects().FirstOrDefault(g => g.name == SpawnerName);
        if (spawnerGo == null)
        {
            spawnerGo = new GameObject(SpawnerName);
            SceneManager.MoveGameObjectToScene(spawnerGo, scene);
            Debug.Log($"[변형] '{SpawnerName}' 을 새로 만들었다.");
        }
        spawnerGo.transform.position = Vector3.zero;

        if (spawnerGo.GetComponent<NetworkObject>() == null) spawnerGo.AddComponent<NetworkObject>();
        var spawner = spawnerGo.GetComponent<MonsterSpawner>();
        if (spawner == null) spawner = spawnerGo.AddComponent<MonsterSpawner>();

        Transform pointTf = spawnerGo.transform.Find(PointName);
        if (pointTf == null)
        {
            var pointGo = new GameObject(PointName);
            pointGo.transform.SetParent(spawnerGo.transform);
            pointTf = pointGo.transform;
            Debug.Log($"[변형] '{PointName}' 을 새로 만들었다.");
        }
        pointTf.position = SoloSpawnPos;
        var point = pointTf.GetComponent<MonsterSpawnPoint>();
        if (point == null) point = pointTf.gameObject.AddComponent<MonsterSpawnPoint>();

        var pso = new SerializedObject(point);
        pso.FindProperty("monsterPrefabOverride").objectReferenceValue = prefab;
        pso.FindProperty("count").intValue = 1;
        pso.FindProperty("scatterRadius").floatValue = 0f;
        pso.ApplyModifiedPropertiesWithoutUndo();

        var sso = new SerializedObject(spawner);
        sso.FindProperty("defaultMonsterPrefab").objectReferenceValue = prefab;
        sso.FindProperty("autoSpawnOnStart").boolValue = true;
        sso.FindProperty("maxAlive").intValue = 0;
        SerializedProperty pts = sso.FindProperty("spawnPoints");
        pts.arraySize = 1;
        pts.GetArrayElementAtIndex(0).objectReferenceValue = point;
        sso.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[변형] {SpawnerName} 배선 완료 — {prefab.name} 1기 @ {SoloSpawnPos} " +
                  "(플레이어 원점 · 최종 보스 +Z5.49 반대쪽).");
    }

    // ── 검증 (읽기 전용) ────────────────────────────────────────────────────
    [MenuItem("Tools/Boss/변형 — No23 단독 검증 (읽기 전용)")]
    public static void Verify()
    {
        var sb = new StringBuilder("[변형] No23 단독 검증\n");

        var solo = AssetDatabase.LoadAssetAtPath<BossDataSO>(SoloDataPath);
        if (solo == null) sb.AppendLine("  ✗ No23_Solo.asset 없음");
        else
        {
            bool noExcluded = solo.attacks != null &&
                              !solo.attacks.Any(a => a != null && ExcludedAttackIds.Contains(a.attackId.ToString()));
            bool seqNone = solo.phases == null || solo.phases.All(p => p.sequence == BossPhaseSequence.None);
            sb.AppendLine($"  {M(solo.archetype == MonsterArchetype.Boss)} archetype={solo.archetype} (Boss 여야 한다)");
            sb.AppendLine($"  {M(solo.maxHp == SoloMaxHp)} maxHp={solo.maxHp}");
            sb.AppendLine($"  {M(noExcluded)} 패턴 {solo.attacks?.Length ?? 0}종 — {string.Join(", ", (solo.attacks ?? new BossAttackEntry[0]).Select(a => a.attackId.ToString()))}");
            sb.AppendLine($"  {M(seqNone)} 페이즈 시퀀스 전부 None");
            sb.AppendLine($"  {M(solo.bombPrefab == null)} bombPrefab 비움 · {M(solo.chargeZonePrefab == null)} chargeZonePrefab 비움");
            sb.AppendLine($"  {M(!solo.hasSuperArmorWhileAttacking)} hasSuperArmorWhileAttacking off");
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SoloPrefabPath);
        if (prefab == null) sb.AppendLine("  ✗ TwentyThree_Solo.prefab 없음");
        else
        {
            int wells = prefab.GetComponentsInChildren<BossWells>(true).Length;
            var boss = prefab.GetComponent<TwentyThreeBoss>();
            var bso = boss != null ? new SerializedObject(boss) : null;
            Object wired = bso?.FindProperty("data")?.objectReferenceValue;
            int anchors = prefab.GetComponentsInChildren<ColliderInfo>(true).Length;
            sb.AppendLine($"  {M(wells == 0)} BossWells {wells}개 (0이어야 한다)");
            sb.AppendLine($"  {M(wired == solo)} data = {(wired != null ? wired.name : "(비어 있음)")}");
            sb.AppendLine($"  {M(prefab.GetComponent<NetworkObject>() != null)} NetworkObject · ColliderInfo 앵커 {anchors}개");

            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            bool registered = false;
            int total = 0;
            if (list != null)
            {
                var so = new SerializedObject(list);
                SerializedProperty arr = so.FindProperty("List");
                total = arr != null ? arr.arraySize : 0;
                for (int i = 0; i < total; i++)
                    if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab")?.objectReferenceValue == prefab)
                        registered = true;
            }
            sb.AppendLine($"  {M(registered)} NetworkPrefabs 등록 (총 {total}개)");
        }

        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != SceneName) sb.AppendLine($"  – 활성 씬이 '{scene.name}' 이라 배치 검증 생략");
        else
        {
            GameObject sp = scene.GetRootGameObjects().FirstOrDefault(g => g.name == SpawnerName);
            sb.AppendLine($"  {M(sp != null)} {SpawnerName} 존재");
            if (sp != null)
            {
                Transform pt = sp.transform.Find(PointName);
                sb.AppendLine($"  {M(pt != null)} {PointName} @ {(pt != null ? pt.position.ToString() : "-")}");
                sb.AppendLine($"  {M(sp.GetComponent<NetworkObject>() != null)} 스포너에 NetworkObject");
            }
        }

        Debug.Log(sb.ToString());
    }

    static string M(bool ok) => ok ? "✓" : "✗";
}
