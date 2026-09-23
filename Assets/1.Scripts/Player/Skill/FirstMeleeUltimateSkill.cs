using UnityEngine;

/// <summary>
/// R 최후의 심판 — 대상 지정(SingleTarget) 채널링 스킬. 조준 모드에서 사거리 내 적을 지정해 시전하고,
/// 채널을 완주하면 지정 대상에게 단일 피해를 1회 적용한다(타겟팅 인프라 검증용 최소 효과).
/// 대상 유효성(생존·사거리)은 서버 CanUse로 권위 검증하며, 채널 중 대상이 사라지면 취소한다.
/// 상세 메커니즘·연출은 후속 작업.
/// </summary>
public class FirstMeleeUltimateSkill : PlayerChannelingSkill
{
    [Header("연출")]
    [Tooltip("조준 연출(낙뢰 + 칼날 전기) 전파 창구. 플레이어 루트의 PlayerSkillVfx 를 물린다. 비워두면 연출만 빠진다")]
    [SerializeField] private PlayerSkillVfx skillVfx;

    private Unit lockedTarget;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Ultimate;

    // 채널 중 이동/회전 잠금
    public override bool CanMoveWhileActive => false;

    // 서버 권위 시전 조건: 대상이 살아있는 적이고 사거리 내여야 한다.
    public override bool CanUse(Vector3 direction, Unit target)
    {
        if (Data == null || target == null || target == owner)
            return false;

        if (target.CurrentHealth <= 0)
            return false;

        return IsWithinRange(target.transform.position);
    }

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        base.OnServerStart(direction, target);
        lockedTarget = target;

        // 🔴 여기는 서버에서만 돈다 — 그래서 창구를 거친다. 누구를 지목했는지는
        //    PlaySkillClientRpc 에 실려 가지 않아(SingleTarget 은 aimPoint 를 안 쓴다)
        //    리모트 클라가 알 방법이 없다.
        skillVfx?.ServerPlayTargetFloor(lockedTarget);
    }

    // 🔴 낙뢰는 여기서 켜지 않는다. R 은 조준만 켜고(ClickToConfirm), 실제 시전은 좌클릭 확정
    //    — 사거리 밖이면 걸어서 접근까지 한 뒤다. 그 사이 간격이 정해져 있지 않아 R 과 연출이 따로 논다.
    //    그래서 시작은 OnOwnerAimStart 로 올라가 있고, 여기는 시전 고유 연출 자리로 비워 둔다.
    public override void OnClientPlay(Vector3 direction)
    {
    }

    /// <summary>[오너] R 을 눌러 조준에 들어갔다. 낙뢰를 바로 떨어뜨리고, 이어서 칼날에 전기가 흐른다.</summary>
    public override void OnOwnerAimStart()
    {
        // 오너 전용 경로라 로컬 재생만으로는 남에게 안 보인다 — 창구가 서버를 거쳐 나머지 피어에 뿌린다.
        skillVfx?.OwnerPlayUltimateVfx();
    }

    /// <summary>[오너] 시전까지 가지 못하고 조준이 물렸다. 연출을 걷는다.</summary>
    public override void OnOwnerAimCancelled()
    {
        skillVfx?.OwnerStopUltimateVfx();
    }

    public override void OnTick()
    {
        // 채널 중 대상이 사라지거나 사망하면 취소 (완주 판정은 base가 처리)
        if (State == SkillState.Channeling && (lockedTarget == null || lockedTarget.CurrentHealth <= 0))
        {
            EndSelf(SkillEndReason.Cancelled);
            return;
        }

        base.OnTick();
    }

    protected override void OnChannelCompleted()
    {
        if (lockedTarget == null || lockedTarget.CurrentHealth <= 0)
            return;

        AttackInfo attackInfo = new AttackInfo(damageSnapshot, AttackType.Skill);
        AttackHitContext hitContext = new AttackHitContext(owner.transform.position, owner.transform);
        lockedTarget.ReceiveAttack(attackInfo, hitContext);

        // 🔴 여기도 서버 전용이라 창구를 거친다(OnChannelCompleted 는 TickServer 경로다).
        //    위치는 넘기지 않는다 — 각 피어가 표식을 깔 때 잡아 둔 그 트랜스폼에 떨어진다.
        skillVfx?.ServerPlayTargetStrike();

        Edit.Log($"[Skill] 최후의 심판 — {lockedTarget.name}에게 피해 {attackInfo.damage}", this);
    }

    public override void OnEnd(SkillEndReason reason)
    {
        base.OnEnd(reason);
        lockedTarget = null;

        // 🔴 여기는 RPC 가 필요 없다 — OnEnd 는 전 피어에서 돈다(서버는 EndActiveSkillServer,
        //    클라는 EndSkillClientRpc). 각 피어가 자기 몫을 걷으면 끝이다.
        //    조준만 하고 물린 경우는 이 경로를 타지 않는다 — 그쪽은 OnOwnerAimCancelled 가 받는다.
        skillVfx?.StopUltimateVfxLocal();
        skillVfx?.StopTargetFloorLocal();
    }

    private bool IsWithinRange(Vector3 worldPoint)
    {
        Vector3 flat = worldPoint - owner.transform.position;
        flat.y = 0f;
        return flat.sqrMagnitude <= Data.CastRange * Data.CastRange;
    }
}
