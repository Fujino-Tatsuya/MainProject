using UnityEngine;

/// <summary>커서 레이 한 발의 판정 결과. 커서 모양(조준 중/무효 대상)을 가르는 데 쓴다.</summary>
public enum CursorPickResult
{
    NoHit,          // 마스크 안의 콜라이더에 안 맞았다
    InvalidTarget,  // 맞았지만 고를 수 없는 대상(유닛 아님·자기 자신·HP 0)
    ValidTarget,
}

/// <summary>
/// 커서 아래 유닛 판정 한 곳 — 스킬 조준(<see cref="PlayerSkillTargeting"/> SingleTarget)과
/// 몬스터 호버 외곽선(<see cref="PlayerEnemyHoverOutline"/>)이 같은 규칙을 쓴다.
/// 레이 → 마스크 안 첫 콜라이더 → <see cref="ResolveUnit"/>(Hurtbox 우선) → 자기 자신·HP 0 제외.
/// </summary>
public static class CursorUnitPick
{
    public const float RaycastMaxDistance = 200f;

    public static CursorPickResult Pick(Ray ray, LayerMask mask, Unit self, out Unit unit)
    {
        unit = null;
        if (!Physics.Raycast(ray, out RaycastHit hit, RaycastMaxDistance, mask, QueryTriggerInteraction.Collide))
            return CursorPickResult.NoHit;

        Unit resolved = ResolveUnit(hit.collider);
        if (!IsSelectable(resolved, self))
            return CursorPickResult.InvalidTarget;

        unit = resolved;
        return CursorPickResult.ValidTarget;
    }

    public static bool IsSelectable(Unit unit, Unit self)
    {
        return unit != null && IsSelectable(unit == self, unit.CurrentHealth);
    }

    /// <summary>순수 규칙 — 자기 자신이 아니고 살아 있어야(HP &gt; 0) 고를 수 있다. 사망 처리 중인 몬스터는 HP 0 이다.</summary>
    public static bool IsSelectable(bool isSelf, int currentHealth)
    {
        return !isSelf && currentHealth > 0;
    }

    // 레이캐스트 히트 → Unit 해석 (Hurtbox 우선, 없으면 상위 Unit). FirstMeleeMainSkill.ResolveUnit 패턴과 동일.
    public static Unit ResolveUnit(Collider hit)
    {
        if (hit == null)
            return null;

        Hurtbox hurtbox = hit.GetComponentInParent<Hurtbox>();
        if (hurtbox != null && hurtbox.TryGetOwner(out Unit hurtboxOwner))
            return hurtboxOwner;

        return hit.GetComponentInParent<Unit>();
    }
}
