using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 우클릭 단죄의 방패 — 전방 방패 강타 1회. 서버가 앵커 범위에 인터럽트 히트를 넣는다
/// (<see cref="AttackInfo.isInterruptAttack"/>).
///
/// 이 스킬의 계약은 "이 히트는 인터럽트 스킬이다"를 실어 보내는 것까지다.
/// 카운터 창 여부·정면 각도·그로기/Break 전이는 <b>전부 맞는 쪽(보스) 책임</b>이다 — 여기서 판단하지 않는다.
///
/// 판정 타이밍은 두 경로를 모두 받되 <b>1회만</b> 발동한다:
/// 클립의 Hit 애니메이션 이벤트(정밀) 또는 <see cref="FirstMeleeInterruptSkillData.HitDelay"/> 타이머(폴백).
/// 클립은 아트/SVN 관할이라 이벤트 없이도 성립해야 한다.
/// </summary>
public class FirstMeleeInterruptSkill : PlayerInstantSkill
{
    // 🔴 여기는 RPC 가 필요 없다 — OnClientPlay 와 OnEnd 는 둘 다 전 피어에서 돈다:
    //    시작 → 서버는 TryStartSkillServer 가, 클라는 PlaySkillClientRpc 가 PlaySkillPresentation 을 탄다
    //    종료 → 서버는 EndActiveSkillServer 가, 클라는 EndSkillClientRpc 가 OnEnd 를 부른다
    //    (FirstMeleeMainSkill 의 smashLoop/trailLoop 와 같은 구조다)
    [Header("연출")]
    [Tooltip("방패 메쉬 문양 발광(머티리얼 페이드). 방패의 MaterialFadeEffect 를 물린다.\n" +
             "비워두면 연출만 빠진다")]
    [SerializeField] private MaterialFadeEffect shieldGlow;

    [Tooltip("강타 동안 재생할 루프 연출(소켓 이펙트). 프리팹의 'VFX/ShieldInterrupt/ShieldGlow' 를 물린다.\n" +
             "비워두면 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer glowLoop;

    // 🔴 적중 충격파만 창구가 다르다. ResolveHit 은 서버에서만 도는데(OnTick=TickServer,
    //    애니 이벤트=HandleAnimationEvent 의 IsServer 게이트) 이 클래스는 MonoBehaviour 라 RPC 를 못 단다.
    [Tooltip("적중 충격파 전파 창구. 플레이어 루트의 PlayerShieldVfx 를 물린다.\n비워두면 연출만 빠진다")]
    [SerializeField] private PlayerShieldVfx shieldVfx;

    private Collider[] hitResults;
    // 키가 Unit이 아니라 Object인 이유: 파괴 가능한 상자처럼 Unit이 아닌 IAttackReceiver도
    // 피격 대상이다. 그런 대상은 Hurtbox를 키로 쓴다(아래 히트 루프 참조).
    private readonly HashSet<Object> hitTargets = new HashSet<Object>();

    private float hitTime;
    private float endTime;
    private bool hasResolvedHit;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Interrupt;

    // 강타 중 이동 잠금 — 방향을 확정한 뒤 때린다
    public override bool CanMoveWhileActive => false;

    private FirstMeleeInterruptSkillData InterruptData => Data as FirstMeleeInterruptSkillData;

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        base.OnServerStart(direction, target);

        FirstMeleeInterruptSkillData data = InterruptData;
        if (data == null)
        {
            Debug.LogError("[Player] 단죄의 방패에는 FirstMeleeInterruptSkillData가 필요합니다.", this);
            EndSelf(SkillEndReason.Completed);
            return;
        }

        hasResolvedHit = false;
        hitTime = Time.time + data.HitDelay;
        endTime = Time.time + data.SkillDuration;

        if (hitResults == null || hitResults.Length != data.MaxHitResults)
            hitResults = new Collider[data.MaxHitResults];
    }

    public override void OnClientPlay(Vector3 direction)
    {
        // 애니메이션은 PlayerSkillController가 AnimatorStateName으로 재생한다. 여기는 방패 발광만.
        shieldGlow?.FadeIn();

        // Play 는 이미 재생 중이면 먼저 회수하므로 연타로 두 번 불려도 겹치지 않는다.
        glowLoop?.Play();
    }

    public override void OnEnd(SkillEndReason reason)
    {
        base.OnEnd(reason);

        // 🔴 종료 사유를 가리지 않는다. 취소·사망·안전망 만료 어느 쪽으로 끝나도 여기를 지나므로
        //    연출이 켜진 채 남는 경로가 없다. (둘 다 꺼져 있으면 조용한 no-op 이다)
        shieldGlow?.FadeOut();

        // 🔴 루프는 반드시 회수한다 — 핸들을 버리면 풀 인스턴스가 영영 안 돌아온다.
        //    소켓의 safetyTimeout(5s)은 이 경로가 실패했을 때를 위한 그물이지 정상 경로가 아니다.
        glowLoop?.Stop();
    }

    public override void OnTick()
    {
        if (!hasResolvedHit && Time.time >= hitTime)
            ResolveHit();

        // 종료 순서 주의: 같은 프레임에 둘 다 만족해도 판정이 먼저다
        if (Time.time >= endTime)
            EndSelf(SkillEndReason.Completed);
    }

    public override void OnAnimationEvent(SkillAnimationEventType eventType)
    {
        if (eventType == SkillAnimationEventType.Hit)
            ResolveHit();

        // End 이벤트 → EndSelf
        base.OnAnimationEvent(eventType);
    }

    // 서버 전용. 래치가 있어 애니 이벤트와 타이머가 겹쳐도 한 번만 들어간다.
    private void ResolveHit()
    {
        if (hasResolvedHit)
            return;

        hasResolvedHit = true;

        if (HitboxAnchor == null || owner == null)
        {
            Debug.LogError("[Player] 단죄의 방패에 판정 앵커(hitboxAnchor)가 배정되지 않았습니다.", this);
            return;
        }

        int hitCount = OverlapHitboxAnchor(hitResults);
        hitTargets.Clear();

        int resolvedCount = 0;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = hitResults[i];
            if (hit == null)
                continue;

            Unit unit = ResolveHitUnit(hit, out Hurtbox hurtbox);

            // 파괴 가능한 상자처럼 Unit이 아닌 대상은 unit이 null이고 hurtbox만 잡힌다.
            // unit으로 게이트하면 그런 대상이 통째로 걸러진다 — 중복 방지 키를 넓힌다.
            Object target = unit != null ? (Object)unit : hurtbox;
            if (target == null || unit == owner || !hitTargets.Add(target))
                continue;

            // isInterruptAttack = 보스가 카운터 판정에 쓰는 유일한 근거.
            // 소비 방식은 맞는 쪽이 정한다 — 몬스터는 누적→그로기, No.23은 카운터 창 판정.
            AttackInfo attackInfo = new AttackInfo(damageSnapshot, AttackType.Skill, isInterruptAttack: true);
            AttackHitContext hitContext =
                new AttackHitContext(owner.transform.position, owner.transform, hit, owner);

            bool resolved = hurtbox != null
                ? hurtbox.ReceiveAttack(attackInfo, hitContext)
                : unit.ReceiveAttack(attackInfo, hitContext);

            if (resolved)
            {
                resolvedCount++;

                // unit이 아니라 target을 찍는다 — Unit이 아닌 대상(상자 등)은 unit이 null이다.
                Edit.Log($"[Skill] 단죄의 방패 적중 — {target.name} 피해 {attackInfo.damage} (Interrupt)", this);
            }
        }

        // 하나라도 맞았을 때만 충격파. 허공 스윙은 조용히 지나간다.
        // 대상 수와 무관하게 한 번만 터뜨린다 — 여럿 맞혔다고 겹쳐 재생하면 밝기만 배로 튄다.
        if (resolvedCount > 0)
            shieldVfx?.ServerInterruptWave();
    }
}
