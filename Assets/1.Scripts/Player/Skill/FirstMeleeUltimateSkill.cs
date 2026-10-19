using UnityEngine;

/// <summary>
/// R 최후의 심판 — 대상 지정(SingleTarget) 채널링 스킬. 조준 모드에서 사거리 내 적을 지정해 시전하고,
/// 채널을 완주하면 지정 대상에게 단일 피해를 1회 적용한다(타겟팅 인프라 검증용 최소 효과).
/// 대상 유효성(생존·사거리)은 서버 CanUse로 권위 검증하며, 채널 중(피해 전) 대상이 사라지면 취소한다.
///
/// 시점은 전부 클립 Garen_Ultimate_Skill 의 이벤트(HandleSkillEvent)가 정한다 — 타이머가 없다.
/// Custom0 = 마무리 낙뢰 발사 / Hit = 피해(완주 판정) / End = 종료(<see cref="RequiredAnimationEvents"/> 로 선언 — 이벤트 방식).
/// 이벤트는 애니 배율(UltimateSpeed)을 같이 타서 실제 시각 = 클립 시각 ÷ 배율.
/// 안전망 = 클립 실제 재생 시간(클립 길이 ÷ 배율) + padding — 컨트롤러가 상태 진입 뒤 런타임에 읽는다. maxActiveDuration 은 쓰지 않는다
/// (클립 길이를 못 구했을 때의 대체 안전망으로만 남는다). 그래서 End 를 클립 안 어디로 옮겨도 안전망이 먼저 끊지 않는다.
///
/// 채널 동안 <b>슈퍼아머</b>를 건다(Q와 같은 방식). 부모 클래스 주석은 이 보호를 "Custom 애니 이벤트
/// 구간에서" 건다고 적어 두었지만, 이 스킬은 채널 전체가 보호 구간이라 <c>OnServerStart</c>~<c>OnEnd</c>로 잡는다.
/// 보호 구간용 이벤트가 따로 필요 없고, 취소·사망 어느 쪽으로 끝나도 해제가 보장된다.
///
/// ⚠️ <b>슈퍼아머는 넉백/밀기만 막는다.</b> 피해는 그대로 들어오고, 맞아도 채널은 끊기지 않는다 —
/// 채널을 끊는 것은 대상 소실과 사망뿐이다.
/// </summary>
public class FirstMeleeUltimateSkill : PlayerChannelingSkill
{
    [Header("연출")]
    [Tooltip("조준 연출(낙뢰 + 칼날 전기) 전파 창구. 플레이어 루트의 PlayerSkillVfx 를 물린다. 비워두면 연출만 빠진다")]
    [SerializeField] private PlayerSkillVfx skillVfx;

    [Tooltip("슈퍼아머 구간 동안 몸에 덮을 오버레이. 'Paladin_Armature/tripo_part_0' 의 DissolveOverlay 를 물린다. 비워두면 연출만 빠진다")]
    [SerializeField] private DissolveOverlay superArmorOverlay;

    [Tooltip("클립 이벤트 PlayEffect(\"Stick\") 가 부르는 칼 꽂기 소켓('VFX/UltimateSkill/SwordStick', id Stick). 데이터의 이펙트 배율을 여기 넣는다.\n" +
             "비워두면 Stick 만 저작 속도로 나온다")]
    [SerializeField] private EffectSocketPlayer stickVfx;

    // Garen_Ultimate_Skill 상태 전용 속도 파라미터. 평타의 AttackSpeed 와 섞이지 않게 따로 둔다.
    private static readonly int UltimateSpeedHash = Animator.StringToHash("UltimateSpeed");

    // 슈퍼아머 만료 안전망 여유. 정상·강제 종료는 OnEnd 가 바로 걷는다 — 만료는 OnEnd 가 안 돈 경우만이다.
    // 지속 = 클립 실제 남은 길이(못 구하면 maxActiveDuration) + 이 여유.
    // 🔴 컨트롤러 안전망 padding(0.1)보다 길어야 한다. 짧으면 End 이벤트·안전망 종료 직전 프레임에 슈퍼아머가 먼저 풀린다.
    private const float SuperArmorExpiryPadding = 0.25f;

    // 클립에 있어야 하는 HandleSkillEvent 이벤트(타이머 대체 경로는 없다). 누락은 공용 경고(AnimationEventRequirements)와
    // EditMode 전수 검사(PlayerAnimationEventRequirementTests)가 잡는다.
    private static readonly SkillAnimationEventType[] UltimateClipEvents =
    {
        SkillAnimationEventType.Hit,
        SkillAnimationEventType.End,
        SkillAnimationEventType.Custom0,
    };

    private Unit lockedTarget;
    // 이번 시전의 애니메이터(OnClientPlay 에서 찾는다). Hit 뒤 속도를 바꿀 때 쓴다.
    private Animator animator;
    // [서버] 마무리 낙뢰를 이번 시전에 쐈는지. 칼이 닿는 데 시간이 걸려 클립 Custom0 에서 피해(Hit)보다 앞서 쏜다.
    private readonly FinalStrikeVfxSchedule finalStrike = new FinalStrikeVfxSchedule();
    // 완료 판정 1회의 적중 대상(1명). Player.ServerAttackLanded 통지용 버퍼.
    private readonly System.Collections.Generic.List<Unit> landedUnits = new System.Collections.Generic.List<Unit>(1);

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Ultimate;

    // 채널 중 이동/회전 잠금
    public override bool CanMoveWhileActive => false;

    // 피해(Hit)·종료(End)·마무리 낙뢰(Custom0)를 클립 이벤트로 정한다 — End 가 있어 이벤트 방식(EndsOnClipEvent).
    public override System.Collections.Generic.IReadOnlyList<SkillAnimationEventType> RequiredAnimationEvents => UltimateClipEvents;

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
        finalStrike.Reset();

        // 🔴 여기는 서버에서만 돈다 — 그래서 창구를 거친다. 누구를 지목했는지는
        //    PlaySkillClientRpc 에 실려 가지 않아(SingleTarget 은 aimPoint 를 안 쓴다)
        //    리모트 클라가 알 방법이 없다.
        skillVfx?.ServerPlayTargetFloor(lockedTarget, VfxPlayRate);

        // 채널 동안 CC 면역. 채널은 이동 잠금(CanMoveWhileActive=false)만으로는 지킬 수 없다 —
        // 그건 내 입력을 막을 뿐이고, 남이 미는 것은 PlayerStateController.BeginKnockback 이
        // 사망·연출잠금만 거부하므로 Skill 상태여도 그대로 들어온다.
        // 정상/강제 종료 시엔 OnEnd 가 즉시 해제한다. 만료는 OnEnd 가 안 돈 경우의 그물 — 컨트롤러 안전망보다 길게 둔다.
        // 여기서는 아직 클립 길이를 모른다(CrossFade 가 다음 애니메이터 갱신에 반영) — 대체 안전망 길이로 걸어 두고,
        // 컨트롤러가 클립 실제 길이를 읽으면 OnClipPlaybackResolved 가 그 길이로 다시 건다.
        // (오프라인에서는 상태이상 시스템이 비활성 — 스탯 계열과 동일 제약)
        ApplySuperArmor(Data.MaxActiveDuration);
    }

    // [서버] 클립 실제 남은 길이 + 여유로 슈퍼아머를 갱신한다(같은 출처라 시간만 리셋된다).
    public override void OnClipPlaybackResolved(float remainingSeconds)
    {
        if (State == SkillState.Channeling)
            ApplySuperArmor(remainingSeconds);
    }

    private void ApplySuperArmor(float protectedSeconds)
    {
        if (owner != null && owner.StatusEffects != null)
            owner.StatusEffects.Apply(StatusEffectType.SuperArmor, protectedSeconds + SuperArmorExpiryPadding, SourceId);
    }

    // 🔴 낙뢰는 여기서 켜지 않는다. R 은 조준만 켜고(ClickToConfirm), 실제 시전은 좌클릭 확정
    //    — 사거리 밖이면 걸어서 접근까지 한 뒤다. 그 사이 간격이 정해져 있지 않아 R 과 연출이 따로 논다.
    //    그래서 시작은 OnOwnerAimStart 로 올라가 있고, 여기는 시전 고유 연출 자리로 비워 둔다.
    public override void OnClientPlay(Vector3 direction)
    {
        // 🔴 RPC 가 필요 없다. 슈퍼아머를 거는 것은 OnServerStart(서버 전용)지만,
        //    그 '구간'은 스킬이 활성인 동안 그 자체라 OnClientPlay~OnEnd 로 정확히 같은 창을 그린다.
        //    두 콜백 모두 전 피어에서 돈다.
        superArmorOverlay?.Show();

        // 🔴 여기도 전 피어다(서버는 TryStartSkillServer, 클라는 PlaySkillClientRpc → PlaySkillPresentation).
        //    방금 CrossFade 한 같은 프레임이라 첫 프레임부터 이 속도로 돈다. 원격 피어도 각자 같은 값을 넣는다.
        //    Animator 는 시전마다 찾는다 — 캐릭터 교체로 Armature 가 바뀔 수 있다.
        animator = owner != null ? owner.GetComponentInChildren<Animator>() : null;
        if (animator != null)
            animator.SetFloat(UltimateSpeedHash, AnimationSpeed);

        // Stick 은 클립 이벤트라 피어마다 각자 재생한다 — 이벤트보다 먼저 배율을 넣어 둔다.
        stickVfx?.SetPlayRate(VfxPlayRate);
    }

    /// <summary>[오너] R 을 눌러 조준에 들어갔다. 낙뢰를 바로 떨어뜨리고, 이어서 칼날에 전기가 흐른다.</summary>
    public override void OnOwnerAimStart()
    {
        // 오너 전용 경로라 로컬 재생만으로는 남에게 안 보인다 — 창구가 서버를 거쳐 나머지 피어에 뿌린다.
        skillVfx?.OwnerPlayUltimateVfx(VfxPlayRate);
    }

    /// <summary>[오너] 시전까지 가지 못하고 조준이 물렸다. 연출을 걷는다.</summary>
    public override void OnOwnerAimCancelled()
    {
        skillVfx?.OwnerStopUltimateVfx();
    }

    public override void OnTick()
    {
        // 피해(Hit) 전에 대상이 사라지거나 사망하면 취소. 피해 뒤 End 까지는 대상이 그 피해로 죽어도 완주다.
        if (State == SkillState.Channeling && !ChannelCompleted &&
            (lockedTarget == null || lockedTarget.CurrentHealth <= 0))
        {
            EndSelf(SkillEndReason.Cancelled);
            return;
        }

        base.OnTick();
    }

    public override void OnAnimationEvent(SkillAnimationEventType eventType)
    {
        // 🔴 마무리 낙뢰. 애니 이벤트는 서버에서만 온다 — 그래서 창구(RPC)를 거친다.
        //    위치는 넘기지 않는다 — 각 피어가 표식을 깔 때 잡아 둔 그 트랜스폼에 떨어진다.
        if (eventType == SkillAnimationEventType.Custom0 && State == SkillState.Channeling && finalStrike.TryFire())
            skillVfx?.ServerPlayTargetStrike(VfxPlayRate);

        // Hit = 피해(완주 판정) / End = 종료 — base 가 처리한다.
        base.OnAnimationEvent(eventType);
    }

    // 전 피어. Hit 뒤 남은 구간을 각 피어가 자기 애니메이터에서 빠르게 돌린다(RPC 없음) — End 도 같이 당겨진다.
    public override void OnLocalAnimationEvent(SkillAnimationEventType eventType)
    {
        if (eventType == SkillAnimationEventType.Hit && animator != null)
            animator.SetFloat(UltimateSpeedHash, AnimationSpeed * AfterHitSpeedMultiplier);
    }

    protected override void OnChannelCompleted()
    {
        if (lockedTarget == null || lockedTarget.CurrentHealth <= 0)
            return;

        int bonus = owner.ServerTakeOnHitBonus(Data != null && Data.TriggersOnHit, lockedTarget);
        int resolvedDamage = bonus >= int.MaxValue - damageSnapshot
            ? int.MaxValue
            : damageSnapshot + bonus;
        AttackInfo attackInfo = new AttackInfo(resolvedDamage, DamageAttackType,
            hitPattern: DamageHitPattern);
        AttackHitContext hitContext = new AttackHitContext(owner.transform.position, owner.transform, sourceUnit: owner);
        if (lockedTarget.ReceiveAttack(attackInfo, hitContext))
        {
            landedUnits.Clear();
            landedUnits.Add(lockedTarget);
            owner.RaiseServerAttackLanded(DamageAttackType, Data != null && Data.TriggersOnHit, landedUnits, this);
        }

        // 마무리 낙뢰는 여기서 쏘지 않는다 — 클립 Custom0 에서 이미 쐈다.

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

        // Custom0 에서 쏜 칼이 떨어지는 중에 완주 못 하고 끝났다 — 피해가 없으니 걷는다. 완주면 끝까지 재생한다.
        // 안전망 종료(MaxDurationReached)도 걷는다 — End 이벤트가 안 온 경우라 피해가 났어도 여기는 사유만 안다.
        if (FinalStrikeVfxSchedule.ShouldRecall(reason))
            skillVfx?.StopTargetStrikeLocal();

        // 서버 전용 쓰기(CanWrite) 가드가 내장돼 있어 클라에서는 no-op
        if (owner != null && owner.StatusEffects != null)
            owner.StatusEffects.Remove(StatusEffectType.SuperArmor, SourceId);

        // 🔴 종료 사유를 가리지 않는다 — 취소·사망·안전망 만료 어느 쪽으로 끝나도 오버레이가 남지 않는다.
        superArmorOverlay?.Hide();
    }

    // 데이터가 궁극 전용 SO 가 아니면(에셋 교체 실수 등) 원래 속도로 돈다.
    private float AnimationSpeed => Data is FirstMeleeUltimateSkillData ult ? ult.AnimationSpeed : 1f;
    private float AfterHitSpeedMultiplier => Data is FirstMeleeUltimateSkillData ult ? ult.AfterHitSpeedMultiplier : 1f;
    private float VfxPlayRate => Data is FirstMeleeUltimateSkillData ult ? ult.VfxPlayRate : 1f;

    private ulong SourceId => owner != null && owner.NetworkObject != null ? owner.NetworkObjectId : 0UL;

    private bool IsWithinRange(Vector3 worldPoint)
    {
        Vector3 flat = worldPoint - owner.transform.position;
        flat.y = 0f;
        return flat.sqrMagnitude <= Data.CastRange * Data.CastRange;
    }
}
