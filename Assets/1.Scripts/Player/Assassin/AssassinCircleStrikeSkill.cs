using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 어쌔신 변신 E 원형 5타 — 변신 중 Sub 슬롯 대체 스킬(alternateSkills[Sub], character_assassin.md §9, PLAN-assassin A9).
///
/// 조준 = 공용 GroundPoint + 고정 거리(A4): 원 중심이 플레이어에서 castRange(1.5m) 떨어져 마우스 방향으로 돈다, 효과 반경 = 데이터 hitArea 원 radius(2m).
/// 판정 = 판정 도형(할 일 153) — 기준점 = 원 중심(지면 조준점, 발 높이), <see cref="AttackShapeQuery.Collect"/>, 칼날의 영역 = 반경 배율 복사본.
/// 조준 중 이동 가능·무적 없음·쿨 없음. 조준 중 변신이 끝나면(만료·R 해제) <see cref="AssassinState"/> 가 오너 조준을 취소한다(한 곳).
///
/// 확정(좌클릭) = 서버 승인 = 공격 시작: 쿨 12초 시작(자동 커밋), 플레이어 위치·원 중심(서버 재투영 지점)을 고정한다.
/// 이동·순간이동 없음 — Skill_02_Move_000pct 의 모델 움직임은 연출이다(루트모션 꺼짐).
/// 클립 Hit 이벤트마다 서버가 원 안 대상을 다시 고른다(지형·벽 차단 검사 없음, 상자 포함 일반 피해 경로, 같은 타에서 대상 1회).
/// 공격 시작~종료 무적(<see cref="InvulnerabilityCause.SkillAction"/>), 강제 중단·사망이면 OnEnd 가 즉시 해제하고 남은 타를 버린다.
/// 공격 중 이동·대시·다른 스킬 불가(스킬 상태), 변신 만료·R 해제는 FSM 이 Skill 을 벗어난 뒤 AssassinState 가 처리한다(5타 완료 후 종료).
/// 변신 중이라 스택은 없다. 백어택 배율은 A11.
/// </summary>
public sealed class AssassinCircleStrikeSkill : PlayerSkillBase, ISkillGroundHitPreview
{
    private AssassinState assassinState;
    private PlayerInvulnerability invulnerability;
    private AssassinSkillView view;
    private Collider[] hitResults;
    private readonly AssassinCircleStrikeSequence sequence = new AssassinCircleStrikeSequence();
    private readonly List<Unit> strikeLandedUnits = new List<Unit>();
    // 도형 질의(할 일 153) — 질의 하나 안의 1회 키. 타마다 1회는 sequence 가 맡는다.
    private readonly HashSet<Object> strikeSeen = new HashSet<Object>();
    private readonly List<AttackShapeHit> shapeHits = new List<AttackShapeHit>();
    private System.Func<Collider, Object> resolveTarget;

    // 서버 — 확정 순간 고정한 판정 영역(데이터 hitArea × 칼날의 영역 반경 배율)·기준 포즈(원 중심, 높이 = 플레이어 발). 5타 모두 이 값.
    private AttackShapeSet serverArea;
    private AttackShapePose serverPose;
    private bool hasInvulnerability;

    private static readonly SkillAnimationEventType[] RequiredEvents =
        { SkillAnimationEventType.Hit, SkillAnimationEventType.End };

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Sub;
    public override bool CanMoveWhileActive => false;

    // 좌클릭 홀드 중 변신 E — 5타가 끝난 뒤 아직 누르고 있으면 기본 공격을 잇는다(할 일 146, 어쌔신 일반 E 와 같은 규칙).
    public override bool ResumesBasicAttackOnHold => true;

    public override IReadOnlyList<SkillAnimationEventType> RequiredAnimationEvents => RequiredEvents;

    private AssassinCircleStrikeSkillData StrikeData => Data as AssassinCircleStrikeSkillData;

    public override void Initialize(Player owner, PlayerSkillController controller)
    {
        base.Initialize(owner, controller);
        assassinState = owner != null ? owner.GetComponent<AssassinState>() : null;
        invulnerability = owner != null ? owner.GetComponent<PlayerInvulnerability>() : null;
        view = owner != null ? owner.GetComponent<AssassinSkillView>() : null;
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
        Vector3 aimPoint = HasAimPoint ? AimPoint : origin + direction * data.CenterDistance;
        Vector3 areaCenter = AreaCenter(origin, aimPoint);
        float radiusMultiplier = RadiusMultiplier();
        serverArea = ResolveStrikeArea(data, origin, aimPoint, radiusMultiplier, out serverPose);

        sequence.Begin(data.HitCount);

        // 서버 전용. 정상/강제 종료 모두 OnEnd 가 해제한다. 만료 안전망은 지속시간.
        if (invulnerability != null && invulnerability.IsServer)
        {
            invulnerability.AddServerToken(InvulnerabilityCause.SkillAction, data.MaxActiveDuration);
            hasInvulnerability = true;
        }

        Edit.Log($"[Assassin/E변신] 공격 시작 — 중심 {areaCenter}, 반경 {data.AreaRadius * radiusMultiplier:F2}m, {data.HitCount}타, 피해 {damageSnapshot}/타", this);
    }

    // 전 피어 — 원 루프 연출(Multi Slashes). 클라는 서버 재투영 지점을 모르므로 승인 방향 × 중심 거리로 근사한다
    // (같은 1.5m 고정 거리 — 시전자 화면에서는 바닥 원 표시 중심과 같다). 배율 = 원 반경.
    public override void OnClientPlay(Vector3 direction)
    {
        AssassinCircleStrikeSkillData data = StrikeData;
        if (view == null || data == null || owner == null)
            return;

        direction.y = 0f;
        Vector3 forward = direction.sqrMagnitude > 0.001f ? direction.normalized : owner.transform.forward;
        view.BeginCircleStrike(owner.transform.position + forward * data.CenterDistance, data.AreaRadius, data.MaxActiveDuration);
    }

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
        view?.EndCircleStrike();   // 전 피어 — 완료·중단 모두 같은 사라짐
        base.OnEnd(reason);
    }

    // 현재 원 안 대상을 다시 고른다 — 첫 타 대상을 이어 붙이지 않는다(§3.3). 벽·지형 차단 검사 없음(§9.2).
    private void StrikeArea()
    {
        AssassinCircleStrikeSkillData data = StrikeData;
        if (data == null || owner == null || hitResults == null || serverArea == null)
            return;

        strikeSeen.Clear();
        shapeHits.Clear();
        // 키 = Unit, Unit 이 아닌 대상(상자)은 Hurtbox(Q 와 같은 규칙). 시전자 자신은 null.
        resolveTarget ??= ResolveShapeTarget;
        AttackShapeQuery.Collect(serverArea, serverPose, data.HittableLayers, QueryTriggerInteraction.Collide, hitResults, resolveTarget, strikeSeen, shapeHits);

        strikeLandedUnits.Clear();
        for (int i = 0; i < shapeHits.Count; i++)
        {
            Collider hit = shapeHits[i].Collider;
            Object target = shapeHits[i].Target;
            Unit unit = ResolveHitUnit(hit, out Hurtbox hurtbox);

            if (!sequence.TryRegisterTarget(target))
                continue;

            AttackInfo attackInfo = new AttackInfo(damageSnapshot, DamageAttackType, hitPattern: DamageHitPattern);
            if (assassinState != null)
                attackInfo = assassinState.WithBackAttack(attackInfo);
            AttackHitContext hitContext = new AttackHitContext(owner.transform.position, owner.transform, hit, owner);
            bool backAttack = AssassinHitTargets.IsBackAttack(unit, attackInfo, hitContext);

            bool resolved = hurtbox != null
                ? hurtbox.ReceiveAttack(attackInfo, hitContext)
                : unit.ReceiveAttack(attackInfo, hitContext);

            if (!resolved)
                continue;

            if (unit != null)
                strikeLandedUnits.Add(unit);
            if (backAttack)
                AssassinHitTargets.NotifyBackAttackHit(owner, hit, unit, hitContext);

            Edit.Log($"[Assassin/E변신] {sequence.StrikesDone}타 적중 — {target.name} 피해 {attackInfo.damage}{(backAttack ? " (백어택)" : "")}", this);
        }

        if (strikeLandedUnits.Count > 0)
            owner.RaiseServerAttackLanded(DamageAttackType, data.TriggersOnHit, strikeLandedUnits, this);
    }

    /// <summary>[오너 로컬] 조준 지점 원이 떠 있는 동안 — 그 지점에 시전하면 칠 영역. 서버 타격(<see cref="StrikeArea"/>)과 같은 식.</summary>
    public bool TryGetGroundHitVolume(Vector3 casterPosition, Vector3 aimPoint, out SkillHitVolume volume)
    {
        AssassinCircleStrikeSkillData data = StrikeData;
        if (data == null)
        {
            volume = default;
            return false;
        }

        volume = PreviewVolume(data, casterPosition, AreaCenter(casterPosition, aimPoint), RadiusMultiplier());
        return true;
    }

    // 칼날의 영역 반경 배율(서버 확정·오너 미리보기·특성 랩 기즈모 공용 — 특성 수치는 전 피어 같은 값). 원 베기 VFX 크기는 데이터 반경 그대로.
    public float RadiusMultiplier() =>
        AssassinTraitAreaRules.ScaleRadius(owner != null ? owner.TraitModifiers : null, AssassinTraitAreaRules.CircleStrike, 1f);

    /// <summary>
    /// 타격 판정 영역·기준 포즈(할 일 153) — 데이터 hitArea × 칼날의 영역 반경 배율, 기준점 = 원 중심(<see cref="AreaCenter"/> — 조준 지점 + 발 높이).
    /// 서버 확정(<see cref="OnServerStart"/>)과 특성 랩 공격 범위 기즈모(할 일 153 6단계)가 같이 쓴다.
    /// </summary>
    public static AttackShapeSet ResolveStrikeArea(AssassinCircleStrikeSkillData data, Vector3 casterPosition, Vector3 aimPoint, float radiusMultiplier,
        out AttackShapePose pose)
    {
        pose = data.StrikePose(casterPosition, AreaCenter(casterPosition, aimPoint));
        return data.StrikeArea(radiusMultiplier);
    }

    /// <summary>원 중심 = 시전 지점의 수평 위치 + 시전자 발 높이(서버 확정·오너 미리보기 공용).</summary>
    public static Vector3 AreaCenter(Vector3 casterPosition, Vector3 point)
    {
        point.y = casterPosition.y;
        return point;
    }

    /// <summary>
    /// 오너 미리보기 영역(스킬 범위 외곽선, 할 일 58) — 서버 타격과 같은 판정 도형(<see cref="AssassinCircleStrikeSkillData.StrikeArea"/>)·포즈의 첫 도형.
    /// 원이면 그 원기둥(도형 높이 범위), 아니면 넓은 질의 상자. 외곽선 후보 거름은 콜라이더 AABB(서버 판정 점보다 약간 넓다).
    /// </summary>
    public static SkillHitVolume PreviewVolume(AssassinCircleStrikeSkillData data, Vector3 casterPosition, Vector3 areaCenter, float radiusMultiplier)
    {
        AttackShapeSet area = data.StrikeArea(radiusMultiplier);
        AttackShape shape = area.IsEmpty ? null : area.Shapes[0];
        AttackShapePose pose = data.StrikePose(casterPosition, areaCenter);
        if (shape is AttackCircleShape circle)
        {
            Vector3 center = circle.ShapePose(pose).Position + Vector3.up * circle.MinHeight;
            return SkillHitVolume.CircleColumn(center, circle.Radius, circle.MaxHeight - circle.MinHeight, data.HittableLayers);
        }

        if (shape == null)
            return SkillHitVolume.Box(areaCenter, Vector3.zero, Quaternion.identity, data.HittableLayers);

        shape.GetBroadBox(pose, out Vector3 boxCenter, out Vector3 halfExtents, out Quaternion rotation);
        return SkillHitVolume.Box(boxCenter, halfExtents, rotation, data.HittableLayers);
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
