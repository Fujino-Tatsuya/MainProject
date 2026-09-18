using UnityEngine;

// 몬스터 원거리 공격기. BaseAttack을 상속해 damage/targetLayer/AttackInfo 스냅샷 파이프라인을 근접과 동일하게 재사용한다.
// MonsterBase가 공격 선딜(attackWindup) 시점에 Fire()를 호출 → 서버에서 투사체(NetworkObject)를 스폰·발사한다.
// 근접(MonsterMeleeAttack)의 Hit()에 대응하는 원거리판.
public class MonsterRangedAttack : BaseAttack
{
    [Header("투사체")]
    [SerializeField] private GameObject projectilePrefab; // 비우면 MonsterBase가 data에서 주입(ConfigureProjectile)
    [SerializeField] private Transform muzzle;            // 발사 원점(비우면 자기 transform)
    [SerializeField] private float projectileSpeed = 12f;
    [SerializeField] private float projectileLifetime = 4f;
    private float arcHeight;    // 0=직선(기존), >0=포물선 정점 높이(m). MonsterDataSO.projectileArcHeight로 주입.
    private float splashRadius; // 0=직격만, >0=착탄 지점 반경 스플래시. MonsterDataSO.projectileSplashRadius로 주입.

    private Unit _ownerUnit;

    private void Awake()
    {
        InitializeAttackInfo();
        _ownerUnit = GetComponentInParent<Unit>();
    }

    // 데이터 주도 설정 주입(MonsterBase.ServerInitialize에서 호출). 인스펙터 값이 있으면 그대로 두고,
    // data 쪽 값이 유효할 때만 덮어쓴다.
    public void ConfigureProjectile(GameObject prefab, float speed, float lifetime, float arcHeight = 0f, float splashRadius = 0f)
    {
        if (prefab != null) projectilePrefab = prefab;
        if (speed > 0f) projectileSpeed = speed;
        if (lifetime > 0f) projectileLifetime = lifetime;
        this.arcHeight = arcHeight;
        this.splashRadius = splashRadius;
    }

    // 서버에서 targetPoint를 향해 투사체 발사.
    /// <summary>
    /// <b>지정한 방향</b>으로 발사한다. 조준선(예고선)과 탄 방향을 일치시킬 때 쓴다.
    ///
    /// 🔴 왜 필요한가(2026-09-14 팀장 확인): <see cref="Fire"/> 는 넘겨받은 지점을 향해 쏘는데,
    /// <c>MonsterBase</c> 가 넘기는 것은 <b>발사 순간의 플레이어 위치</b>다. 고정 터렛은 예고선을
    /// 미리 고정해 두므로 그 방식이면 <b>선과 탄이 어긋난다</b> — 피했는데 맞는다.
    /// 총구에서 이 방향으로 <paramref name="distance"/> 만큼 떨어진 점을 목표로 삼아
    /// <see cref="Fire"/> 에 그대로 넘긴다(탄도 계산 경로를 하나로 유지한다).
    /// </summary>
    public void FireDirection(Vector3 direction, float distance)
    {
        if (direction.sqrMagnitude < 0.0001f) return;
        Vector3 origin = muzzle != null ? muzzle.position : transform.position;
        Fire(origin + direction.normalized * Mathf.Max(1f, distance));
    }

    public void Fire(Vector3 targetPoint)
    {
        if (!IsServer)
            return;
        if (projectilePrefab == null)
        {
            Debug.LogError("MonsterRangedAttack에 projectilePrefab이 필요합니다(인스펙터 또는 MonsterDataSO).", this);
            return;
        }

        Vector3 origin = muzzle != null ? muzzle.position : transform.position;
        Vector3 dir = targetPoint - origin;
        if (dir.sqrMagnitude < 0.0001f)
            dir = transform.forward;

        GameObject go = Instantiate(projectilePrefab, origin, Quaternion.LookRotation(dir));

        var netObj = go.GetComponent<Unity.Netcode.NetworkObject>();
        var proj = go.GetComponent<MonsterProjectile>();
        if (netObj == null || proj == null)
        {
            Debug.LogError("projectilePrefab에 NetworkObject + MonsterProjectile이 모두 필요합니다.", this);
            Destroy(go);
            return;
        }

        netObj.Spawn(true);

        if (arcHeight > 0f)
        {
            // 포물선 탄도(MortarBot 등). 발사 시점 타깃 지점 조준 — 유도 없음, 회피 가능.
            float g = -Physics.gravity.y;
            Vector3 target = targetPoint;
            float apexY = Mathf.Max(origin.y, target.y) + arcHeight;
            float hUp = apexY - origin.y;
            float hDown = Mathf.Max(0.1f, apexY - target.y);
            float vy = Mathf.Sqrt(2f * g * hUp);
            float tUp = vy / g;
            float tDown = Mathf.Sqrt(2f * hDown / g);
            float tTotal = Mathf.Max(0.1f, tUp + tDown);

            Vector3 flat = target - origin;
            flat.y = 0f;
            Vector3 vHoriz = flat / tTotal;

            Vector3 v0 = vHoriz + Vector3.up * vy;

            proj.LaunchBallistic(_ownerUnit, v0, damage, targetLayer, projectileLifetime, splashRadius);
        }
        else
        {
            proj.Launch(_ownerUnit, dir, projectileSpeed, damage, targetLayer, projectileLifetime);
        }
    }
}
