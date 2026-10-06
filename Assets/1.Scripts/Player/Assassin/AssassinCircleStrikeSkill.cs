using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 어쌔신 변신 E 원형 5타 — 변신 중 Sub 슬롯 대체 스킬(alternateSkills[Sub], character_assassin.md §9, PLAN-assassin A9).
///
/// 조준 = 공용 GroundPoint + 고정 거리(A4): 원 중심이 플레이어에서 castRange(1.5m) 떨어져 마우스 방향으로 돈다, 효과 반경 aoeRadius(2m).
/// 조준 중 이동 가능·무적 없음·쿨 없음. 조준 중 변신이 끝나면(만료·R 해제) <see cref="AssassinState"/> 가 오너 조준을 취소한다(한 곳).
///
/// 확정(좌클릭) = 서버 승인 = 공격 시작: 쿨 12초 시작(자동 커밋), 플레이어 위치·원 중심(서버 재투영 지점)을 고정한다.
/// 이동·순간이동 없음 — Skill_02_Move_000pct 의 모델 움직임은 연출이다(루트모션 꺼짐).
/// 클립 Hit 이벤트마다 서버가 원 안 대상을 다시 고른다(지형·벽 차단 검사 없음, 상자 포함 일반 피해 경로, 같은 타에서 대상 1회).
/// 공격 시작~종료 무적(<see cref="InvulnerabilityCause.SkillAction"/>), 강제 중단·사망이면 OnEnd 가 즉시 해제하고 남은 타를 버린다.
/// 공격 중 이동·대시·다른 스킬 불가(스킬 상태), 변신 만료·R 해제는 FSM 이 Skill 을 벗어난 뒤 AssassinState 가 처리한다(5타 완료 후 종료).
/// 변신 중이라 스택은 없다. 백어택 배율은 A11.
/// </summary>
public sealed class AssassinCircleStrikeSkill : PlayerSkillBase
{
    private AssassinState assassinState;
    private PlayerInvulnerability invulnerability;
    private Collider[] hitResults;
    private readonly AssassinCircleStrikeSequence sequence = new AssassinCircleStrikeSequence();
    private readonly List<Unit> strikeLandedUnits = new List<Unit>();

    // 서버 — 확정 순간 고정한 원 중심(높이 = 플레이어 발)
    private Vector3 areaCenter;
    private bool hasInvulnerability;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Sub;
    public override bool CanMoveWhileActive => false;

    private AssassinCircleStrikeSkillData StrikeData => Data as AssassinCircleStrikeSkillData;

    public override void Initialize(Player owner, PlayerSkillController controller)
    {
        base.Initialize(owner, controller);
        assassinState = owner != null ? owner.GetComponent<AssassinState>() : null;
        invulnerability = owner != null ? owner.GetComponent<PlayerInvulnerability>() : null;
    }

    // 변신 중이고 종료 대기가 아닐 때만 — 종료 대기 중 새 공격은 시작하지 않는다(§10.2·§10.3).
    public override bool CanUse(Vector3 direction, Unit target) =>
        StrikeData != null && assassinState != null &&
        AssassinCircleStrikeRules.CanStart(assassinState.IsTransformed, assassinState.IsTransformEndPending);

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        State = SkillState.Active;

        AssassinCircleStrikeSkillData data = StrikeData;
        if (data == null)
        {
            Debug.LogError("[Assassin] 변신 E 에는 AssassinCircleStrikeSkillData 가 필요합니다.", this);
            EndSelf(SkillEndReason.Completed);
            return;
        }

        if (hitResults == null || hitResults.Length != data.MaxHitResults)
            hitResults = new Collider[data.MaxHitResults];

        // 확정 지점은 컨트롤러가 서버 위치 기준으로 재투영해 넘긴다(A4). 없으면 승인 방향 × 중심 거리.
        Vector3 origin = owner.transform.position;
        areaCenter = HasAimPoint ? AimPoint : origin + direction * data.CenterDistance;
        areaCenter.y = origin.y;

        sequence.Begin(data.HitCount);

        // 서버 전용. 정상/강제 종료 모두 OnEnd 가 해제한다. 만료 안전망은 지속시간.
        if (invulnerability != null && invulnerability.IsServer)
        {
            invulnerability.AddServerToken(InvulnerabilityCause.SkillAction, data.MaxActiveDuration);
            hasInvulnerability = true;
        }

        Edit.Log($"[Assassin/E변신] 공격 시작 — 중심 {areaCenter}, 반경 {data.AreaRadius:F1}m, {data.HitCount}타, 피해 {damageSnapshot}/타", this);
    }

    public override void OnClientPlay(Vector3 direction) { }

    public override void OnAnimationEvent(SkillAnimationEventType eventType)
    {
        if (eventType == SkillAnimationEventType.Hit)
        {
            if (sequence.TryBeginStrike())
                StrikeArea();
            return;
        }

        if (eventType != SkillAnimationEventType.End)
            return;

        // 클립 Hit 이벤트가 누락되거나 End 가 이르게 튜닝돼도 5타를 마저 친다(§9.2 "5타 완료 후 종료").
        while (sequence.TryBeginStrike())
            StrikeArea();

        EndSelf(SkillEndReason.Completed);
    }

    public override void OnEnd(SkillEndReason reason)
    {
        // 완료면 이미 다 쳤다. 사망·강제 중단이면 남은 타는 버린다(§12.2-3).
        sequence.Stop();
        RemoveInvulnerability();
        base.OnEnd(reason);
    }

    // 현재 원 안 대상을 다시 고른다 — 첫 타 대상을 이어 붙이지 않는다(§3.3). 벽·지형 차단 검사 없음(§9.2).
    private void StrikeArea()
    {
        AssassinCircleStrikeSkillData data = StrikeData;
        if (data == null || owner == null || hitResults == null)
            return;

        float radius = data.AreaRadius;
        Vector3 boxCenter = areaCenter + Vector3.up * (data.AreaHeight * 0.5f);
        Vector3 halfExtents = new Vector3(radius, data.AreaHeight * 0.5f, radius);
        int count = Physics.OverlapBoxNonAlloc(
            boxCenter, halfExtents, hitResults, Quaternion.identity,
            data.HittableLayers, QueryTriggerInteraction.Collide);

        strikeLandedUnits.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider hit = hitResults[i];
            if (hit == null || !AssassinCircleStrikeRules.OverlapsCircle(areaCenter, radius, hit.bounds))
                continue;

            Unit unit = ResolveHitUnit(hit, out Hurtbox hurtbox);

            // 상자처럼 Unit 이 아닌 대상은 Hurtbox 를 키로 쓴다(Q 와 같은 규칙).
            Object target = unit != null ? (Object)unit : hurtbox;
            if (target == null || unit == owner || !sequence.TryRegisterTarget(target))
                continue;

            AttackInfo attackInfo = new AttackInfo(damageSnapshot, DamageAttackType, hitPattern: DamageHitPattern);
            AttackHitContext hitContext = new AttackHitContext(owner.transform.position, owner.transform, hit, owner);

            bool resolved = hurtbox != null
                ? hurtbox.ReceiveAttack(attackInfo, hitContext)
                : unit.ReceiveAttack(attackInfo, hitContext);

            if (!resolved)
                continue;

            if (unit != null)
                strikeLandedUnits.Add(unit);

            Edit.Log($"[Assassin/E변신] {sequence.StrikesDone}타 적중 — {target.name} 피해 {attackInfo.damage}", this);
        }

        if (strikeLandedUnits.Count > 0)
            owner.RaiseServerAttackLanded(DamageAttackType, data.TriggersOnHit, strikeLandedUnits, this);
    }

    private void RemoveInvulnerability()
    {
        if (!hasInvulnerability)
            return;

        hasInvulnerability = false;
        if (invulnerability != null)
            invulnerability.RemoveServerToken(InvulnerabilityCause.SkillAction);
    }
}
