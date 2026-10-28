using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Q 진격의 방패 — 홀드 동안 방패를 들고 자동 전진하며 마우스 에임 쪽으로 틱당 고정 각도만큼 조향한다.
/// 서버는 스킬 틱마다 앵커 범위의 적에게 피해 + 진행 방향 넉백(견인)을 적용하고,
/// 홀드 동안 시전자에게 슈퍼아머를 부여한다 (Unit.Knockback 공통 검사로 CC 면역).
///
/// 권위 분담(networking.md): 전진·회전은 시뮬레이션 피어가 수행하고 서버 결과를 NetworkTransform이 복제한다.
/// 서버는 원격 오너의 기존 에임 경로(OnAimUpdated)로 같은 조향을 시뮬레이션해 판정 앵커 방향과
/// 넉백 방향을 유지한다 (오차는 표시·판정 허용 범위, 쿨타임 미러와 같은 정책).
/// </summary>
public class FirstMeleeMainSkill : PlayerHoldSkill
{
    // 홀드 동안 이어지는 루프 연출(방패 스매시). 켜고 끄는 지점이 OnClientPlay / OnEnd 다.
    //
    // 🔴 여기는 **RPC 가 필요 없다** — 이 프로젝트의 다른 연출과 다른 점이다.
    //    OnClientPlay 와 OnEnd 는 둘 다 전 피어에서 돈다:
    //      시작 → 서버는 TryStartSkillServer 가, 클라는 PlaySkillClientRpc 가 PlaySkillPresentation 을 탄다
    //      종료 → 서버는 EndActiveSkillServer 가, 클라는 EndSkillClientRpc 가 OnEnd 를 부른다
    //    서버 전용 스코프에서 재생해 호스트에만 보이던 사고(보스 연출 선례)가 여기선 구조적으로 안 난다.
    [Header("연출")]
    [Tooltip("홀드 동안 재생할 루프 연출. 프리팹의 'Smash' EffectSocketPlayer 를 물린다.\n" +
             "비워두면 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer smashLoop;

    [Tooltip("홀드 동안 재생할 궤적 루프. 프리팹의 'Trail' EffectSocketPlayer 를 물린다.\n" +
             "비워두면 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer trailLoop;

    private PlayerMovement movement;
    private PlayerMotor motor;
    private PlayerAimIndicator aimIndicator;
    private Collider[] hitResults;
    // 키가 Unit이 아니라 Object인 이유: 파괴 가능한 상자처럼 Unit이 아닌 IAttackReceiver도
    // 피격 대상이다. 그런 대상은 Hurtbox를 키로 쓴다(아래 히트 루프 참조).
    private readonly HashSet<Object> tickTargets = new HashSet<Object>();
    // 이번 틱에 맞은 Unit (Player.ServerAttackLanded 통지용). Unit 이 아닌 대상(상자 등)은 빠진다.
    private readonly List<Unit> tickLandedUnits = new List<Unit>();
    // 제압의 진격(할 일 126) — 이번 시전에서 뇌진탕을 받은 대상을 지속 밀치기 동안 이어 싣는다(서버).
    private readonly PaladinQConcussionCarry traitConcussionCarry = new PaladinQConcussionCarry();
    // 진격의 충격(할 일 126) — Q 종료 지점 파생 판정(서버). 원래 Q 틱 버퍼와 따로 둔다.
    private readonly List<TraitDerivedAttack> traitDerivedAttacks = new List<TraitDerivedAttack>();
    private readonly Collider[] traitBlastResults = new Collider[16];
    private readonly HashSet<Object> traitBlastTargets = new HashSet<Object>();

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
        motor = owner.GetComponent<PlayerMotor>();
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
        // 전용 서버는 Client 재생 콜백을 받지 않으므로 권위 시뮬레이션을 서버 시작 경로에서 켠다.
        isLocallySimulating = owner != null && owner.IsMotionAuthority;

        if (hitResults == null || hitResults.Length != data.MaxHitResults)
            hitResults = new Collider[data.MaxHitResults];
        traitConcussionCarry.Reset();

        // 홀드 동안 CC 면역. 만료 안전망은 지속시간, 정상/강제 종료 시엔 OnEnd가 즉시 해제한다.
        // (오프라인에서는 상태이상 시스템이 비활성 — 스탯 계열과 동일 제약)
        if (owner.StatusEffects != null)
            owner.StatusEffects.Apply(StatusEffectType.SuperArmor, Data.MaxActiveDuration, SourceId);
    }

    public override void OnClientPlay(Vector3 direction)
    {
        heading = Flatten(direction);
        // Motor를 돌리는 오너/서버/오프라인만 전진·조향 채널을 제출한다.
        isLocallySimulating = owner != null && owner.IsSimulating;

        // 연출은 권위와 무관하다 — 모든 피어가 각자 켠다.
        // Play 는 이미 재생 중이면 먼저 회수하므로 두 번 불려도 겹치지 않는다.
        smashLoop?.Play();
        trailLoop?.Play();
    }

    public override void OnAimUpdated(Vector3 direction)
    {
        serverAim = Flatten(direction);
    }

    public override void OnTick()
    {
        // 서버가 원격 오너의 플레이어를 대리 조향 — 판정 앵커(아마추어 하위)와 넉백 방향을 최신으로 유지
        if (State == SkillState.Charging && owner != null &&
            owner.IsMotionAuthority && !owner.IsInputSource)
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
        tickLandedUnits.Clear();
        bool onHitBonusTaken = false;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = hitResults[i];
            if (hit == null)
                continue;

            Unit unit = ResolveHitUnit(hit, out Hurtbox hurtbox);

            // 파괴 가능한 상자처럼 Unit이 아닌 대상은 unit이 null이고 hurtbox만 잡힌다.
            // unit으로 게이트하면 그런 대상이 통째로 걸러진다 — 중복 방지 키를 넓힌다.
            Object target = unit != null ? (Object)unit : hurtbox;
            if (target == null || unit == owner || !tickTargets.Add(target))
                continue;

            // 견인: 넉백/경직 값을 AttackInfo에 실어 전달 — 수신측(MonsterBase)이 지속넉백(Knockback 상태,
            // NavMesh 경계 클램프) → 종료 후 Stunned 경직 시퀀스를 처리한다. 슈퍼아머도 수신측이 거른다.
            // 방향 = heading(진행 방향) 명시 — 전 대상을 균일하게 앞으로 견인(다음 틱에도 범위 유지).
            // 방사형 폴백에 맡기면 전진(6m/s)이 넉백(3m/s)을 따라잡는 순간 옆/뒤로 뒤집힌다.
            // 견인 속도 하한 = 전진 속도: 넉백이 전진보다 느리면 플레이어가 몹을 추월해 히트박스에서
            // 놓친다("한두 번 밀리고 끝"). 하한을 코드로 보장해 돌진 끝까지 방패 앞에 붙어 밀려가게 한다.
            int resolvedDamage = damageSnapshot;
            // 시체(공격 거절)는 첫 대상 자리를 차지하지 않는다 — 뒤의 살아 있는 첫 Unit 이 보너스를 받는다.
            if (!onHitBonusTaken && unit != null && unit.CurrentHealth > 0)
            {
                onHitBonusTaken = true;
                int bonus = owner.ServerTakeOnHitBonus(data.TriggersOnHit, unit);
                resolvedDamage = bonus >= int.MaxValue - resolvedDamage
                    ? int.MaxValue
                    : resolvedDamage + bonus;
            }

            AttackInfo attackInfo = new AttackInfo(resolvedDamage, DamageAttackType,
                knockbackStrength: Mathf.Max(data.KnockbackStrength, data.AdvanceSpeed),
                knockbackDuration: data.KnockbackDuration,
                staggerDuration: data.StaggerDuration,
                knockbackDirection: heading,
                hitPattern: DamageHitPattern);
            // 특성 밀치기·뇌진탕(할 일 126 — 제압의 진격). Unit 이 아닌 대상(상자 등)은 특성 CC 없음(명세 §4).
            if (unit != null)
                ApplyTraitCrowdControlServer(unit, heading, ref attackInfo);
            AttackHitContext hitContext = new AttackHitContext(owner.transform.position, owner.transform, hit, owner);

            bool resolved = hurtbox != null
                ? hurtbox.ReceiveAttack(attackInfo, hitContext)
                : unit.ReceiveAttack(attackInfo, hitContext);

            if (!resolved)
                continue;

            if (unit != null)
                tickLandedUnits.Add(unit);

            // unit이 아니라 target을 찍는다 — Unit이 아닌 대상(상자 등)은 unit이 null이다.
            Edit.Log($"[Skill] 진격의 방패 틱 — {target.name} 피해 {attackInfo.damage} + 견인", this);
        }

        traitConcussionCarry.EndTick();
        owner.RaiseServerAttackLanded(DamageAttackType, data.TriggersOnHit, tickLandedUnits, this);
    }

    /// <summary>
    /// [서버] 특성 밀치기·뇌진탕 연결 한 곳(할 일 126) — 원래 Q 판정 1건이 대상에 들어가기 직전. 돌파의 방패(4/4) 단발 넉백도 이걸 부른다.
    /// 공용 수정자(<see cref="PlayerTraitEffects.ModifyOutgoingAttackServer"/>)가 시전당 대상별 1회로 밀치기·뇌진탕을 싣고,
    /// 뇌진탕은 지속 밀치기 동안 매 틱 이어 싣는다(<see cref="PaladinQConcussionCarry"/>). 보스 상태는 묻지 않는다 — 수신측 거부 규칙에 맡긴다.
    /// </summary>
    internal void ApplyTraitCrowdControlServer(Unit unit, Vector3 direction, ref AttackInfo info)
    {
        if (unit == null || owner == null)
            return;

        TraitAttackKey key = TraitAttackKey.ForSkill(Slot, controller != null && controller.IsAlternateSkill(this));
        PlayerTraitEffects.For(owner).ModifyOutgoingAttackServer(key, ServerCastId, unit, direction, ref info);
        info.concussionDuration = traitConcussionCarry.Resolve(unit.GetInstanceID(), info.concussionDuration);
    }

    public override void OnEnd(SkillEndReason reason)
    {
        base.OnEnd(reason);

        isLocallySimulating = false;
        traitConcussionCarry.Reset();

        // 진격의 충격 — 정상 종료·홀드 해제·최대 유지 시간 도달이면 종료 순간 위치·방향으로 1회 예약. 사망·강제 중단·대시 끊김은 없음.
        if (owner != null && owner.IsServer && PaladinTraitRules.QBlastFires(reason, owner.CurrentHealth))
            ScheduleTraitBlastServer();

        // 🔴 종료 사유를 가리지 않는다. 취소·사망·안전망 만료 어느 쪽으로 끝나도 여기를 지나므로
        //    루프가 남는 경로가 없다. (Stop 은 재생 중이 아니면 조용한 no-op 이다)
        smashLoop?.Stop();
        trailLoop?.Stop();

        // 서버 전용 쓰기(CanWrite) 가드가 내장돼 있어 클라에서는 no-op
        if (owner != null && owner.StatusEffects != null)
            owner.StatusEffects.Remove(StatusEffectType.SuperArmor, SourceId);
    }

    // ── 진격의 충격(PAL_4_Q_BLAST, 할 일 126) ──

    /// <summary>
    /// [서버] Q 가 끝난 순간 — 파생 공격(⑧) 명세 중 진격의 충격만 골라 전방 forwardOffset 지점·반경 radius 판정을 delay 뒤 1회 예약한다.
    /// 피해 = DQpacket(<see cref="PaladinTraitRules.QPacketDamage"/> — 이 시전의 틱 Dref 스냅샷 × 예정 틱 수) × 파생 배율. 실제 맞힌 틱·적 수와 무관하다.
    /// </summary>
    private void ScheduleTraitBlastServer()
    {
        if (Data == null || !isActiveAndEnabled)
            return;

        TraitAttackKey key = TraitAttackKey.ForSkill(Slot, controller != null && controller.IsAlternateSkill(this));
        PlayerTraitEffects.For(owner).GetDerivedAttacks(key, traitDerivedAttacks);
        int packet = PaladinTraitRules.QPacketDamage(damageSnapshot, Data.MaxActiveDuration, Data.TickInterval);
        // 플레이어는 씬을 넘어 살아 있으므로 '씬 전환 취소'는 활성 씬으로 가른다.
        int sceneHandle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;

        for (int i = 0; i < traitDerivedAttacks.Count; i++)
        {
            TraitDerivedAttack attack = traitDerivedAttacks[i];
            if (attack.Key != PaladinTraitValues.QBlastKey)
                continue;

            if (attack.Radius <= 0f)
            {
                Debug.LogWarning("[Skill] 진격의 충격 — 반경이 0 이라 판정하지 않습니다(특성 데이터 확인).", this);
                continue;
            }

            Vector3 center = owner.transform.position + heading * attack.ForwardOffset;
            StartCoroutine(RunTraitBlastServer(center, attack.Radius, attack.DamageFrom(packet), attack.Delay, sceneHandle));
        }

        traitDerivedAttacks.Clear();
    }

    /// <summary>
    /// [서버] 파생 판정 1회 — 대상별 1회 피해만(<see cref="TraitHitFlags.Derived"/>: 특성·자원·CC·간파 없음).
    /// 그래서 패시브 추가피해(ServerTakeOnHitBonus)·밀치기 수정자·<see cref="Player.RaiseServerAttackLanded"/> 를 부르지 않는다.
    /// 지형 차단은 원래 Q 판정처럼 없다(같은 레이어 Overlap). 지연 동안 시전자가 죽거나 씬이 바뀌면 취소한다. 이펙트 없음(판정만).
    /// </summary>
    private System.Collections.IEnumerator RunTraitBlastServer(Vector3 center, float radius, int damage, float delay, int sceneHandle)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        if (owner == null || !owner.IsServer || owner.CurrentHealth <= 0 || UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle != sceneHandle || Data == null)
            yield break;

        int hitCount = Physics.OverlapSphereNonAlloc(center, radius, traitBlastResults, Data.HittableLayers, QueryTriggerInteraction.Collide);
        traitBlastTargets.Clear();
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = traitBlastResults[i];
            if (hit == null)
                continue;

            Unit unit = ResolveHitUnit(hit, out Hurtbox hurtbox);
            Object target = unit != null ? (Object)unit : hurtbox;
            if (target == null || unit == owner || !traitBlastTargets.Add(target))
                continue;

            AttackInfo attackInfo = new AttackInfo(damage, DamageAttackType, isInterruptAttack: false, hitPattern: DamageHitPattern);
            AttackHitContext hitContext = new AttackHitContext(owner.transform.position, owner.transform, hit, owner);
            bool resolved = hurtbox != null
                ? hurtbox.ReceiveAttack(attackInfo, hitContext)
                : unit.ReceiveAttack(attackInfo, hitContext);
            if (resolved)
                Edit.Log($"[Skill] 진격의 충격 — {target.name} 추가 피해 {damage}", this);
        }

        traitBlastTargets.Clear();
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
    }

    private void FixedUpdate()
    {
        if (!isLocallySimulating)
            return;

        FirstMeleeMainSkillData data = MainSkillData;
        if (data == null || motor == null)
            return;

        motor.AddDisplacement(heading * (data.AdvanceSpeed * Time.fixedDeltaTime));
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
