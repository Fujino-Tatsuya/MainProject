using System;
using System.Collections.Generic;
using UnityEngine;

// 재사용 효과 클래스(할 일 125 3/5, 계획 §4 ①~⑩ + 스탯). 특성 SO(TraitDefinition)가 [SerializeReference] 목록으로 조합한다.
// 수치는 ratio 로 보관한다(명세 §9 — 20% = 0.20). 생성자는 테스트·저작 도구용이고, 인스펙터는 매개변수 없는 생성자로 만든다.

/// <summary>① 공격 계열 원래 피해 배율 — Dref 에 한 번(<see cref="TraitModifierKind.DamageMultiplier"/>). 0.2 = +20%, −0.15 = −15%(페널티).</summary>
[Serializable]
public sealed class TraitDamageEffect : TraitEffect
{
    [Tooltip("피해 증감 비율. 0.2 = +20%, -0.15 = -15%.")]
    [SerializeField] private float damageIncrease = 0.2f;
    [Tooltip("이 공격 종류에만(평타 = Default, Q/E/R = SkillQ/E/R, 우클릭 = Skill). 비우면 전부.")]
    [SerializeField] private AttackType[] attackTypes = Array.Empty<AttackType>();
    [Tooltip("이 형태에만(일반·대체/변신·강화/강타). 비우면 전부.")]
    [SerializeField] private TraitAttackForm[] forms = Array.Empty<TraitAttackForm>();

    public TraitDamageEffect() { }

    public TraitDamageEffect(float damageIncrease, AttackType[] attackTypes = null, TraitAttackForm[] forms = null)
    {
        this.damageIncrease = damageIncrease;
        this.attackTypes = attackTypes ?? Array.Empty<AttackType>();
        this.forms = forms ?? Array.Empty<TraitAttackForm>();
    }

    public float Multiplier => 1f + damageIncrease;

    public override void GetModifiers(List<TraitModifier> result) =>
        result.Add(TraitModifier.Damage(Multiplier, MaskOf(attackTypes), MaskOf(forms)));

    public override void Validate(List<string> errors)
    {
        CheckFinite(errors, nameof(damageIncrease), damageIncrease);
        if (Multiplier < 0f)
            errors.Add($"{nameof(TraitDamageEffect)}.damageIncrease = {damageIncrease} — 배율 < 0");
    }
}

/// <summary>② 슬롯 전체 쿨 배율(<see cref="TraitModifierKind.CooldownMultiplier"/>). −0.15 = 15% 감소, 0.2 = 20% 증가. 서로 곱한다(명세 §6).</summary>
[Serializable]
public sealed class TraitCooldownEffect : TraitEffect
{
    [Tooltip("전체 쿨 증감 비율. -0.15 = 15% 감소, 0.2 = 20% 증가. 감소는 1 미만이어야 한다.")]
    [SerializeField] private float cooldownChange = -0.15f;
    [Tooltip("이 슬롯에만. 비우면 전부.")]
    [SerializeField] private PlayerSkillSlot[] slots = Array.Empty<PlayerSkillSlot>();
    [Tooltip("이 형태에만(일반 Q 만 = Normal). 비우면 전부.")]
    [SerializeField] private TraitAttackForm[] forms = Array.Empty<TraitAttackForm>();

    public TraitCooldownEffect() { }

    public TraitCooldownEffect(float cooldownChange, PlayerSkillSlot[] slots = null, TraitAttackForm[] forms = null)
    {
        this.cooldownChange = cooldownChange;
        this.slots = slots ?? Array.Empty<PlayerSkillSlot>();
        this.forms = forms ?? Array.Empty<TraitAttackForm>();
    }

    public float Multiplier => 1f + cooldownChange;

    public override void GetModifiers(List<TraitModifier> result) =>
        result.Add(TraitModifier.Cooldown(Multiplier, MaskOf(slots), MaskOf(forms)));

    public override bool AffectsCooldownOf(PlayerSkillSlot slot) => TraitModifier.Covers(MaskOf(slots), (int)slot);

    public override void Validate(List<string> errors)
    {
        CheckFinite(errors, nameof(cooldownChange), cooldownChange);
        if (Multiplier <= 0f)
            errors.Add($"{nameof(TraitCooldownEffect)}.cooldownChange = {cooldownChange} — 감소율 1 이상");
    }
}

/// <summary>
/// ③ 사건 시 남은 쿨 N초 차감 — 수호의 순환(방어 성공 E −2)·불굴의 순환(패시브 Q/E 중 긴 쪽 −1)·사냥의 박자(평타 적중 Q·E 각 −0.2, 동작당 1회 + 0.8초).
/// 0 아래로 내려가지 않고 준비된 스킬에는 저장하지 않는다(명세 §6).
/// </summary>
[Serializable]
public sealed class TraitCooldownRefundEffect : TraitTriggeredEffect
{
    [Tooltip("차감 초.")]
    [Min(0f)]
    [SerializeField] private float refundSeconds = 1f;
    [SerializeField] private PlayerSkillSlot[] slots = { PlayerSkillSlot.Main };
    [SerializeField] private TraitRefundTarget target = TraitRefundTarget.EachSlot;

    [NonSerialized] private List<PlayerSkillSlot> pickScratch;

    public TraitCooldownRefundEffect() { }

    public TraitCooldownRefundEffect(TraitTriggerFilter filter, float refundSeconds, PlayerSkillSlot[] slots,
        TraitRefundTarget target = TraitRefundTarget.EachSlot)
    {
        this.filter = filter ?? new TraitTriggerFilter();
        this.refundSeconds = refundSeconds;
        this.slots = slots ?? Array.Empty<PlayerSkillSlot>();
        this.target = target;
    }

    public float RefundSeconds => refundSeconds;

    protected override void OnTriggered(ITraitEffectHost host, int traitHash, in TraitTriggerEvent e)
    {
        pickScratch ??= new List<PlayerSkillSlot>();
        TraitCooldownRefundRules.PickSlots(target, slots, host.GetCooldownRemaining, pickScratch);
        for (int i = 0; i < pickScratch.Count; i++)
            host.ReduceCooldown(pickScratch[i], refundSeconds);
    }

    public override bool AffectsCooldownOf(PlayerSkillSlot slot) => Array.IndexOf(slots ?? Array.Empty<PlayerSkillSlot>(), slot) >= 0;

    public override void Validate(List<string> errors)
    {
        base.Validate(errors);
        CheckPositive(errors, nameof(refundSeconds), refundSeconds);
        if (slots == null || slots.Length == 0)
            errors.Add($"{nameof(TraitCooldownRefundEffect)}.slots 가 비었다");
    }
}

/// <summary>
/// ④ 회복 배율 + 초과 회복 전달 — 불굴의 회복(+25%)·나누는 불굴(초과분을 12m 안 체력 비율 최저 아군 1명에게).
/// 배율은 특성 회복 경로(<see cref="PlayerTraitEffects.HealServer"/>)의 요청량에 곱하고, 초과 = 요청 − 실제 회복(<see cref="Unit.HealHp(int)"/> 반환값)이다(명세 §7).
/// </summary>
[Serializable]
public sealed class TraitHealEffect : TraitEffect
{
    [Tooltip("회복 요청량 증감 비율(0.25 = +25%). 0 = 배율 없음.")]
    [SerializeField] private float healIncrease;
    [Tooltip("초과 회복 전달 범위(m). 0 = 전달 없음.")]
    [Min(0f)]
    [SerializeField] private float overflowShareRange;
    [Tooltip("전달 비율(1 = 초과분 전부).")]
    [Min(0f)]
    [SerializeField] private float overflowTransferRatio = 1f;

    public TraitHealEffect() { }

    public TraitHealEffect(float healIncrease, float overflowShareRange = 0f, float overflowTransferRatio = 1f)
    {
        this.healIncrease = healIncrease;
        this.overflowShareRange = overflowShareRange;
        this.overflowTransferRatio = overflowTransferRatio;
    }

    public float Multiplier => 1f + healIncrease;
    public bool SharesOverflow => overflowShareRange > 0f;

    public override void GetModifiers(List<TraitModifier> result)
    {
        if (healIncrease != 0f)
            result.Add(TraitModifier.Heal(Multiplier));
    }

    public override ITraitEffectBinding BindServer(ITraitEffectHost host, int traitHash)
    {
        if (host == null || !SharesOverflow)
            return null;

        var share = new TraitOverflowShare(traitHash, overflowShareRange, overflowTransferRatio);
        host.AddOverflowShare(share);
        return new TraitActionBinding(() => host.RemoveOverflowShare(share));
    }

    public override void Validate(List<string> errors)
    {
        CheckFinite(errors, nameof(healIncrease), healIncrease);
        if (Multiplier < 0f)
            errors.Add($"{nameof(TraitHealEffect)}.healIncrease = {healIncrease} — 배율 < 0");
        CheckNonNegative(errors, nameof(overflowShareRange), overflowShareRange);
        CheckNonNegative(errors, nameof(overflowTransferRatio), overflowTransferRatio);
    }
}

/// <summary>
/// ⑤ 사건 시 특성 채널 보호막 — 수호의 잔광(방어 성공 → 자신 최대 체력 10%, 4초)·불굴의 가호(실제 회복량 75% → 회복한 대상, 상한 최대 체력 10%).
/// 같은 특성 채널은 큰 값 유지 + 시간 갱신, 다른 채널 유지(<see cref="TraitShieldRules"/>). '피해 무시가 끝난 뒤' 같은 지급 시점은 사건을 내는 스킬이 정한다.
/// </summary>
[Serializable]
public sealed class TraitShieldEffect : TraitTriggeredEffect
{
    [SerializeField] private TraitShieldAmountSource amountSource = TraitShieldAmountSource.MaxHpRatio;
    [Tooltip("양 비율 — 최대 체력 비율(0.1) 또는 사건 양(실제 회복량) 비율(0.75).")]
    [Min(0f)]
    [SerializeField] private float amountRatio = 0.1f;
    [Tooltip("생성 상한 = 받는 대상 최대 체력 × 이 비율. 0 = 상한 없음.")]
    [Min(0f)]
    [SerializeField] private float capMaxHpRatio;
    [Tooltip("지속 초.")]
    [Min(0f)]
    [SerializeField] private float duration = 4f;
    [SerializeField] private TraitShieldRecipient recipient = TraitShieldRecipient.Self;

    public TraitShieldEffect() { }

    public TraitShieldEffect(TraitTriggerFilter filter, TraitShieldAmountSource amountSource, float amountRatio, float duration,
        TraitShieldRecipient recipient = TraitShieldRecipient.Self, float capMaxHpRatio = 0f)
    {
        this.filter = filter ?? new TraitTriggerFilter();
        this.amountSource = amountSource;
        this.amountRatio = amountRatio;
        this.duration = duration;
        this.recipient = recipient;
        this.capMaxHpRatio = capMaxHpRatio;
    }

    protected override void OnTriggered(ITraitEffectHost host, int traitHash, in TraitTriggerEvent e)
    {
        Unit target = recipient == TraitShieldRecipient.EventSubject ? e.Subject : null;
        if (recipient == TraitShieldRecipient.EventSubject && target == null)
            return;

        int amount = TraitShieldRules.Amount(amountSource, amountRatio, host.MaxHpOf(target), e.Amount, capMaxHpRatio);
        if (amount > 0)
            host.GrantTraitShield(target, traitHash, amount, duration);
    }

    public override void Validate(List<string> errors)
    {
        base.Validate(errors);
        CheckPositive(errors, nameof(amountRatio), amountRatio);
        CheckNonNegative(errors, nameof(capMaxHpRatio), capMaxHpRatio);
        CheckPositive(errors, nameof(duration), duration);
    }
}

/// <summary>
/// ⑥ 적중 시 밀치기 — 돌파의 방패·수호의 파동·충격 레이저·급소 파괴(+ 제압의 진격 = 뇌진탕 옵션 <c>concussionDuration</c>).
/// 지금은 <see cref="AttackInfo"/> 의 넉백(+경직·뇌진탕) 경로로 건다. 경직은 0 이어도 된다 — 23호도 넉백 세기만으로 취약 밀림이 걸린다(할 일 134).
/// 보스 쪽 거부 규칙(상태별 슈퍼아머)·23호 넉백 입구(Unit.Knockback → OnKnockback)는 134 로 들어왔다 — 직접 걸기로 바꿀 때도 <see cref="ApplyCrowdControl"/> 한 곳만 고친다.
/// 보스 상태를 묻지 않는다 — 걸고, 대상의 기존 거부 규칙에 맡긴다. 시전당 대상별 1회 — 밀치기를 받는 대상에만 기록한다
/// (슈퍼아머로 거부 중이면 허브가 부르지 않는다, <see cref="TraitAttackModifiers.Apply"/>).
/// </summary>
[Serializable]
public sealed class TraitKnockbackEffect : TraitEffect
{
    [Tooltip("이 공격 종류의 원래 공격에만. 비우면 전부.")]
    [SerializeField] private AttackType[] attackTypes = Array.Empty<AttackType>();
    [Tooltip("이 형태에만(급소 파괴 = Enhanced). 비우면 전부.")]
    [SerializeField] private TraitAttackForm[] forms = Array.Empty<TraitAttackForm>();
    [Tooltip("밀치는 거리(m).")]
    [Min(0f)]
    [SerializeField] private float pushDistance = 2f;
    [Tooltip("밀치는 시간(초).")]
    [Min(0f)]
    [SerializeField] private float pushDuration = 0.2f;
    [Tooltip("넉백 끝난 뒤 경직(초). 0 = 경직 없이 밀기만(23호 취약 밀림도 경직 없이 걸린다 — 할 일 134).")]
    [Min(0f)]
    [SerializeField] private float staggerDuration;
    [Tooltip("뇌진탕(초) — 밀치기가 끝나는 순간 이만큼 기절(제압의 진격 = 0.75). 맞을 때마다 재설정, 기존 기절과는 큰 값. 0 = 없음.")]
    [Min(0f)]
    [SerializeField] private float concussionDuration;

    public TraitKnockbackEffect() { }

    public TraitKnockbackEffect(float pushDistance, float pushDuration, float staggerDuration,
        AttackType[] attackTypes = null, TraitAttackForm[] forms = null, float concussionDuration = 0f)
    {
        this.pushDistance = pushDistance;
        this.pushDuration = pushDuration;
        this.staggerDuration = staggerDuration;
        this.attackTypes = attackTypes ?? Array.Empty<AttackType>();
        this.forms = forms ?? Array.Empty<TraitAttackForm>();
        this.concussionDuration = concussionDuration;
    }

    /// <summary>밀기 속도(m/s) = 거리 ÷ 시간. 시간 0 이면 0(넉백 없음).</summary>
    public float PushSpeed => pushDuration > 0f ? pushDistance / pushDuration : 0f;

    /// <summary>뇌진탕 기절 길이(초). 0 = 뇌진탕 없음.</summary>
    public float ConcussionDuration => concussionDuration;

    public bool Matches(TraitAttackKey key) =>
        TraitModifier.Covers(MaskOf(attackTypes), (int)key.Type) && TraitModifier.Covers(MaskOf(forms), (int)key.Form);

    /// <summary>
    /// 🔴 CC 를 실제로 거는 단 한 곳 — 지금은 공격 정보의 넉백·경직 필드만 채운다(맞는 쪽 MonsterBase·23호가 처리).
    /// 이미 더 센 넉백(거리 = 속도 × 시간)이 실려 있으면 그쪽을 두고(벡터 합산 금지 — 명세 §4), 경직은 큰 값을 남긴다(합산 아님).
    /// 방향이 0 이면 그대로 둔다(수신측이 방사형으로 계산).
    /// 뇌진탕 옵션이 있으면 <see cref="AttackInfo.concussionDuration"/> 을 채운다 — 수신측(MonsterBase)이 실제로 밀린 대상에만
    /// <see cref="StatusEffectType.Concussion"/> 을 건다(한 타격 안에서는 큰 값, 타격마다의 재설정은 <see cref="ConcussionState"/>).
    /// </summary>
    public void ApplyCrowdControl(ref AttackInfo info, Vector3 direction)
    {
        if (concussionDuration > 0f)
            info.concussionDuration = Mathf.Max(info.concussionDuration, concussionDuration);

        float existingDistance = info.knockbackStrength * info.knockbackDuration;
        if (pushDistance > existingDistance && PushSpeed > 0f)
        {
            info.knockbackStrength = PushSpeed;
            info.knockbackDuration = pushDuration;
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude > 1e-6f)
                info.knockbackDirection = flat.normalized;
        }

        info.staggerDuration = Mathf.Max(info.staggerDuration, staggerDuration);
    }

    public override ITraitEffectBinding BindServer(ITraitEffectHost host, int traitHash)
    {
        if (host == null)
            return null;

        var modifier = new Modifier(this);
        host.AddAttackModifier(modifier);
        return new TraitActionBinding(() => host.RemoveAttackModifier(modifier));
    }

    public override void Validate(List<string> errors)
    {
        CheckPositive(errors, nameof(pushDistance), pushDistance);
        CheckPositive(errors, nameof(pushDuration), pushDuration);
        CheckNonNegative(errors, nameof(staggerDuration), staggerDuration);
        CheckNonNegative(errors, nameof(concussionDuration), concussionDuration);
    }

    /// <summary>[서버] 등록된 밀치기 1개 — 키가 맞고 이 시전에서 이 대상이 처음일 때만 <see cref="ApplyCrowdControl"/>.</summary>
    public sealed class Modifier : ITraitAttackModifier
    {
        private readonly TraitKnockbackEffect effect;
        private readonly TraitCastTargetGate gate = new TraitCastTargetGate();

        public Modifier(TraitKnockbackEffect effect) => this.effect = effect;

        public void Modify(TraitAttackKey key, int castId, int targetId, Vector3 direction, ref AttackInfo info)
        {
            if (effect.Matches(key) && gate.TryMark(castId, targetId))
                effect.ApplyCrowdControl(ref info, direction);
        }

        public void Reset() => gate.Reset();
    }
}

/// <summary>⑦ 범위·폭 배율 — 광폭 레이저(Q 폭 ×1.3)·칼날의 영역(강타 길이·폭, 변신 E 반경 ×1.25). 스킬이 판정·표시 치수에 곱한다(VFX 크기 연결은 별도 할 일).</summary>
[Serializable]
public sealed class TraitAreaEffect : TraitEffect
{
    [Tooltip("범위 증감 비율(0.3 = 30% 넓게).")]
    [SerializeField] private float areaIncrease = 0.3f;
    [Tooltip("닿는 치수. 비우면 전부.")]
    [SerializeField] private TraitAreaAxis[] axes = { TraitAreaAxis.Width };
    [SerializeField] private AttackType[] attackTypes = Array.Empty<AttackType>();
    [SerializeField] private TraitAttackForm[] forms = Array.Empty<TraitAttackForm>();

    public TraitAreaEffect() { }

    public TraitAreaEffect(float areaIncrease, TraitAreaAxis[] axes = null, AttackType[] attackTypes = null, TraitAttackForm[] forms = null)
    {
        this.areaIncrease = areaIncrease;
        this.axes = axes ?? Array.Empty<TraitAreaAxis>();
        this.attackTypes = attackTypes ?? Array.Empty<AttackType>();
        this.forms = forms ?? Array.Empty<TraitAttackForm>();
    }

    public override void GetModifiers(List<TraitModifier> result) =>
        result.Add(TraitModifier.Area(1f + areaIncrease, MaskOf(axes), MaskOf(attackTypes), MaskOf(forms)));

    public override void Validate(List<string> errors)
    {
        CheckFinite(errors, nameof(areaIncrease), areaIncrease);
        if (1f + areaIncrease <= 0f)
            errors.Add($"{nameof(TraitAreaEffect)}.areaIncrease = {areaIncrease} — 배율 ≤ 0");
    }
}

/// <summary>
/// ⑧ 파생 공격 — 진격의 충격·이중 출력·잔류 포격·추격의 궤적·끝없는 칼날. 서버에 명세(<see cref="TraitDerivedAttack"/>)만 등록하고,
/// 언제·어디서 판정할지는 원래 공격을 가진 스킬(캐릭터 레인)이 <see cref="PlayerTraitEffects.GetDerivedAttacks"/> 로 찾아 정한다.
/// 피해 = 원본 Dref × 비율(명세 §5), 판정 플래그 = <see cref="TraitHitFlags.Derived"/>. VFX 없음(판정만).
/// </summary>
[Serializable]
public sealed class TraitDerivedAttackEffect : TraitEffect
{
    [Tooltip("스킬이 어떤 파생인지 가르는 이름(예: PAL_Q_BLAST). 스킬 코드와 맞춘다.")]
    [SerializeField] private string derivedKey = "";
    [Tooltip("원래 공격 종류. 비우면 전부.")]
    [SerializeField] private AttackType[] sourceAttackTypes = Array.Empty<AttackType>();
    [SerializeField] private TraitAttackForm[] sourceForms = Array.Empty<TraitAttackForm>();
    [Tooltip("원본 기준 피해 대비 비율(0.3 = 30%).")]
    [Min(0f)]
    [SerializeField] private float damageRatio = 0.3f;
    [Tooltip("원래 공격 뒤 지연(초).")]
    [Min(0f)]
    [SerializeField] private float delay;
    [Tooltip("판정 도형(할 일 153). 비우면 원래 공격 치수 재사용(이중 출력·잔류 포격·추격의 궤적·끝없는 칼날). 진격의 충격 = Point(Q 끝 지점 + 마지막 진행 방향) 부채꼴.")]
    [SerializeField] private AttackShapeSet hitArea = new AttackShapeSet();
    // ⚠️ 미사용(할 일 153 4/6 — 판정은 hitArea). GameData.xlsx Traits_Paladin/Gunner/Assassin 시트에 effects[0].radius·forwardOffset 행이 있어
    //    지우면 테이블 모드가 막힌다 — 다음 병합 Export 때 행부터 정리하고 지운다.
    [Tooltip("미사용 — 판정은 hitArea(할 일 153). 시트 행 정리 뒤 지운다.")]
    [Min(0f)]
    [SerializeField] private float radius;
    [Tooltip("미사용 — 판정은 hitArea(할 일 153). 시트 행 정리 뒤 지운다.")]
    [SerializeField] private float forwardOffset;
    [Tooltip("원래 공격 N번마다 1회(이중 출력 3). 1 = 매번.")]
    [Min(1)]
    [SerializeField] private int requiredCount = 1;

    public TraitDerivedAttackEffect() { }

    public TraitDerivedAttackEffect(string derivedKey, float damageRatio, AttackType[] sourceAttackTypes = null,
        TraitAttackForm[] sourceForms = null, float delay = 0f, AttackShapeSet hitArea = null, int requiredCount = 1)
    {
        this.derivedKey = derivedKey;
        this.damageRatio = damageRatio;
        this.sourceAttackTypes = sourceAttackTypes ?? Array.Empty<AttackType>();
        this.sourceForms = sourceForms ?? Array.Empty<TraitAttackForm>();
        this.delay = delay;
        this.hitArea = hitArea ?? new AttackShapeSet();
        this.requiredCount = requiredCount;
    }

    public string DerivedKey => derivedKey;
    public float DamageRatio => damageRatio;
    public AttackShapeSet HitArea => hitArea;

    public TraitDerivedAttack CreateRuntime(int traitHash) =>
        new TraitDerivedAttack(traitHash, derivedKey, MaskOf(sourceAttackTypes), MaskOf(sourceForms),
            damageRatio, delay, hitArea, requiredCount);

    public override ITraitEffectBinding BindServer(ITraitEffectHost host, int traitHash)
    {
        if (host == null)
            return null;

        TraitDerivedAttack attack = CreateRuntime(traitHash);
        host.AddDerivedAttack(attack);
        return new TraitActionBinding(() => host.RemoveDerivedAttack(attack));
    }

    public override void Validate(List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(derivedKey))
            errors.Add($"{nameof(TraitDerivedAttackEffect)}.derivedKey 가 비었다");
        CheckPositive(errors, nameof(damageRatio), damageRatio);
        CheckNonNegative(errors, nameof(delay), delay);
        int before = errors.Count;
        hitArea?.Validate(errors);
        for (int i = before; i < errors.Count; i++)
            errors[i] = $"{nameof(TraitDerivedAttackEffect)}.hitArea.{errors[i]}";
        if (requiredCount < 1)
            errors.Add($"{nameof(TraitDerivedAttackEffect)}.requiredCount = {requiredCount} < 1");
    }
}

/// <summary>
/// ⑨ 다음 공격 1회 강화 — 돌파의 여세(Q 최초 유효 적중 → 4초 안 다음 기본 공격 첫 타 +30%, 강타 포함).
/// 부여 = 거름을 지난 사건(보통 시전당 1회), 소모 = 스킬이 첫 원래 피해 판정에서 <see cref="PlayerTraitEffects.ConsumeNextAttackMultiplierServer"/>.
/// 재부여는 중첩 없이 갱신, 사망·새 회차·특성 되돌리기에 지운다(<see cref="TraitNextAttackBuffs"/>).
/// </summary>
[Serializable]
public sealed class TraitNextAttackBuffEffect : TraitTriggeredEffect
{
    [Tooltip("강화 비율(0.3 = +30%).")]
    [SerializeField] private float damageIncrease = 0.3f;
    [Tooltip("부여 뒤 유지 초.")]
    [Min(0f)]
    [SerializeField] private float buffDuration = 4f;
    [Tooltip("소모하는 공격 종류(기본 공격 = Default). 비우면 전부.")]
    [SerializeField] private AttackType[] consumerAttackTypes = { AttackType.Default };
    [Tooltip("소모하는 형태(강타 포함이면 Normal·Enhanced·Alternate 다). 비우면 전부.")]
    [SerializeField] private TraitAttackForm[] consumerForms = Array.Empty<TraitAttackForm>();

    public TraitNextAttackBuffEffect() { }

    public TraitNextAttackBuffEffect(TraitTriggerFilter filter, float damageIncrease, float buffDuration,
        AttackType[] consumerAttackTypes = null, TraitAttackForm[] consumerForms = null)
    {
        this.filter = filter ?? new TraitTriggerFilter();
        this.damageIncrease = damageIncrease;
        this.buffDuration = buffDuration;
        this.consumerAttackTypes = consumerAttackTypes ?? Array.Empty<AttackType>();
        this.consumerForms = consumerForms ?? Array.Empty<TraitAttackForm>();
    }

    protected override void OnTriggered(ITraitEffectHost host, int traitHash, in TraitTriggerEvent e) =>
        host.NextAttackBuffs.Grant(traitHash, 1f + damageIncrease, host.Now + buffDuration,
            MaskOf(consumerAttackTypes), MaskOf(consumerForms));

    protected override void OnUnbound(ITraitEffectHost host, int traitHash) => host.NextAttackBuffs.Remove(traitHash);

    public override void Validate(List<string> errors)
    {
        base.Validate(errors);
        CheckFinite(errors, nameof(damageIncrease), damageIncrease);
        if (1f + damageIncrease < 0f)
            errors.Add($"{nameof(TraitNextAttackBuffEffect)}.damageIncrease = {damageIncrease} — 배율 < 0");
        CheckPositive(errors, nameof(buffDuration), buffDuration);
    }
}

/// <summary>⑩ 스킬 수치 칸의 값 해석 — 시트·카드에는 기획 명세 그대로의 값(감소율 0.25 등)을 두고, 연산에 넣을 때만 바꾼다.</summary>
public enum TraitSkillValueScale : byte
{
    /// <summary>값 그대로.</summary>
    Raw = 0,
    /// <summary>1 + 값(증가율 0.35 → × 1.35).</summary>
    OnePlus = 1,
    /// <summary>1 − 값(감소율 0.25 → × 0.75). 값은 0 이상 1 미만.</summary>
    OneMinus = 2,
}

/// <summary>
/// ⑩ 스킬 수치 덮어쓰기 — 이름 붙은 스킬 수치에 Set/Add/Multiply(예: 고속 집속 GUN_Q_MAX_CHARGE_TIME × (1 − 0.25), 한계 돌파 과열 증가 × (1 + 0.35)).
/// 스킬은 <see cref="PlayerTraitModifiers.ResolveSkillValue"/>(이름, 데이터 값)으로 읽는다. 이름은 스킬 코드와 데이터가 맞춘다.
/// 시트 Traits_&lt;캐릭터&gt; 행 = <c>effects[i].entries[j].value</c>(숫자 칸만 — key·name·op·scale 은 인스펙터 칸, 할 일 133).
/// 카드 설명 토큰 = 칸의 <see cref="Entry.name"/>(<c>{chargeTimeReduction:%}</c> → 그 칸의 value, 해석 전 값).
/// 캐릭터 전용 수치 칸(옛 GunnerChargeTimeEffect 등 거너 7종·PaladinShieldDashEffect)은 할 일 133 에서 이 효과로 합쳤다.
/// </summary>
[Serializable]
public sealed class TraitSkillValueEffect : TraitEffect
{
    [Serializable]
    public struct Entry
    {
        [Tooltip("카드 설명 토큰 이름({name}) — 비우면 토큰으로 안 쓴다. 정의 안에서 겹치면 안 된다.")]
        public string name;
        [Tooltip("스킬 수치 이름 — 스킬 코드가 묻는 이름과 같아야 한다.")]
        public string key;
        public TraitValueOp op;
        [Tooltip("값 해석 — Raw 그대로, OnePlus = 1 + 값(증가율), OneMinus = 1 − 값(감소율).")]
        public TraitSkillValueScale scale;
        [Tooltip("스킬 수치 값(해석 전 — 증가율·감소율이면 비율 0.25 = 25%). 칸마다 뜻은 key·name 을 볼 것.")]
        public float value;

        public Entry(string key, TraitValueOp op, float value, string name = null, TraitSkillValueScale scale = TraitSkillValueScale.Raw)
        {
            this.name = name;
            this.key = key;
            this.op = op;
            this.scale = scale;
            this.value = value;
        }

        /// <summary>연산에 넣는 값(<see cref="scale"/> 적용).</summary>
        public float Applied => scale == TraitSkillValueScale.OnePlus ? 1f + value
            : scale == TraitSkillValueScale.OneMinus ? 1f - value
            : value;
    }

    [SerializeField] private Entry[] entries = Array.Empty<Entry>();

    public TraitSkillValueEffect() { }

    public TraitSkillValueEffect(params Entry[] entries) => this.entries = entries ?? Array.Empty<Entry>();

    public IReadOnlyList<Entry> Entries => entries ?? Array.Empty<Entry>();

    public override void GetModifiers(List<TraitModifier> result)
    {
        if (entries == null)
            return;

        foreach (Entry entry in entries)
            result.Add(TraitModifier.SkillValue(entry.key, entry.op, entry.Applied));
    }

    public override bool TryGetNamedValue(string name, out object value)
    {
        value = null;
        if (entries == null || string.IsNullOrEmpty(name))
            return false;

        foreach (Entry entry in entries)
        {
            if (string.Equals(entry.name, name, StringComparison.Ordinal))
            {
                value = entry.value;
                return true;
            }
        }

        return false;
    }

    public override void Validate(List<string> errors)
    {
        if (entries == null || entries.Length == 0)
        {
            errors.Add($"{nameof(TraitSkillValueEffect)}.entries 가 비었다");
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < entries.Length; i++)
        {
            Entry entry = entries[i];
            string field = $"entries[{i}].value";
            if (string.IsNullOrWhiteSpace(entry.key))
                errors.Add($"{nameof(TraitSkillValueEffect)}.entries[{i}].key 가 비었다");
            if (!string.IsNullOrEmpty(entry.name) && !names.Add(entry.name))
                errors.Add($"{nameof(TraitSkillValueEffect)}.entries[{i}].name '{entry.name}' 이 겹친다");

            CheckFinite(errors, field, entry.value);
            // 시간·거리·비율·발수뿐이라 Set·Multiply 는 음수가 될 수 없다(Add 는 깎기가 있어 음수 허용). 감소율은 1 미만(배율 > 0).
            if (entry.op != TraitValueOp.Add && entry.Applied < 0f)
                errors.Add($"{nameof(TraitSkillValueEffect)}.{field} = {entry.value} — 연산 값 {entry.Applied} < 0");
            if (entry.scale != TraitSkillValueScale.Raw && entry.value < 0f)
                errors.Add($"{nameof(TraitSkillValueEffect)}.{field} = {entry.value} < 0(증가율·감소율)");
            if (entry.scale == TraitSkillValueScale.OneMinus && entry.value >= 1f)
                errors.Add($"{nameof(TraitSkillValueEffect)}.{field} = {entry.value} ≥ 1(감소율)");
        }
    }
}

/// <summary>PlayerStats 단계(player-stats.md §2).</summary>
public enum TraitStatStage : byte
{
    Base = 0,
    Total = 1,
    /// <summary>가하는 피해%(스탯 종류 무시).</summary>
    DealtDamage = 2,
}

/// <summary>스탯 % 기여 — 옛 공격력·이동속도 배율 종류(1/5 TraitEffectKind)를 대신한다. 0.2 = +20%, −0.2 = −20%(페널티 유지).</summary>
[Serializable]
public sealed class TraitStatEffect : TraitEffect
{
    [SerializeField] private PlayerStatKind stat = PlayerStatKind.AttackDamage;
    [SerializeField] private TraitStatStage stage = TraitStatStage.Total;
    [Tooltip("% 값(0.2 = +20%).")]
    [SerializeField] private float percent = 0.1f;

    public TraitStatEffect() { }

    public TraitStatEffect(PlayerStatKind stat, float percent, TraitStatStage stage = TraitStatStage.Total)
    {
        this.stat = stat;
        this.percent = percent;
        this.stage = stage;
    }

    public override void GetModifiers(List<TraitModifier> result)
    {
        switch (stage)
        {
            case TraitStatStage.Base: result.Add(TraitModifier.StatBase(stat, percent)); break;
            case TraitStatStage.Total: result.Add(TraitModifier.StatTotal(stat, percent)); break;
            case TraitStatStage.DealtDamage: result.Add(TraitModifier.DealtDamage(percent)); break;
        }
    }

    public override void Validate(List<string> errors) => CheckFinite(errors, nameof(percent), percent);
}
