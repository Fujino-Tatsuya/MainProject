using Unity.Netcode;
using UnityEngine;

// 보스 테스트 씬의 스폰 주체. 서버가 아레나 안에 23호를 1기 만든다.
//
// 🔴 2026-08-08 — 보스 재작성에 맞춰 **죽은 배선 2건을 걷어냈다.**
//    이전 판은 스폰 직후 `ChargeController` 와 `EnemyBTActivator` 를 찾아 목록을 넘겼는데,
//    둘 다 레거시 BT 계열이라 보스 프리팹에서 제거됐다. 그대로 두면 스폰은 되지만
//    `ChargeController 가 없습니다` LogError 를 내고 **early return** 해서, 그 뒤로 아무것도
//    실행되지 않는다(로그도 더럽힌다).
//
// 🔴 송전탑 목록도 없앴다. 신규 `BossChargingPylon` 은 **정적 레지스트리**(`BossChargingPylon.Active`)에
//    스폰 시 자기가 등록하고, `BossChargeSequence` 가 거기서 필요한 수만큼 고른다.
//    기둥은 아레나에 있고 매니저는 보스에 붙으므로 부모-자식 탐색으로는 서로를 못 찾는데,
//    그 문제를 레지스트리로 이미 풀어 뒀다. 즉 여기서 목록을 넘길 이유가 사라졌다.
//
// ⚠️ 이 파일은 아직 레거시 폴더(`Enemy/Boss/`)에 있다. CONTEXT 4단계에서 그 폴더를 지울 때
//    `Monster/Boss/` 로 **옮겨야 한다**(지우면 안 된다 — 씬 3개가 이 컴포넌트를 참조한다).
//    파일을 옮기면 .meta 가 따라가 GUID 가 유지되므로 씬 참조는 깨지지 않는다.
public class TwentyThreeArenaContext : NetworkBehaviour
{
    [SerializeField]
    [Tooltip("스폰할 보스 프리팹(TwentyThree). NetworkObject 가 있어야 한다.")]
    GameObject bossPrefab;

    [SerializeField]
    [Tooltip("보스를 놓을 아레나 내 위치(월드).")]
    Vector3 bossPos;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer) return;

        if (bossPrefab == null)
        {
            Debug.LogError($"{name}: bossPrefab 이 비어 있다 — 스폰할 보스가 없다.", this);
            return;
        }

        // GetComponent 는 프리팹 원본이 아니라 **인스턴스**에서 해야 한다.
        GameObject instance = Instantiate(bossPrefab, bossPos, Quaternion.identity);
        NetworkObject boss = instance.GetComponent<NetworkObject>();
        if (boss == null)
        {
            Debug.LogError($"{name}: {bossPrefab.name} 에 NetworkObject 가 없다 — 스폰할 수 없다.", this);
            Destroy(instance);
            return;
        }

        boss.Spawn();

        // 이후 배선은 없다 — 보스가 자기 컴포넌트를 스스로 찾고(MonsterBase.OnNetworkSpawn),
        // 송전탑은 BossChargingPylon.Active 로 자기 등록한다.
    }
}
