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

    // 돌파의 방패(PAL_2_DASH, 할 일 126 4/4) — Q 모드 변경. 특성이 있으면 홀드·지속 타격·지속 밀치기·조향 대신
    // 입력 순간 방향으로 준비 → 고정 거리 돌진 → 회수. 서버는 경로 장부로 대상당 피해 1회 + 넉백 1회를 낸다.
    // 벽·낭떠러지 전 안전 정지는 시뮬레이션 피어(오너)가 하고 실제 거리를 보고한다(어쌔신 Q 와 같은 정책).
    private const float DashLedgeProbeAhead = 0.35f; // 다음 걸음 앞 바닥 검사 거리(m)
    private const float DashLedgeProbeUp = 0.5f;
    private const float DashLedgeProbeDepth = 1.2f;  // 이보다 깊으면 낭떠러지로 보고 멈춘다
    private readonly PaladinShieldDashPath dashPath = new PaladinShieldDashPath();
    private readonly List<float> dashSamples = new List<float>();
    private bool isShieldDash;           // 서버 — 이번 시전이 돌파의 방패
    private bool dashOnHitBonusTaken;
    private float dashServerStartTime;
    private bool isLocallyDashing;       // 시뮬레이션 피어
    private PaladinShieldDashSettings localDash;
    private float localDashElapsed;
    private float localDashRemaining;
    private float localDashTraveled;

    // 진행 방향 장부. 이동 권위 피어는 실제 이동에, 서버는 판정·넉백 방향에 사용한다.
    private Vector3 heading = Vector3.forward;
    private Vector3 serverAim = Vector3.forward;
    private bool isLocallySimulating;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Main;

    // 전진은 스킬이 직접 수행 — 기본 이동/회전은 잠근다
    public override bool CanMoveWhileActive => false;

    // 돌파의 방패(PAL_2_DASH)면 돌진이 끝난 뒤 좌클릭 홀드로 기본 공격을 잇는다(할 일 146). 특성 보유는 모든 피어가 같은 값으로 읽는다.
    public override bool ResumesBasicAttackOnHold
    {
        get
        {
            PaladinShieldDashSettings dash = ResolveShieldDash(out bool hasDashTrait);
            return PaladinTraitRules.QResumesBasicAttack(hasDashTrait, dash);
        }
    }

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

        PaladinShieldDashSettings dash = ResolveShieldDash(out bool hasDashTrait);
        isShieldDash = PaladinTraitRules.UsesShieldDash(hasDashTrait, dash);
        if (hasDashTrait && !isShieldDash)
            Debug.LogError("[Skill] 돌파의 방패 — 특성 수치가 비었거나 0 이하라 원래 Q 로 돕니다(PAL_2_DASH 데이터 확인).", this);
        // 원래 Q = 홀드(Charging), 돌파의 방패 = 실행(Active) — 홀드 틱(지속 타격·지속 밀치기)·홀드 해제가 Charging 에서만 돈다.
        State = PaladinTraitRules.QStartState(isShieldDash);

        // 전용 서버는 Client 재생 콜백을 받지 않으므로 권위 시뮬레이션을 서버 시작 경로에서 켠다.
        isLocallySimulating = !isShieldDash && owner != null && owner.IsMotionAuthority;

        if (hitResults == null || hitResults.Length != data.MaxHitResults)
            hitResults = new Collider[data.MaxHitResults];
        traitConcussionCarry.Reset();

        if (isShieldDash)
        {
            float baseDistance = PaladinTraitRules.BaseQMaxDistance(data.AdvanceSpeed, Data.MaxActiveDuration);
            if (!(baseDistance > dash.Distance))
                Debug.LogError($"[Skill] 돌파의 방패 — 기본 Q 유효 최대 거리 {baseDistance}m 가 돌진 거리 {dash.Distance}m 이하입니다(콘텐츠 오류, 거리를 늘리지 않음).", this);

            dashPath.Begin(owner.transform.position, heading, dash);
            dashServerStartTime = Time.time;
            dashOnHitBonusTaken = false;
            // 서버가 원격 오너를 대리 시뮬레이션하는 경우에도 판정 앵커가 돌진 방향을 보게 한 번 돌린다(이후 조향 없음).
            if (owner.IsMotionAuthority && !owner.IsInputSource && movement != null)
                movement.RotateImmediately(heading);
        }

        // 홀드 동안 CC 면역. 만료 안전망은 지속시간, 정상/강제 종료 시엔 OnEnd가 즉시 해제한다.
        // (오프라인에서는 상태이상 시스템이 비활성 — 스탯 계열과 동일 제약)
        if (owner.StatusEffects != null)
            owner.StatusEffects.Apply(StatusEffectType.SuperArmor, Data.MaxActiveDuration, SourceId);
    }

    public override void OnClientPlay(Vector3 direction)
    {
        heading = Flatten(direction);
        // 돌파의 방패 — 각 피어가 같은 획득 행으로 모드를 정한다. 방향 = 서버가 승인한 입력 순간 조준(도중에 바꾸지 않는다).
        PaladinShieldDashSettings dash = ResolveShieldDash(out bool hasDashTrait);
        bool shieldDash = PaladinTraitRules.UsesShieldDash(hasDashTrait, dash);
        // Motor를 돌리는 오너/서버/오프라인만 전진·조향 채널을 제출한다.
        isLocallySimulating = !shieldDash && owner != null && owner.IsSimulating;
        if (shieldDash)
            BeginLocalDash(dash);

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
        // 돌파의 방패 — 옛 홀드 루틴(조향·지속 타격·지속 밀치기) 대신 경로 판정만.
        if (isShieldDash)
        {
            TickShieldDashServer();
            return;
        }

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
        if (data == null || HitboxAnchor == null || owner == null || isShieldDash)
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
        StopLocalDash();

        // 진격의 충격 — 정상 종료·홀드 해제·최대 유지 시간 도달이면 종료 순간 위치·방향으로 1회 예약. 사망·강제 중단·대시 끊김은 없음.
        // 돌파의 방패면 묶음 = 교체 단발 피해(정상 종료 = 회수 끝, 벽·낭떠러지 안전 정지 포함).
        if (owner != null && owner.IsServer && PaladinTraitRules.QBlastFires(reason, owner.CurrentHealth))
            ScheduleTraitBlastServer(isShieldDash ? dashPath.Settings.DamageRatio : 0f);

        // 사망·강제 종료면 아직 훑지 않은 경로의 타격은 버린다.
        isShieldDash = false;
        dashPath.End();

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
    /// [서버] Q 가 끝난 순간 — 파생 공격(⑧) 명세 중 진격의 충격만 골라 전방 forwardOffset 지점을 꼭짓점으로 반경 radius·각도 angle 부채꼴 판정을 delay 뒤 1회 예약한다.
    /// 피해 = DQpacket(<see cref="PaladinTraitRules.QPacketDamage(int,float,float,float)"/> — 이 시전의 틱 Dref 스냅샷 × 예정 틱 수,
    /// 돌파의 방패면 그 × 교체 배율 = 교체 단발 피해) × 파생 배율. 실제 맞힌 틱·적 수와 무관하다.
    /// </summary>
    private void ScheduleTraitBlastServer(float dashDamageRatio)
    {
        if (Data == null || !isActiveAndEnabled)
            return;

        TraitAttackKey key = TraitAttackKey.ForSkill(Slot, controller != null && controller.IsAlternateSkill(this));
        PlayerTraitEffects.For(owner).GetDerivedAttacks(key, traitDerivedAttacks);
        int packet = PaladinTraitRules.QPacketDamage(damageSnapshot, Data.MaxActiveDuration, Data.TickInterval, dashDamageRatio);
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

            // 부채꼴 꼭짓점 = Q 끝 지점 + 정면 forwardOffset, 정면 = 마지막 진행 방향(할 일 146 — 원 → 90° 부채꼴, 각도 = 효과 칸 angle).
            Vector3 apex = owner.transform.position + heading * attack.ForwardOffset;
            StartCoroutine(RunTraitBlastServer(apex, heading, attack.Radius, attack.Angle, attack.DamageFrom(packet), attack.Delay, sceneHandle));
        }

        traitDerivedAttacks.Clear();
    }

    /// <summary>
    /// [서버] 파생 판정 1회 — 대상별 1회 피해만(<see cref="TraitHitFlags.Derived"/>: 특성·자원·CC·간파 없음).
    /// 그래서 패시브 추가피해(ServerTakeOnHitBonus)·밀치기 수정자·<see cref="Player.RaiseServerAttackLanded"/> 를 부르지 않는다.
    /// 지형 차단은 원래 Q 판정처럼 없다(같은 레이어 Overlap). 지연 동안 시전자가 죽거나 씬이 바뀌면 취소한다. 이펙트 없음(판정만).
    /// </summary>
    private System.Collections.IEnumerator RunTraitBlastServer(Vector3 apex, Vector3 forward, float radius, float angle, int damage, float delay, int sceneHandle)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        if (owner == null || !owner.IsServer || owner.CurrentHealth <= 0 || UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle != sceneHandle || Data == null)
            yield break;

        int hitCount = Physics.OverlapSphereNonAlloc(apex, radius, traitBlastResults, Data.HittableLayers, QueryTriggerInteraction.Collide);
        traitBlastTargets.Clear();
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = traitBlastResults[i];
            if (hit == null || !PaladinTraitRules.QBlastInSector(apex, forward, angle, hit.ClosestPoint(apex), hit.bounds.center))
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

    // ── 돌파의 방패(PAL_2_DASH, 할 일 126 4/4) ──

    /// <summary>특성을 골랐는가 + 그 수치(스킬 수치 ⑩). 없으면 default.</summary>
    private PaladinShieldDashSettings ResolveShieldDash(out bool hasTrait)
    {
        hasTrait = false;
        if (owner == null || !owner.TryGetComponent(out PlayerTraitModifiers modifiers))
            return default;

        hasTrait = modifiers.HasTrait(PaladinTraitValues.Dash);
        return hasTrait ? PaladinShieldDashSettings.From(modifiers) : default;
    }

    /// <summary>[서버] 경과 시간으로 경로를 늘려 새로 지난 구간을 훑고, 준비 + 이동 + 회수가 끝나면 정상 종료한다.</summary>
    private void TickShieldDashServer()
    {
        float elapsed = Time.time - dashServerStartTime;
        SweepShieldDashServer(dashPath.TraveledAt(elapsed));
        if (isShieldDash && dashPath.IsFinished(elapsed))
        {
            SweepShieldDashServer(dashPath.Cap);
            EndSelf(SkillEndReason.Completed);
        }
    }

    /// <summary>
    /// [서버] 경로 표본마다 원래 Q 판정 앵커 형상(= 기존 Q 공격 폭)을 그 지점으로 옮겨 훑는다. 대상당 1회만 —
    /// 피해 = 교체 Q 1회(DQfull × 배율), 넉백 = 이 모드 전용 거리·시간. 특성 밀치기·뇌진탕은 원래 Q 와 같은 한 곳으로 싣는다.
    /// </summary>
    private void SweepShieldDashServer(float target)
    {
        FirstMeleeMainSkillData data = MainSkillData;
        if (!isShieldDash || data == null || owner == null || HitboxAnchor == null || hitResults == null)
            return;

        dashPath.AdvanceTo(target, dashSamples);
        if (dashSamples.Count == 0)
            return;

        int damage = PaladinTraitRules.QPacketDamage(damageSnapshot, Data.MaxActiveDuration, Data.TickInterval, dashPath.Settings.DamageRatio);
        Vector3 current = owner.transform.position;
        tickLandedUnits.Clear();

        for (int s = 0; s < dashSamples.Count; s++)
        {
            Vector3 offset = dashPath.PointAt(dashSamples[s]) - current;
            offset.y = 0f;
            int hitCount = OverlapHitboxAnchor(hitResults, offset);

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = hitResults[i];
                if (hit == null)
                    continue;

                Unit unit = ResolveHitUnit(hit, out Hurtbox hurtbox);
                Object targetObject = unit != null ? (Object)unit : hurtbox;
                if (targetObject == null || unit == owner || !dashPath.TryRegisterTarget(targetObject.GetInstanceID()))
                    continue;

                int resolvedDamage = damage;
                // 패시브 적중 추가피해 — 원래 Q 처럼 살아 있는 첫 Unit 하나(이 모드는 시전 1회 = 1번).
                if (!dashOnHitBonusTaken && unit != null && unit.CurrentHealth > 0)
                {
                    dashOnHitBonusTaken = true;
                    int bonus = owner.ServerTakeOnHitBonus(data.TriggersOnHit, unit);
                    resolvedDamage = bonus >= int.MaxValue - resolvedDamage ? int.MaxValue : resolvedDamage + bonus;
                }

                AttackInfo attackInfo = dashPath.CreateAttack(resolvedDamage, DamageAttackType, data.StaggerDuration, DamageHitPattern);
                if (unit != null)
                    ApplyTraitCrowdControlServer(unit, dashPath.Heading, ref attackInfo);
                AttackHitContext hitContext = new AttackHitContext(owner.transform.position, owner.transform, hit, owner);

                bool resolved = hurtbox != null
                    ? hurtbox.ReceiveAttack(attackInfo, hitContext)
                    : unit.ReceiveAttack(attackInfo, hitContext);
                if (!resolved)
                    continue;

                if (unit != null)
                    tickLandedUnits.Add(unit);
                Edit.Log($"[Skill] 돌파의 방패 — {targetObject.name} 피해 {attackInfo.damage} + 넉백 {dashPath.Settings.PushDistance}m ({dashSamples[s]:F2}m)", this);
            }
        }

        traitConcussionCarry.EndTick();
        if (tickLandedUnits.Count > 0)
            owner.RaiseServerAttackLanded(DamageAttackType, data.TriggersOnHit, tickLandedUnits, this);
    }

    // 서버 전용 — 오너가 벽·낭떠러지 전에서 멈춘 실제 거리. 0~돌진 거리만 받아 경로 상한만 줄인다(늘리지 않음).
    public override void OnOwnerResultReported(float value)
    {
        if (isShieldDash && dashPath.ReportStop(value))
            Edit.Log($"[Skill] 돌파의 방패 안전 정지 보고 — {value:F2}m → 경로 상한 {dashPath.Cap:F2}m", this);
    }

    /// <summary>[시뮬레이션 피어] 돌진 시작 — 방향 고정(한 번 돌리고 조향 없음), 준비 시간 뒤 이동.</summary>
    private void BeginLocalDash(PaladinShieldDashSettings dash)
    {
        StopLocalDash();
        if (owner == null || !owner.IsSimulating || motor == null)
            return;

        localDash = dash;
        localDashElapsed = 0f;
        localDashRemaining = dash.Distance;
        localDashTraveled = 0f;
        isLocallyDashing = true;
        if (movement != null)
            movement.RotateImmediately(heading);
        motor.MovementResolved -= HandleDashMovementResolved;
        motor.MovementResolved += HandleDashMovementResolved;
    }

    public override void OnFixedTick()
    {
        if (!isLocallyDashing || motor == null || localDashRemaining <= 0f)
            return;

        localDashElapsed += Time.fixedDeltaTime;
        // 준비 — 제자리(기존 Q 애니가 그대로 돈다).
        if (localDashElapsed < localDash.PrepareTime)
            return;

        float step = Mathf.Min(localDash.Speed * Time.fixedDeltaTime, localDashRemaining);
        if (!HasGroundAhead(step))
        {
            // 낭떠러지 직전 안전 정지 — 여기서 끝내고 실제 거리를 보고한다.
            StopLocalDash();
            controller?.ReportOwnerSkillResult(this, localDashTraveled);
            return;
        }

        localDashRemaining -= step;
        motor.AddGroundedDisplacement(heading * step);
    }

    private void HandleDashMovementResolved(Vector3 requested, Vector3 resolved, bool wasBlocked)
    {
        if (!isLocallyDashing || localDashElapsed < localDash.PrepareTime)
            return;

        localDashTraveled += Mathf.Max(0f, Vector3.Dot(new Vector3(resolved.x, 0f, resolved.z), heading));

        // 벽·통과 불가 지형 = 안전 정지. 적·아군 몸에 막힌 것은 기존 Q 와 같은 통과 정책(막힘)이라 멈추지 않고 남은 시간만큼 민다.
        if (wasBlocked && motor != null && !motor.IsUnitBlockingAhead(heading, 0.3f))
        {
            StopLocalDash();
            controller?.ReportOwnerSkillResult(this, localDashTraveled);
            return;
        }

        if (localDashRemaining <= 0f)
            StopLocalDash();
    }

    /// <summary>다음 걸음 앞 바닥이 있는가(지면 마스크 레이). 규칙 데이터가 없으면 검사하지 않는다.</summary>
    private bool HasGroundAhead(float step)
    {
        PlayerGameRuleData rule = motor != null ? motor.GameRule : null;
        if (rule == null)
            return true;

        Vector3 probe = motor.Position + heading * (step + DashLedgeProbeAhead) + Vector3.up * DashLedgeProbeUp;
        return Physics.Raycast(probe, Vector3.down, DashLedgeProbeUp + DashLedgeProbeDepth, rule.GroundMask, QueryTriggerInteraction.Ignore);
    }

    private void StopLocalDash()
    {
        isLocallyDashing = false;
        localDashRemaining = 0f;
        if (motor != null)
            motor.MovementResolved -= HandleDashMovementResolved;
    }

    private void OnDisable()
    {
        StopLocalDash();
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
