using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// 개발용 단독 테스트 부트스트랩 (MonsterScene 전용).
// Play 시작 시 자동으로 StartHost → 호스트 플레이어 스폰.
// 이 브랜치는 플레이어 프리팹 전투가 깨져 있어(Player=NetworkAnimator NRE / Paladin=히트박스 없음),
// 몬스터 FSM만 검증할 수 있도록 좌클릭 "디버그 공격"으로 플레이어 공격을 임시 대체한다.
//
// ⚠️ 실제 게임 씬/빌드에는 넣지 말 것. 검증용 임시 오브젝트에만 부착한다.
[DisallowMultipleComponent]
public class MonsterTestBootstrap : MonoBehaviour
{
    [Header("플레이어")]
    [SerializeField] private GameObject playerPrefab;              // NRE 없는 Paladin 권장(이동만 필요, 공격은 아래 디버그로 대체)
    [SerializeField] private Transform playerSpawnPoint;          // 비우면 fallback 위치 사용
    [SerializeField] private Vector3 fallbackSpawnPosition = new Vector3(0f, 1f, 3f);

    [Header("옵션")]
    [SerializeField] private bool autoStartHostOnPlay = true;
    [SerializeField] private bool spawnHostPlayer = true;

    [Header("디버그 공격 (플레이어 공격이 깨진 브랜치의 임시 대체 - 좌클릭)")]
    [SerializeField] private bool enableDebugAttack = true;
    [SerializeField] private int debugAttackDamage = 10;
    [SerializeField] private LayerMask debugAttackTargetLayer;                 // Enemy(8)로 지정할 것
    [SerializeField] private Vector3 debugAttackBoxHalfExtents = new Vector3(1f, 1f, 1.25f);
    [SerializeField] private float debugAttackForwardOffset = 1.25f;

    [Header("디버그 넉백/경직 (지속넉백→Stunned 시퀀스 검증 — 0이면 데미지만)")]
    [SerializeField, Min(0f)] private float debugKnockbackStrength = 3f;   // 지속 밀기 속도(m/s)
    [SerializeField, Min(0f)] private float debugKnockbackDuration = 0.3f; // 지속 밀기 시간(초)
    [SerializeField, Min(0f)] private float debugStaggerDuration = 0.2f;   // 종료 후 Stunned 경직(초)

    private Transform _hostPlayer;
    private readonly Collider[] _debugHits = new Collider[16];

    private void Start()
    {
        if (!autoStartHostOnPlay) return;

        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null)
        {
            Debug.LogError("[MonsterTestBootstrap] NetworkManager.Singleton이 없습니다. 씬에 NetworkManager가 있는지 확인하세요.");
            return;
        }
        if (nm.IsListening)
        {
            Debug.LogWarning("[MonsterTestBootstrap] 이미 네트워크가 시작된 상태입니다.");
            return;
        }

        nm.OnServerStarted += HandleServerStarted;
        if (!nm.StartHost())
        {
            nm.OnServerStarted -= HandleServerStarted;
            Debug.LogError("[MonsterTestBootstrap] StartHost 실패.");
        }
    }

    private void HandleServerStarted()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null) nm.OnServerStarted -= HandleServerStarted;

        if (spawnHostPlayer)
            SpawnHostPlayer(nm);
    }

    private void SpawnHostPlayer(NetworkManager nm)
    {
        if (nm == null || !nm.IsServer) return;

        ulong hostId = nm.LocalClientId;
        if (nm.ConnectedClients.TryGetValue(hostId, out NetworkClient client) && client.PlayerObject != null)
        {
            _hostPlayer = client.PlayerObject.transform;
            Debug.Log("[MonsterTestBootstrap] 호스트에 이미 PlayerObject가 있어 스폰을 건너뜁니다.");
            return;
        }

        if (playerPrefab == null)
        {
            Debug.LogError("[MonsterTestBootstrap] playerPrefab이 할당되지 않았습니다. (Paladin 지정 권장)");
            return;
        }
        if (!playerPrefab.TryGetComponent(out NetworkObject _))
        {
            Debug.LogError($"[MonsterTestBootstrap] playerPrefab '{playerPrefab.name}'에 NetworkObject가 없습니다.");
            return;
        }

        Vector3 pos = playerSpawnPoint != null ? playerSpawnPoint.position : fallbackSpawnPosition;
        Quaternion rot = playerSpawnPoint != null ? playerSpawnPoint.rotation : Quaternion.identity;

        GameObject go = Instantiate(playerPrefab, pos, rot);
        go.GetComponent<NetworkObject>().SpawnAsPlayerObject(hostId, true);
        _hostPlayer = go.transform;
        Debug.Log($"[MonsterTestBootstrap] 호스트 플레이어 스폰 완료 @ {pos}");
    }

    private void Update()
    {
        if (!enableDebugAttack) return;

        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || _hostPlayer == null) return;

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            DoDebugAttack();
    }

    // 호스트(=서버)에서 플레이어 전방 박스 오버랩 → Enemy 레이어의 Hurtbox/Unit에 데미지.
    // 플레이어 공격 시스템이 깨진 브랜치에서 몹 피격/사망을 검증하기 위한 임시 수단.
    private void DoDebugAttack()
    {
        Vector3 center = _hostPlayer.position
            + _hostPlayer.forward * debugAttackForwardOffset
            + Vector3.up * 0.5f;

        // Target Layer가 미설정(0)이면 전체 레이어를 오버랩한다(레이어 설정을 깜빡해도 동작하게).
        int mask = debugAttackTargetLayer.value != 0 ? debugAttackTargetLayer.value : ~0;

        int n = Physics.OverlapBoxNonAlloc(
            center, debugAttackBoxHalfExtents, _debugHits,
            _hostPlayer.rotation, mask, QueryTriggerInteraction.Collide);

        // 방향 = 플레이어 전방 명시(수평) — 방향성 밀기 검증용(방사형 폴백 검증은 zero로 바꿔서).
        Vector3 knockDir = _hostPlayer.forward;
        knockDir.y = 0f;
        AttackInfo info = new AttackInfo(debugAttackDamage, AttackType.Default, false,
            debugKnockbackStrength, debugKnockbackDuration, debugStaggerDuration, knockDir);
        int applied = 0;
        for (int i = 0; i < n; i++)
        {
            Collider hit = _debugHits[i];
            if (hit == null) continue;

            // 자기 자신(플레이어)은 제외.
            Unit unit = hit.GetComponentInParent<Unit>();
            if (unit != null && unit.transform == _hostPlayer) continue;

            Hurtbox hurtbox = hit.GetComponentInParent<Hurtbox>();
            if (hurtbox != null)
            {
                hurtbox.ReceiveAttack(info, new AttackHitContext(center, _hostPlayer));
                applied++;
                continue;
            }

            if (unit != null)
            {
                unit.ReceiveAttack(info, new AttackHitContext(center, _hostPlayer));
                applied++;
            }
        }

        string maskLabel = debugAttackTargetLayer.value != 0 ? "설정됨" : "ALL(미설정)";
        Debug.Log($"[MonsterTestBootstrap] 디버그공격 overlap={n} applied={applied} dmg={debugAttackDamage} mask={maskLabel}");
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;
    }
}
