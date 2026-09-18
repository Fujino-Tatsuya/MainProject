using UnityEngine;

/// <summary>
/// 방어막에 "이 방향에서 맞았다"고 알려 주는 창구. <see cref="ShieldActivate"/>의 파문과,
/// 선택적으로 표면 폭발 이펙트를 함께 띄운다.
///
/// <b>왜 별도 컴포넌트인가</b>: 에셋 팩 원본에서 ShieldActivate를 호출하는 것은
/// ObjectMoveDestroy(레이캐스트로 날아가는 총알) 하나뿐이다. 근접 공격이나 판정이
/// 로직에서 내려오는 공격은 씬에 투사체가 존재하지 않아 파문을 띄울 방법이 없다.
/// 방향만 있으면 되는 입구를 따로 둔다.
/// </summary>
public class ShieldHitReceiver : MonoBehaviour
{
    [Tooltip("비워 두면 자식에서 자동으로 찾는다.")]
    public ShieldActivate shield;

    [Tooltip("피격 지점 표면에서 터질 이펙트. 비워 두면 파문만 뜬다.")]
    public GameObject hitBurstPrefab;

    [Tooltip("hitBurstPrefab을 자동 삭제하기까지의 시간. 0 이하면 삭제하지 않는다.")]
    public float burstLifetime = 1f;

    [Tooltip("0 이하면 방어막의 SphereCollider에서 월드 반지름을 자동으로 구한다.")]
    public float shieldRadiusOverride;

    void Reset()
    {
        shield = GetComponentInChildren<ShieldActivate>();
    }

    void Awake()
    {
        if (shield == null)
            shield = GetComponentInChildren<ShieldActivate>();
    }

    /// <summary>공격이 들어온 지점, 또는 공격자의 위치를 월드 좌표로 넘긴다.</summary>
    public void HitFromPosition(Vector3 worldPosition)
    {
        if (shield == null) return;
        HitFromDirection(worldPosition - shield.transform.position);
    }

    /// <summary>공격이 들어온 방향만 넘긴다. 벡터의 길이는 무시된다.</summary>
    public void HitFromDirection(Vector3 direction)
    {
        if (shield == null) return;

        // 정확히 중심에서 맞은 경우. 방향이 없으니 파문을 어디에 띄울지 정할 수 없다.
        if (direction.sqrMagnitude < 1e-6f) return;

        Vector3 dir = direction.normalized;
        Vector3 center = shield.transform.position;

        // AddHitObject가 내부에서 정규화하므로 거리는 아무 값이나 된다.
        shield.AddHitObject(center + dir);

        if (hitBurstPrefab == null) return;

        // 구 표면의 법선은 중심에서 바깥을 향하는 방향과 같다. 에셋 팩이
        // Quaternion.LookRotation(hit.normal)로 만드는 것과 같은 회전이 나온다.
        GameObject burst = Instantiate(hitBurstPrefab,
                                       center + dir * GetShieldRadius(),
                                       Quaternion.LookRotation(dir));

        // 방어막이 캐릭터를 따라 움직여도 같이 가도록 붙이되, 방어막 메쉬의 스케일(보통 4)을
        // 물려받지 않게 this 밑에 둔다.
        burst.transform.SetParent(transform, true);
        burst.transform.localScale = Vector3.one;

        if (burstLifetime > 0f)
            Destroy(burst, burstLifetime);
    }

    /// <summary>
    /// 방어막 메쉬의 월드 반지름. SphereCollider가 이미 정답을 들고 있어서 숫자를 손으로
    /// 맞출 필요가 없다. 방어막 스케일을 바꿔도 따라온다.
    /// </summary>
    float GetShieldRadius()
    {
        if (shieldRadiusOverride > 0f)
            return shieldRadiusOverride;

        SphereCollider sphere = shield.GetComponent<SphereCollider>();
        if (sphere == null)
            return 1f;

        Vector3 scale = shield.transform.lossyScale;
        float max = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        return sphere.radius * max;
    }
}
