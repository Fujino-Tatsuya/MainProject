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
