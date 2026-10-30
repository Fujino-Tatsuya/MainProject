using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 특성 효과 1개(할 일 125 3/5) — 특성 SO(<see cref="TraitDefinition"/>) = 효과 조합 + 수치. 정의가 <c>[SerializeReference]</c> 목록으로 직렬화한다.
/// 종류는 재사용 효과 클래스 약 10개(<c>TraitEffects.cs</c>):
///   수치형(①②④⑦⑩·스탯) = <see cref="GetModifiers"/> 로 <see cref="ITraitModifierContributor"/> 기여 — 서버·클라가 같은 값(<see cref="PlayerTraitModifiers"/>).
///   이벤트형(③⑤⑥⑧⑨) = [서버] <see cref="BindServer"/> 로 플레이어 특성 허브(<see cref="ITraitEffectHost"/>)에 구독·등록하고 되돌리기 핸들을 낸다.
/// 되돌리기·중복 실행 방지는 <see cref="TraitDirector"/> 의 효과 장부(<see cref="TraitEffectLedger{THandle}"/>)가 특성 해시 단위로 맡는다.
/// 모드 변경 7개는 효과 클래스가 아니라 스킬 코드 분기 — 스킬이 <see cref="PlayerTraitModifiers.HasTrait"/>(<see cref="TraitModeIds"/>)로 묻는다.
/// </summary>
[Serializable]
public abstract class TraitEffect
{
    /// <summary>수치 기여(서버·클라 공통, SO 값만 읽는다). 이벤트형은 보통 없다.</summary>
    public virtual void GetModifiers(List<TraitModifier> result) { }

    /// <summary>
    /// [서버] 이벤트형 효과를 건다 — 구독·등록 뒤 되돌리기 핸들(<see cref="ITraitEffectBinding.Unbind"/>)을 낸다. 수치형은 null.
    /// 특성 해시는 보호막 채널·강화 상태의 출처 키로 쓴다.
    /// </summary>
    public virtual ITraitEffectBinding BindServer(ITraitEffectHost host, int traitHash) => null;

    /// <summary>획득 때 한 번만 실행할 일이 있는가(장부가 판·플레이어·특성·효과 칸 단위로 한 번만 부른다 — 재선택·재접속에서 다시 안 함).</summary>
    public virtual bool HasOnceAction => false;

    /// <summary>[서버] 획득 1회 동작. <see cref="HasOnceAction"/> 이 참일 때만 불린다.</summary>
    public virtual void RunOnceServer(ITraitEffectHost host, int traitHash) { }

    /// <summary>그 슬롯의 쿨에 관여하는가(전투 HUD 스킬 강화 칸 — <see cref="TraitProgressHUD"/>).</summary>
    public virtual bool AffectsCooldownOf(PlayerSkillSlot slot) => false;

    /// <summary>
    /// 카드 설명 토큰 <c>{이름}</c> 을 필드가 아닌 이름 붙은 칸으로 푼다(⑩ 스킬 수치 칸의 <c>name</c>, 할 일 133). 없으면 false.
    /// <see cref="TraitTooltipValueSource"/> 가 효과 필드 이름과 같은 단계에서 묻는다(정의 안에서 둘 이상 = 오류).
    /// </summary>
    public virtual bool TryGetNamedValue(string name, out object value)
    {
        value = null;
        return false;
    }

    /// <summary>데이터 검증(명세 §9 — 음수 시간·거리, 발수 &lt; 1, NaN·무한). 오류 문장을 덧붙인다.</summary>
    public virtual void Validate(List<string> errors) { }

    public override string ToString() => GetType().Name;

    // ── 공용 도우미 ─────────────────────────────────────────────────────

    protected static int MaskOf(AttackType[] types)
    {
        int mask = 0;
        if (types != null)
            foreach (AttackType t in types) mask |= TraitModifier.MaskOf(t);
        return mask;
    }

    protected static int MaskOf(TraitAttackForm[] forms)
    {
        int mask = 0;
        if (forms != null)
            foreach (TraitAttackForm f in forms) mask |= TraitModifier.MaskOf(f);
        return mask;
    }

    protected static int MaskOf(PlayerSkillSlot[] slots)
    {
        int mask = 0;
        if (slots != null)
            foreach (PlayerSkillSlot s in slots) mask |= TraitModifier.MaskOf(s);
        return mask;
    }

    protected static int MaskOf(TraitAreaAxis[] axes)
    {
        int mask = 0;
        if (axes != null)
            foreach (TraitAreaAxis a in axes) mask |= TraitModifier.MaskOf(a);
        return mask;
    }

    protected void CheckFinite(List<string> errors, string field, float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            errors.Add($"{GetType().Name}.{field} = {value} — NaN·무한");
    }

    protected void CheckNonNegative(List<string> errors, string field, float value)
    {
        CheckFinite(errors, field, value);
        if (value < 0f)
            errors.Add($"{GetType().Name}.{field} = {value} < 0");
    }

    protected void CheckPositive(List<string> errors, string field, float value)
    {
        CheckFinite(errors, field, value);
        if (value <= 0f)
            errors.Add($"{GetType().Name}.{field} = {value} ≤ 0");
    }
}

/// <summary>[서버] 이벤트형 효과가 건 것(구독·등록·상태)의 되돌리기 핸들 — 장부가 특성 해시 단위로 들고 있다가 초기화·이탈·디스폰 때 부른다.</summary>
public interface ITraitEffectBinding
{
    void Unbind();
}

/// <summary>
/// [서버] 이벤트형 효과가 플레이어에게 요구하는 창구. 런타임 구현 = <see cref="PlayerTraitEffects"/>(플레이어 1명당 1개), 테스트 = 가짜 허브.
/// 스킬 연결(캐릭터 레인)은 <see cref="PlayerTraitEffects"/> 의 질의 API(밀치기·다음 공격 강화·파생 공격)를 부른다.
/// </summary>
public interface ITraitEffectHost
{
    /// <summary>서버 시각(초) — 간격·지속시간 기준.</summary>
    double Now { get; }

    /// <summary>특성 사건(<see cref="TraitTrigger"/>).</summary>
    event Action<TraitTriggerEvent> Triggered;

    float GetCooldownRemaining(PlayerSkillSlot slot);
    void ReduceCooldown(PlayerSkillSlot slot, float seconds);

    /// <summary>대상 최대 체력(null = 이 플레이어).</summary>
    int MaxHpOf(Unit target);

    /// <summary>특성 채널 보호막(<see cref="TraitShieldRules"/>) — 같은 특성 채널은 큰 값 유지·지속시간 갱신. target null = 이 플레이어.</summary>
    void GrantTraitShield(Unit target, int traitHash, int amount, float duration);

    /// <summary>⑨ 다음 공격 1회 강화 상태(이 플레이어).</summary>
    TraitNextAttackBuffs NextAttackBuffs { get; }

    /// <summary>⑥ 나가는 공격 정보 수정자(밀치기). 스킬이 적중 직전 <see cref="PlayerTraitEffects.ModifyOutgoingAttackServer"/> 로 거친다.</summary>
    void AddAttackModifier(ITraitAttackModifier modifier);
    void RemoveAttackModifier(ITraitAttackModifier modifier);

    /// <summary>⑧ 파생 공격 명세. 스킬이 <see cref="PlayerTraitEffects.GetDerivedAttacks"/> 로 묻는다.</summary>
    void AddDerivedAttack(TraitDerivedAttack attack);
    void RemoveDerivedAttack(TraitDerivedAttack attack);

    /// <summary>④ 초과 회복 전달(나누는 불굴). 특성 회복 경로가 쓴다.</summary>
    void AddOverflowShare(TraitOverflowShare share);
    void RemoveOverflowShare(TraitOverflowShare share);
}

/// <summary>⑥ 나가는 원래 공격 1건의 공격 정보를 고친다(밀치기). targetId = 대상별 1회 키(유닛 하나 = 한 값).</summary>
public interface ITraitAttackModifier
{
    void Modify(TraitAttackKey key, int castId, int targetId, Vector3 direction, ref AttackInfo info);
}

/// <summary>⑥ 수정자 목록을 한 대상에 거친다 — 서버 허브(<see cref="PlayerTraitEffects.ModifyOutgoingAttackServer"/>)와 테스트가 같은 경로를 쓴다.</summary>
public static class TraitAttackModifiers
{
    /// <summary>
    /// 대상이 지금 밀치기를 거부하면(<see cref="Unit.AcceptsKnockback"/> false — 보스 슈퍼아머 등) 아무 수정자도 부르지 않는다 —
    /// '시전당 대상별 1회' 기록이 남지 않아 같은 시전의 뒤 발이 다시 시도할 수 있다(할 일 133). 지금 수정자는 밀치기뿐이다.
    /// </summary>
    public static void Apply(IReadOnlyList<ITraitAttackModifier> modifiers, TraitAttackKey key, int castId, int targetId,
        bool targetAcceptsKnockback, Vector3 direction, ref AttackInfo info)
    {
        if (modifiers == null || !targetAcceptsKnockback)
            return;

        for (int i = 0; i < modifiers.Count; i++)
            modifiers[i].Modify(key, castId, targetId, direction, ref info);
    }
}

/// <summary>
/// 이벤트형 효과의 발동 조건 — 사건 종류 + 공격 키 거름 + '시전당 1회'·'간격'(<see cref="TraitProcGate"/>).
/// 빈 배열 = 전부. requireTriggersOnHit = "적중 시 발동" 공격만(평타 스텝·스킬 데이터의 TriggersOnHit).
/// </summary>
[Serializable]
public sealed class TraitTriggerFilter
{
    [SerializeField] private TraitTrigger trigger = TraitTrigger.AttackLanded;
    [Tooltip("이 공격 종류의 사건만. 비우면 전부.")]
    [SerializeField] private AttackType[] attackTypes = Array.Empty<AttackType>();
    [Tooltip("이 형태(일반·대체/변신·강화/강타)의 사건만. 비우면 전부.")]
    [SerializeField] private TraitAttackForm[] forms = Array.Empty<TraitAttackForm>();
    [Tooltip("'적중 시 발동' 공격만 센다(평타 스텝·스킬 데이터 TriggersOnHit).")]
    [SerializeField] private bool requireTriggersOnHit;
    [Tooltip("같은 시전(CastId)은 한 번만 — 여러 대상·여러 틱을 한 번으로.")]
    [SerializeField] private bool oncePerCast = true;
    [Tooltip("마지막 발동 뒤 이 초 안에는 다시 발동하지 않는다(0 = 간격 없음).")]
    [Min(0f)]
    [SerializeField] private float minInterval;

    public TraitTriggerFilter() { }

    public TraitTriggerFilter(TraitTrigger trigger, AttackType[] attackTypes = null, TraitAttackForm[] forms = null,
        bool oncePerCast = true, float minInterval = 0f, bool requireTriggersOnHit = false)
    {
        this.trigger = trigger;
        this.attackTypes = attackTypes ?? Array.Empty<AttackType>();
        this.forms = forms ?? Array.Empty<TraitAttackForm>();
        this.oncePerCast = oncePerCast;
        this.minInterval = minInterval;
        this.requireTriggersOnHit = requireTriggersOnHit;
    }

    public TraitTrigger Trigger => trigger;
    public bool OncePerCast => oncePerCast;
    public float MinInterval => minInterval;

    /// <summary>사건 종류·공격 키·적중 시 발동 조건(시전·간격 거름 전).</summary>
    public bool Matches(in TraitTriggerEvent e)
    {
        if (e.Trigger != trigger)
            return false;
        if (requireTriggersOnHit && !e.TriggersOnHit)
            return false;

        int typeMask = 0;
        if (attackTypes != null)
            foreach (AttackType t in attackTypes) typeMask |= TraitModifier.MaskOf(t);
        int formMask = 0;
        if (forms != null)
            foreach (TraitAttackForm f in forms) formMask |= TraitModifier.MaskOf(f);

        return TraitModifier.Covers(typeMask, (int)e.AttackType) && TraitModifier.Covers(formMask, (int)e.Form);
    }

    public TraitProcGate CreateGate() => new TraitProcGate(oncePerCast, minInterval);

    public void Validate(List<string> errors, string owner)
    {
        if (float.IsNaN(minInterval) || float.IsInfinity(minInterval) || minInterval < 0f)
            errors.Add($"{owner}.filter.minInterval = {minInterval} — 0 이상 유한값이어야 한다");
    }
}

/// <summary>
/// 사건에 반응하는 효과(③⑤⑨)의 공통 뼈대 — [서버] 허브 사건을 구독하고, 거름(<see cref="TraitTriggerFilter"/>)·시전/간격 거름을 지나면 <see cref="OnTriggered"/>.
/// 되돌리기 = 구독 해제 + <see cref="OnUnbound"/>(효과가 건 상태 정리).
/// </summary>
[Serializable]
public abstract class TraitTriggeredEffect : TraitEffect
{
    [SerializeField] protected TraitTriggerFilter filter = new TraitTriggerFilter();

    public TraitTriggerFilter Filter => filter;

    public override ITraitEffectBinding BindServer(ITraitEffectHost host, int traitHash) =>
        host != null ? new Subscription(this, host, traitHash) : null;

    /// <summary>[서버] 거름을 지난 사건 1건.</summary>
    protected abstract void OnTriggered(ITraitEffectHost host, int traitHash, in TraitTriggerEvent e);

    /// <summary>[서버] 되돌리기 — 이 효과가 남긴 상태를 지운다(기본: 없음).</summary>
    protected virtual void OnUnbound(ITraitEffectHost host, int traitHash) { }

    public override void Validate(List<string> errors)
    {
        if (filter == null)
            errors.Add($"{GetType().Name}.filter 가 비었다");
        else
            filter.Validate(errors, GetType().Name);
    }

    private sealed class Subscription : ITraitEffectBinding
    {
        private readonly TraitTriggeredEffect effect;
        private readonly ITraitEffectHost host;
        private readonly int traitHash;
        private readonly TraitProcGate gate;
        private bool bound;

        public Subscription(TraitTriggeredEffect effect, ITraitEffectHost host, int traitHash)
        {
            this.effect = effect;
            this.host = host;
            this.traitHash = traitHash;
            gate = (effect.filter ?? new TraitTriggerFilter()).CreateGate();
            host.Triggered += Handle;
            bound = true;
        }

        private void Handle(TraitTriggerEvent e)
        {
            if (!bound || effect.filter == null || !effect.filter.Matches(e) || !gate.TryPass(e.CastId, host.Now))
                return;

            effect.OnTriggered(host, traitHash, e);
        }

        public void Unbind()
        {
            if (!bound)
                return;

            bound = false;
            host.Triggered -= Handle;
            effect.OnUnbound(host, traitHash);
        }
    }
}

/// <summary>여러 핸들을 한 번에 되돌린다(등록 + 구독을 같이 거는 효과용).</summary>
public sealed class TraitBindingGroup : ITraitEffectBinding
{
    private readonly ITraitEffectBinding[] bindings;

    public TraitBindingGroup(params ITraitEffectBinding[] bindings) => this.bindings = bindings ?? Array.Empty<ITraitEffectBinding>();

    public void Unbind()
    {
        for (int i = 0; i < bindings.Length; i++)
            bindings[i]?.Unbind();
    }
}

/// <summary>등록·해제 한 쌍을 되돌리기 핸들로.</summary>
public sealed class TraitActionBinding : ITraitEffectBinding
{
    private Action unbind;

    public TraitActionBinding(Action unbind) => this.unbind = unbind;

    public void Unbind()
    {
        Action action = unbind;
        unbind = null;
        action?.Invoke();
    }
}

/// <summary>
/// ⑧ 파생 공격 1개의 런타임 명세(서버) — 효과 SO 값 + 'N번마다' 카운터. 스킬(캐릭터 레인)이 원래 공격 키로 찾아
/// <see cref="TraitDerivedAttackRules.Damage"/> 로 피해를 내고, 판정은 <see cref="TraitHitFlags.Derived"/>(특성·자원·CC·간파 없음,
/// <see cref="Player.RaiseServerAttackLanded"/> 금지)로 한다. VFX 는 이번 범위 밖(판정만).
/// </summary>
public sealed class TraitDerivedAttack
{
    public readonly int TraitHash;
    /// <summary>스킬이 어떤 파생인지 가르는 이름(예: PAL_Q_BLAST·GUN_EXTRA_BASIC). 스킬과 데이터가 맞춘다.</summary>
    public readonly string Key;
    public readonly int SourceAttackTypeMask;
    public readonly int SourceFormMask;
    public readonly float DamageRatio;
    public readonly float Delay;
    /// <summary>판정 도형(할 일 153, 효과 SO 의 칸 그대로 — 고치지 말 것). 비었으면 원래 공격 치수 재사용(스킬이 정한다).</summary>
    public readonly AttackShapeSet HitArea;
    public readonly TraitShotCounter Counter;

    public TraitDerivedAttack(int traitHash, string key, int sourceAttackTypeMask, int sourceFormMask,
        float damageRatio, float delay, AttackShapeSet hitArea, int requiredCount)
    {
        HitArea = hitArea ?? new AttackShapeSet();
        TraitHash = traitHash;
        Key = key ?? string.Empty;
        SourceAttackTypeMask = sourceAttackTypeMask;
        SourceFormMask = sourceFormMask;
        DamageRatio = damageRatio;
        Delay = delay;
        Counter = new TraitShotCounter(requiredCount);
    }

    public bool Matches(TraitAttackKey key) =>
        TraitModifier.Covers(SourceAttackTypeMask, (int)key.Type) && TraitModifier.Covers(SourceFormMask, (int)key.Form);

    /// <summary>원본 기준 피해 → 이 파생의 피해.</summary>
    public int DamageFrom(int sourceDref) => TraitDerivedAttackRules.Damage(sourceDref, DamageRatio);
}

/// <summary>④ 초과 회복 전달 설정(서버). 하나만 쓴다(같은 특성 중복 없음).</summary>
public sealed class TraitOverflowShare
{
    public readonly int TraitHash;
    public readonly float Range;
    public readonly float TransferRatio;

    public TraitOverflowShare(int traitHash, float range, float transferRatio)
    {
        TraitHash = traitHash;
        Range = range;
        TransferRatio = transferRatio;
    }
}

/// <summary>
/// 모드 변경 특성 7개의 ID(계획 §4) — 효과 클래스 없이 스킬 코드 안 전용 분기가 <see cref="PlayerTraitModifiers.HasTrait"/> 로 묻는다.
/// 분기 구현은 캐릭터 레인(126~128).
/// </summary>
public static class TraitModeIds
{
    /// <summary>돌파의 방패 — 팔라딘 Q 단발 돌진.</summary>
    public const string PaladinShieldDash = "PAL_2_DASH";
    /// <summary>집속 연사 — 거너 Q 5연사.</summary>
    public const string GunnerVolley = "GUN_3_VOLLEY";
    /// <summary>그림자 회귀 — 어쌔신 Q 왕복.</summary>
    public const string AssassinShadowReturn = "ASN_2_RETURN";
    /// <summary>한계 돌파 — 거너 과열 단계 피해표 교체.</summary>
    public const string GunnerOverdrive = "GUN_2_OVERDRIVE";
    /// <summary>축열 포격 — 거너 Q 과열 소모.</summary>
    public const string GunnerHeatShot = "GUN_2_HEAT_SHOT";
    /// <summary>정온 운용 — 거너 과열 제거.</summary>
    public const string GunnerCoolMode = "GUN_2_COOL_MODE";
    /// <summary>절제된 분노 — 어쌔신 R·변신 제거.</summary>
    public const string AssassinRestraint = "ASN_2_RESTRAINT";

    public static readonly string[] All =
    {
        PaladinShieldDash, GunnerVolley, AssassinShadowReturn, GunnerOverdrive, GunnerHeatShot, GunnerCoolMode, AssassinRestraint,
    };
}
