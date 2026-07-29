using UnityEngine;

/// <summary>
/// 몬스터가 "이것을 노리고 쫓고 때려도 되는 대상인가"를 판정하는 <b>단일 기준</b>.
///
/// 왜 필요한가: 기존 <c>IsTargetValid</c>는 <c>null</c>과 <c>activeInHierarchy</c>만 봤다. 그런데
/// Soul(유령) 플레이어는 오브젝트가 그대로 활성이라 판정을 통과했다. 그래서 사망 직전에 잡힌
/// 타겟이 그대로 유지되어 몬스터가 유령을 계속 쫓고 공격 모션까지 냈다. 데미지는 Soul에서
/// hurtbox 콜라이더가 꺼지므로 안 들어갔지만(<c>PlayerSoulController.SetHurtboxesEnabled</c>),
/// <b>쫓고 때리는 행동 자체가 남아</b> 유령을 발견하는 것처럼 보였다.
///
/// 판정 기준을 <c>ShouldEnableHurtbox</c>가 아니라 <see cref="PlayerLifeState.Alive"/>로 두는 이유:
/// hurtbox 정책은 앞으로 무적 프레임(대시 회피 등)에도 쓰일 수 있다. 그때 "맞지 않는다"를
/// "노리지 않는다"로 해석하면 몬스터가 대시 한 번에 타겟을 놓친다. 타겟팅은 <b>생명주기</b>로만 판단한다.
/// </summary>
public static class MonsterTargeting
{
    /// <summary>플레이어가 아닌 대상은 종전대로 유효하다(생명주기 개념이 없으므로).</summary>
    public static bool IsAttackable(Transform target)
    {
        if (target == null || !target.gameObject.activeInHierarchy) return false;

        // 콜라이더가 자식일 수 있으므로 부모로 거슬러 찾는다.
        PlayerLifeCycleController lifeCycle = target.GetComponentInParent<PlayerLifeCycleController>();
        if (lifeCycle == null) return true;

        return lifeCycle.State == PlayerLifeState.Alive;
    }

    public static bool IsAttackable(Collider target)
        => target != null && IsAttackable(target.transform);
}
