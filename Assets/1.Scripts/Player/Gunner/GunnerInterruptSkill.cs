using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 거너 우클릭 — 근접 간파 (character_gunner.md §8, D10·D11, PLAN-gunner.md G6).
///
/// 레이저포를 내질러 총구가 닿는 순간(HitDelay 또는 Hit 애니 이벤트, 1회) 앵커 범위에 피해 + <b>간파 공격 표시</b>를 싣는다.
/// 간파 성공 조건(유효 구간·전방 120도)·취약 넉백·벽/증기 벤트는 <b>맞는 쪽(보스)</b>이 판단한다 — 가붕이 우클릭과 같은 계약.
/// 그 순간부터 적중·간파 여부와 무관하게 후폭풍으로 공격 반대 방향으로 밀려난다 — 벽·오브젝트에만 막히고 유닛은 통과, 무적 없음.
/// 쿨타임은 사용 시점(빗나가도 적용 — §8.6). 과열도와 무관.
/// </summary>
public class GunnerInterruptSkill : PlayerInstantSkill, ISkillPreviewSource
{
    private PlayerMotor motor;
    private GunnerBeamView view;
    private Collider[] hitResults;
    private readonly HashSet<Object> hitTargets = new HashSet<Object>();
    private readonly List<Unit> landedUnits = new List<Unit>();

    // 서버
    private float hitTime;
    private float endTime;
    private bool hasResolvedHit;

    // 시뮬레이션 피어(오너 + 서버 권위 시 서버)
    private Vector3 attackDirection;
    private float recoilStartTime;
    private float recoilRemaining;
    private float recoilSpeed;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Interrupt;
    public override bool CanMoveWhileActive => false;

    private GunnerInterruptData IData => Data as GunnerInterruptData;

    public override void Initialize(Player owner, PlayerSkillController controller)
    {
        base.Initialize(owner, controller);
        motor = owner.GetComponent<PlayerMotor>();
        view = owner.GetComponent<GunnerBeamView>();
    }

    public override bool CanUse(Vector3 direction, Unit target) => IData != null && HitboxAnchor != null;

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        base.OnServerStart(direction, target);

        GunnerInterruptData data = IData;
        hasResolvedHit = false;
        hitTime = Time.time + data.HitDelay;
        endTime = Time.time + data.SkillDuration;
        if (hitResults == null || hitResults.Length != data.MaxHitResults)
            hitResults = new Collider[data.MaxHitResults];
    }

    // 전 피어. 후폭풍은 판정 시각(시작 + HitDelay)에 각 시뮬레이션 피어가 스스로 시작한다(RPC 없이 같은 시각 규칙).
    public override void OnClientPlay(Vector3 direction)
    {
        GunnerInterruptData data = IData;
        direction.y = 0f;
        attackDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : owner.transform.forward;
        recoilStartTime = Time.time + (data != null ? data.HitDelay : 0f);
        recoilRemaining = data != null ? data.RecoilDistance : 0f;
        recoilSpeed = data != null ? data.RecoilDistance / data.RecoilDuration : 0f;
    }

    public override void OnFixedTick()
    {
        if (recoilRemaining <= 0f || motor == null || !owner.IsSimulating || Time.time < recoilStartTime)
            return;

        motor.PassThroughEnemiesOverride = true; // 유닛 통과(§8.5 — 벽·오브젝트에만 막힘)
        float step = Mathf.Min(recoilSpeed * Time.fixedDeltaTime, recoilRemaining);
        recoilRemaining -= step;
        motor.AddGroundedDisplacement(-attackDirection * step);
    }

    public override void OnTick()
    {
        if (!hasResolvedHit && Time.time >= hitTime)
            ResolveHit();

        if (Time.time >= endTime)
            EndSelf(SkillEndReason.Completed);
    }

    public override void OnAnimationEvent(SkillAnimationEventType eventType)
    {
        if (eventType == SkillAnimationEventType.Hit)
            ResolveHit();
        base.OnAnimationEvent(eventType);
    }

    public override void OnEnd(SkillEndReason reason)
    {
        recoilRemaining = 0f;
        if (motor != null)
            motor.PassThroughEnemiesOverride = false;
        base.OnEnd(reason);
    }

    public bool TryGetPreview(Vector3 origin, Vector3 forward, out SkillPreviewShape shape)
    {
        GunnerInterruptData data = IData;
        if (data == null || HitboxAnchor == null ||
            !HitboxAnchor.TryGetLocalBox(out Vector3 center, out Vector3 size))
        {
            shape = default;
            return false;
        }

        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
        float clipped = motor != null
            ? motor.GetGroundedPreviewDistance(
                -forward * data.RecoilDistance,
                blockOtherPlayers: false,
                passThroughEnemies: true)
            : data.RecoilDistance;
        shape = SkillPreviewShapes.HitboxWithArrow(
            center,
            size,
            arrowDirection: -1f,
            arrowLength: data.RecoilDistance,
            clippedArrowLength: clipped);
        return true;
    }

    // 서버 전용. 애니 이벤트와 타이머가 겹쳐도 한 번만.
    private void ResolveHit()
    {
        if (hasResolvedHit)
            return;
        hasResolvedHit = true;

        int hitCount = OverlapHitboxAnchor(hitResults);
        hitTargets.Clear();
        landedUnits.Clear();
        int resolvedCount = 0;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = hitResults[i];
            if (hit == null)
                continue;

            Unit unit = ResolveHitUnit(hit, out Hurtbox hurtbox);
            Object key = unit != null ? (Object)unit : hurtbox;
            if (key == null || unit == owner || (unit != null && unit.CurrentHealth <= 0) || !hitTargets.Add(key))
                continue;

            int damage = damageSnapshot;
            if (unit != null)
            {
                int bonus = owner.ServerTakeOnHitBonus(Data.TriggersOnHit, unit);
                damage = bonus >= int.MaxValue - damage ? int.MaxValue : damage + bonus;
            }

            // isInterruptAttack = 보스가 간파 판정에 쓰는 유일한 근거(D11)
            var attackInfo = new AttackInfo(damage, DamageAttackType,
                isInterruptAttack: true, hitPattern: DamageHitPattern);
            var context = new AttackHitContext(owner.transform.position, owner.transform, hit, owner);
            bool resolved = hurtbox != null ? hurtbox.ReceiveAttack(attackInfo, context) : unit.ReceiveAttack(attackInfo, context);
            if (!resolved)
                continue;

            resolvedCount++;
            if (unit != null)
                landedUnits.Add(unit);
        }

        owner.RaiseServerAttackLanded(DamageAttackType, Data.TriggersOnHit, landedUnits, this);

        // 레이저·폭발 연출(임시) — 빗나가도 나간다(§8.6)
        Vector3 origin = owner.transform.position + Vector3.up;
        view?.ServerInterruptBlast(origin, origin + attackDirection * 1.5f);

        Edit.Log($"[Gunner/우클릭] 간파 판정 — 적중 {resolvedCount}", this);
    }
}
