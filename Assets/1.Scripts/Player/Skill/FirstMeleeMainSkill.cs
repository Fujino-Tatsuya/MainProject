using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Q 진격의 방패 — 홀드 동안 방패를 들고 자동 전진하며 마우스 에임 쪽으로 틱당 고정 각도만큼 조향한다.
/// 서버는 스킬 틱마다 앵커 범위의 적에게 피해 + 진행 방향 넉백(견인)을 적용하고,
/// 홀드 동안 시전자에게 슈퍼아머를 부여한다 (Unit.Knockback 공통 검사로 CC 면역).
///
/// 권위 분담(networking.md): 전진·회전은 이동 권위(오너/오프라인)가 로컬 수행 — NetworkTransform이 위치를 복제한다.
/// 아마추어 회전은 동기화되지 않으므로 서버는 기존 에임 경로(OnAimUpdated)로 같은 조향을 시뮬레이션해
/// 판정 앵커 방향과 넉백 방향을 유지한다 (오차는 표시·판정 허용 범위, 쿨타임 미러와 같은 정책).
/// </summary>
public class FirstMeleeMainSkill : PlayerHoldSkill
{
    private PlayerMovement movement;
    private PlayerAimIndicator aimIndicator;
    private Collider[] hitResults;
    private readonly HashSet<Unit> tickTargets = new HashSet<Unit>();

    // 진행 방향 장부. 이동 권위 피어는 실제 이동에, 서버는 판정·넉백 방향에 사용한다.
    private Vector3 heading = Vector3.forward;
    private Vector3 serverAim = Vector3.forward;
    private bool isLocallySimulating;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Main;

    // 전진은 스킬이 직접 수행 — 기본 이동/회전은 잠근다
    public override bool CanMoveWhileActive => false;

    private FirstMeleeMainSkillData MainSkillData => Data as FirstMeleeMainSkillData;

    public override void Initialize(Player owner, PlayerSkillController controller)
    {
        base.Initialize(owner, controller);
        movement = owner.GetComponent<PlayerMovement>();
        aimIndicator = owner.GetComponent<PlayerAimIndicator>();
    }

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        base.OnServerStart(direction, target);

        FirstMeleeMainSkillData data = MainSkillData;
        if (data == null)
        {
            Debug.LogError("[Player] 진격의 방패에는 FirstMeleeMainSkillData가 필요합니다.", this);
            EndSelf(SkillEndReason.Completed);
            return;
        }

        heading = Flatten(direction);
        serverAim = heading;

        if (hitResults == null || hitResults.Length != data.MaxHitResults)
            hitResults = new Collider[data.MaxHitResults];

        // 홀드 동안 CC 면역. 만료 안전망은 지속시간, 정상/강제 종료 시엔 OnEnd가 즉시 해제한다.
        // (오프라인에서는 상태이상 시스템이 비활성 — 스탯 계열과 동일 제약)
        if (owner.StatusEffects != null)
            owner.StatusEffects.Apply(StatusEffectType.SuperArmor, Data.MaxActiveDuration, SourceId);
    }

    public override void OnClientPlay(Vector3 direction)
    {
        heading = Flatten(direction);
        // 이동 권위 피어(오너/오프라인)만 전진·조향을 실제 수행한다
        isLocallySimulating = owner != null && owner.IsMovementAuthority;
    }

    public override void OnAimUpdated(Vector3 direction)
    {
        serverAim = Flatten(direction);
    }

    public override void OnTick()
    {
        // 서버가 원격 오너의 플레이어를 대리 조향 — 판정 앵커(아마추어 하위)와 넉백 방향을 최신으로 유지
        if (State == SkillState.Charging && owner != null && !owner.IsMovementAuthority)
        {
            RotateHeadingToward(serverAim, Time.deltaTime);

            if (movement != null)
                movement.RotateImmediately(heading);
        }

        base.OnTick();
    }

    protected override void OnHoldTick()
    {
        FirstMeleeMainSkillData data = MainSkillData;
        if (data == null || HitboxAnchor == null || owner == null)
            return;

        int hitCount = OverlapHitboxAnchor(hitResults);
        tickTargets.Clear();

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = hitResults[i];
            if (hit == null)
                continue;

            Unit unit = ResolveHitUnit(hit, out Hurtbox hurtbox);
            if (unit == null || unit == owner || !tickTargets.Add(unit))
                continue;

            // 견인: 넉백/경직 값을 AttackInfo에 실어 전달 — 수신측(MonsterBase)이 지속넉백(Knockback 상태,
            // NavMesh 경계 클램프) → 종료 후 Stunned 경직 시퀀스를 처리한다. 슈퍼아머도 수신측이 거른다.
            // 방향 = heading(진행 방향) 명시 — 전 대상을 균일하게 앞으로 견인(다음 틱에도 범위 유지).
            // 방사형 폴백에 맡기면 전진(6m/s)이 넉백(3m/s)을 따라잡는 순간 옆/뒤로 뒤집힌다.
            // 견인 속도 하한 = 전진 속도: 넉백이 전진보다 느리면 플레이어가 몹을 추월해 히트박스에서
            // 놓친다("한두 번 밀리고 끝"). 하한을 코드로 보장해 돌진 끝까지 방패 앞에 붙어 밀려가게 한다.
            AttackInfo attackInfo = new AttackInfo(damageSnapshot, AttackType.Skill,
                knockbackStrength: Mathf.Max(data.KnockbackStrength, data.AdvanceSpeed),
                knockbackDuration: data.KnockbackDuration,
                staggerDuration: data.StaggerDuration,
                knockbackDirection: heading);
            AttackHitContext hitContext = new AttackHitContext(owner.transform.position, owner.transform, hit);

            bool resolved = hurtbox != null
                ? hurtbox.ReceiveAttack(attackInfo, hitContext)
                : unit.ReceiveAttack(attackInfo, hitContext);

            if (!resolved)
                continue;

            Edit.Log($"[Skill] 진격의 방패 틱 — {unit.name} 피해 {attackInfo.damage} + 견인", this);
        }
    }

    public override void OnEnd(SkillEndReason reason)
    {
        base.OnEnd(reason);

        isLocallySimulating = false;

        // 서버 전용 쓰기(CanWrite) 가드가 내장돼 있어 클라에서는 no-op
        if (owner != null && owner.StatusEffects != null)
            owner.StatusEffects.Remove(StatusEffectType.SuperArmor, SourceId);
    }

    private void Update()
    {
        if (!isLocallySimulating)
            return;

        FirstMeleeMainSkillData data = MainSkillData;
        if (data == null || movement == null)
            return;

        Vector3 aim = aimIndicator != null ? Flatten(aimIndicator.AimDirection) : heading;
        RotateHeadingToward(aim, Time.deltaTime);

        movement.RotateImmediately(heading);
        movement.MoveRoot(heading * (data.AdvanceSpeed * Time.deltaTime));
    }

    // 에임 방향으로 틱당 SteerAnglePerTick(도)만큼 조향 — 프레임에서는 시간 비례 분할 적용.
    // 좌/우 판별과 목표 도달 클램프는 RotateTowards가 처리한다.
    private void RotateHeadingToward(Vector3 targetDirection, float deltaTime)
    {
        FirstMeleeMainSkillData data = MainSkillData;
        if (data == null || targetDirection.sqrMagnitude < 0.001f)
            return;

        float interval = Data.TickInterval;
        float degreesPerSecond = interval > 0f ? data.SteerAnglePerTick / interval : data.SteerAnglePerTick;

        heading = Vector3.RotateTowards(
            heading,
            targetDirection,
            degreesPerSecond * Mathf.Deg2Rad * deltaTime,
            0f).normalized;
        heading.y = 0f;
    }

    private Vector3 Flatten(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude >= 0.001f ? direction.normalized : heading;
    }

    // 상태이상 출처 식별자 — 시전자 NetworkObjectId (오프라인 0, 어차피 상태이상 비활성)
    private ulong SourceId => owner != null && owner.NetworkObject != null ? owner.NetworkObjectId : 0UL;
}
