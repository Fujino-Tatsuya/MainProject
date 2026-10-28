using UnityEngine;

// 23호 넉백 입구(할 일 134 — 경석 handoff 보스넉백·기절). 규칙 = BossCrowdControlRules.
// 넉백은 Q·간파·특성 전부 Unit.Knockback 한 입구로 들어온다(서버 가드·슈퍼아머 거부는 거기서).
// 고정 거리·콤보 끊기·NavMesh 정지·벽 튕김·점프 체인/필수 기믹 예외는 전부 23호 자체 밀림(StartVulnerableKnockback)이 든다 —
// 일반 LinearKnockback(IKnockbackable)은 쓰지 않는다.
public partial class TwentyThreeBoss
{
    /// <summary>
    /// [서버] 넉백 입구의 23호 처리 — 취약이면 자체 밀림(고정 거리), 아니면 무시한다(오류 로그 없음).
    /// 🔴 base 를 부르지 않는다 — 23호엔 IKnockbackable 이 없어 base 는 오류 로그만 찍는다.
    /// <paramref name="strength"/> 는 쓰지 않는다(거리 = <c>vulnerableKnockbackDistance</c>).
    /// </summary>
    protected override void OnKnockback(Vector3 direction, float strength)
    {
        if (!BossCrowdControlRules.AcceptsVulnerableKnockback(
                IsVulnerable, IsInMandatorySequence, IsInJumpChain, State == MonsterState.Dead))
            return;

        StartVulnerableKnockback(direction);
    }

    /// <summary>취약 밀림 방향 — 공격자 반대(방향 무관 넉백, 기획 취약 §5.1). 겹치면 스킬 공격 방향 → 공격자 정면.</summary>
    Vector3 VulnerableKnockbackDirection(AttackHitContext ctx, AttackInfo info)
    {
        Vector3 from = ctx.sourceTransform != null ? ctx.sourceTransform.position : ctx.sourcePosition;
        Vector3 dir = transform.position - from;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)   // 위치가 겹쳐 방향을 못 구하면 스킬 공격 방향(§5.1)
        {
            dir = info.knockbackDirection;
            if (dir.sqrMagnitude < 0.0001f && ctx.sourceTransform != null) dir = ctx.sourceTransform.forward;
            dir.y = 0f;
        }
        return dir;
    }
}
