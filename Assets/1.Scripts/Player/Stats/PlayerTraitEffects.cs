using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [서버] 플레이어 1명의 특성 이벤트형 효과 허브(할 일 125 3/5) — <see cref="ITraitEffectHost"/> 런타임 구현. 없으면 <see cref="For"/> 가 붙인다(프리팹 수정 없음).
/// <see cref="TraitDirector"/> 가 획득 때 효과(<see cref="TraitEffect.BindServer"/>)를 여기에 걸고, 효과 장부로 되돌린다.
///
/// 사건: <see cref="Player.ServerAttackLanded"/> → <see cref="TraitTrigger.AttackLanded"/>(시전 ID·형태 포함) 자동.
///       방어 성공·패시브 발동·스킬 정상 종료·기본 공격 발사 커밋은 각 스킬(캐릭터 레인)이 <see cref="RaiseServer"/> 로 알린다.
/// 스킬이 묻는 표면(캐릭터 레인이 연결):
///   ⑥ 밀치기 = 적중 직전 <see cref="ModifyOutgoingAttackServer"/>(공격 정보 넉백·경직 필드만 채운다)
///   ⑨ 다음 공격 강화 = 첫 원래 피해 판정에서 <see cref="ConsumeNextAttackMultiplierServer"/>
///   ⑧ 파생 공격 = <see cref="GetDerivedAttacks"/>(피해는 <see cref="TraitDerivedAttack.DamageFrom"/>, 판정은 <see cref="TraitHitFlags.Derived"/>)
///   ④ 회복 = 특성 회복 경로 <see cref="HealServer"/>(요청 × 회복 배율 → 실제 회복 → 초과 전달 → Healed 사건)
/// 수치형(피해·쿨·범위·스킬 수치·모드 스위치)은 여기가 아니라 <see cref="PlayerTraitModifiers"/>(서버·클라 공통).
/// 사망(살아 있음 → 그 밖)이면 대기 상태(다음 공격 강화·파생 카운터·대상별 1회 기억)를 지운다(명세 §9). 선택한 특성은 유지.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerTraitEffects : MonoBehaviour, ITraitEffectHost
{
    private const string Tag = "[TraitEffects]";

    private Player player;
    private PlayerSkillController skills;
    private PlayerTraitModifiers modifiers;
    private bool subscribed;

    private readonly TraitNextAttackBuffs nextAttackBuffs = new TraitNextAttackBuffs();
    private readonly List<ITraitAttackModifier> attackModifiers = new List<ITraitAttackModifier>();
    private readonly List<TraitDerivedAttack> derivedAttacks = new List<TraitDerivedAttack>();
    private readonly List<TraitOverflowShare> overflowShares = new List<TraitOverflowShare>();
    private readonly List<TraitHealCandidate> healCandidates = new List<TraitHealCandidate>();
    private readonly List<Player> healCandidatePlayers = new List<Player>();

    /// <summary>[서버] 특성 사건.</summary>
    public event Action<TraitTriggerEvent> Triggered;

    /// <summary>프리팹에 있으면 그것, 없으면 붙인다(<see cref="PlayerTraitModifiers.For"/> 와 같은 방식).</summary>
    public static PlayerTraitEffects For(Component owner)
    {
        if (owner == null)
            return null;

        return owner.TryGetComponent(out PlayerTraitEffects effects)
            ? effects
            : owner.gameObject.AddComponent<PlayerTraitEffects>();
    }

    private bool IsServerSide => player != null && player.IsSpawned && player.IsServer;

    private void Awake()
    {
        player = GetComponent<Player>();
        skills = GetComponent<PlayerSkillController>();
        modifiers = PlayerTraitModifiers.For(this);
    }

    private void OnEnable()
    {
        if (player != null && !subscribed)
        {
            player.ServerAttackLanded += HandleAttackLanded;
            PlayerLifeCycleController.ServerLifeStateChanged += HandleLifeStateChanged;
            subscribed = true;
        }
    }

    private void OnDisable()
    {
        if (player != null && subscribed)
        {
            player.ServerAttackLanded -= HandleAttackLanded;
            PlayerLifeCycleController.ServerLifeStateChanged -= HandleLifeStateChanged;
            subscribed = false;
        }
    }

    // ── 사건 ────────────────────────────────────────────────────────────

    /// <summary>[서버] 스킬이 특성 사건을 알린다(방어 성공·패시브 발동·정상 종료·기본 공격 발사). 서버가 아니면 무시.</summary>
    public void RaiseServer(in TraitTriggerEvent e)
    {
        if (!IsServerSide)
            return;

        Triggered?.Invoke(e);
    }

    /// <summary>[서버] 스킬 출처 사건 — 형태(일반/대체)와 시전 ID 를 스킬에서 읽는다.</summary>
    public void RaiseServer(TraitTrigger trigger, PlayerSkillBase skill, int amount = 0, Unit subject = null)
    {
        if (skill == null)
            return;

        RaiseServer(new TraitTriggerEvent(trigger, TraitAttackKey.AttackTypeOf(skill.Slot), FormOf(skill),
            skill.ServerCastId, true, 0, subject, amount));
    }

    private void HandleAttackLanded(PlayerAttackLanded landed)
    {
        TraitAttackForm form = landed.Source is PlayerSkillBase skill ? FormOf(skill) : TraitAttackForm.Normal;
        RaiseServer(new TraitTriggerEvent(TraitTrigger.AttackLanded, landed.AttackType, form, landed.CastId,
            landed.TriggersOnHit, landed.Targets != null ? landed.Targets.Count : 0));
    }

    private TraitAttackForm FormOf(PlayerSkillBase skill) =>
        skills != null && skills.IsAlternateSkill(skill) ? TraitAttackForm.Alternate : TraitAttackForm.Normal;

    private void HandleLifeStateChanged(ulong clientId, PlayerLifeState previous, PlayerLifeState current)
    {
        if (!IsServerSide || clientId != player.OwnerClientId || previous != PlayerLifeState.Alive || current == PlayerLifeState.Alive)
            return;

        ClearTransientServer();
    }

    /// <summary>[서버] 사망·새 회차 — 대기 상태만 지운다(선택한 특성·구독은 유지).</summary>
    public void ClearTransientServer()
    {
        nextAttackBuffs.Clear();
        for (int i = 0; i < derivedAttacks.Count; i++)
            derivedAttacks[i].Counter.Reset();
        for (int i = 0; i < attackModifiers.Count; i++)
        {
            if (attackModifiers[i] is TraitKnockbackEffect.Modifier knockback)
                knockback.Reset();
        }
    }

    // ── 스킬이 묻는 표면 ─────────────────────────────────────────────────

    /// <summary>
    /// [서버] ⑥ 원래 공격 1건이 대상에 들어가기 직전 — 특성 수정자(밀치기)가 공격 정보를 고친다. 파생 공격에는 부르지 않는다(CC 없음).
    /// castId = 그 시전(스킬은 <see cref="PlayerSkillBase.ServerCastId"/>), 대상별 1회는 대상 유닛 단위.
    /// 대상이 지금 밀치기를 거부하면(<see cref="Unit.AcceptsKnockback"/>) 싣지도 기록하지도 않는다 — 같은 시전의 뒤 발이 다시 시도한다.
    /// </summary>
    public void ModifyOutgoingAttackServer(TraitAttackKey key, int castId, Unit target, Vector3 direction, ref AttackInfo info)
    {
        if (!IsServerSide || attackModifiers.Count == 0)
            return;

        int targetId = target != null ? target.GetInstanceID() : 0;
        TraitAttackModifiers.Apply(attackModifiers, key, castId, targetId, target == null || target.AcceptsKnockback, direction, ref info);
    }

    /// <summary>[서버] ⑨ 그 공격 판정에 붙는 다음 공격 강화 배율(없으면 1)을 내고 소모한다. 같은 시전의 다른 대상은 같은 배율.</summary>
    public float ConsumeNextAttackMultiplierServer(TraitAttackKey key, int castId) =>
        IsServerSide ? nextAttackBuffs.Consume(key, castId, Now) : 1f;

    /// <summary>[서버] ⑧ 그 원래 공격에 붙는 파생 공격 명세(등록 순서).</summary>
    public void GetDerivedAttacks(TraitAttackKey key, List<TraitDerivedAttack> result)
    {
        result.Clear();
        for (int i = 0; i < derivedAttacks.Count; i++)
        {
            if (derivedAttacks[i].Matches(key))
                result.Add(derivedAttacks[i]);
        }
    }

    /// <summary>
    /// [서버] ④ 특성 회복 경로(명세 §7) — 요청 H = 원래 회복 × 회복 배율 → 자신 실제 A(<see cref="Unit.HealHp(int,bool,ulong)"/> 반환) →
    /// 초과 O = H − A 를 전달 설정이 있으면 아군 1명에게(실제 B) → 각 실제 회복 &gt; 0 마다 Healed 사건(보호막 효과가 받는다). 전달분은 재전달하지 않는다.
    /// 반환 = 자신 실제 회복량. 서버가 아니면 0.
    /// </summary>
    public int HealServer(int baseAmount)
    {
        if (!IsServerSide || baseAmount <= 0)
            return 0;

        float multiplier = modifiers != null ? modifiers.HealMultiplier : 1f;
        int requested = TraitHealRules.Request(baseAmount, multiplier);
        int actual = player.HealHp(requested);
        if (actual > 0)
            RaiseServer(new TraitTriggerEvent(TraitTrigger.Healed, AttackType.None, TraitAttackForm.Normal, 0, false, 0, player, actual));

        int overflow = TraitHealRules.Overflow(requested, actual);
        if (overflow > 0 && overflowShares.Count > 0)
            ShareOverflow(overflowShares[0], overflow);

        return actual;
    }

    private void ShareOverflow(TraitOverflowShare share, int overflow)
    {
        int transfer = TraitHealRules.Transfer(overflow, share.TransferRatio);
        if (transfer <= 0 || NetworkManager.Singleton == null)
            return;

        healCandidates.Clear();
        healCandidatePlayers.Clear();
        Vector3 origin = transform.position;
        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            Player other = client.PlayerObject != null ? client.PlayerObject.GetComponent<Player>() : null;
            if (other == null || other == player)
                continue;

            bool eligible = TraitHealRules.IsShareRecipientEligible(other.isActiveAndEnabled,
                other.TryGetComponent(out PlayerLifeCycleController life) ? life.State : PlayerLifeState.Alive,
                other.TryGetComponent(out PlayerEncounterLock encounterLock) && encounterLock.IsCinematicHidden);
            healCandidates.Add(new TraitHealCandidate(other.OwnerClientId, other.CurrentHealth, other.MaxHp,
                (other.transform.position - origin).sqrMagnitude, eligible));
            healCandidatePlayers.Add(other);
        }

        int pick = TraitHealRules.PickOverflowRecipient(healCandidates, share.Range);
        if (pick < 0)
            return;

        Player ally = healCandidatePlayers[pick];
        int healed = ally.HealHp(transfer, true, player.OwnerClientId);
        Edit.Log($"{Tag} '{name}' 초과 회복 {overflow} → '{ally.name}' {transfer} 요청, 실제 {healed}.", this);
        if (healed > 0)
            RaiseServer(new TraitTriggerEvent(TraitTrigger.Healed, AttackType.None, TraitAttackForm.Normal, 0, false, 0, ally, healed));
    }

    // ── ITraitEffectHost ────────────────────────────────────────────────

    public double Now => Time.timeAsDouble;

    public float GetCooldownRemaining(PlayerSkillSlot slot) => skills != null ? skills.GetCooldownRemaining(slot) : 0f;

    public void ReduceCooldown(PlayerSkillSlot slot, float seconds)
    {
        if (skills != null)
            skills.ReduceCooldownServer(slot, seconds);
    }

    public int MaxHpOf(Unit target)
    {
        Unit unit = target != null ? target : player;
        return unit != null ? unit.MaxHp : 0;
    }

    public void GrantTraitShield(Unit target, int traitHash, int amount, float duration)
    {
        Unit unit = target != null ? target : player;
        if (unit == null || amount <= 0)
            return;

        ulong source = TraitShieldRules.ChannelSource(traitHash);
        int existing = 0;
        for (int i = 0; i < unit.ShieldInstanceCount; i++)
        {
            ShieldInstance shield = unit.GetShieldInstance(i);
            if (shield.type == ShieldType.Trait && shield.sourceId == source)
                existing += shield.amount;
        }

        // 같은 특성 채널 = 큰 값 유지 + 지속시간 새로(Replace 규칙이 같은 종류·출처를 갈아 끼운다). 다른 채널은 그대로.
        unit.AddShield(ShieldType.Trait, source, TraitShieldRules.Merge(existing, amount), duration);
    }

    public TraitNextAttackBuffs NextAttackBuffs => nextAttackBuffs;

    public void AddAttackModifier(ITraitAttackModifier modifier)
    {
        if (modifier != null && !attackModifiers.Contains(modifier))
            attackModifiers.Add(modifier);
    }

    public void RemoveAttackModifier(ITraitAttackModifier modifier) => attackModifiers.Remove(modifier);

    public void AddDerivedAttack(TraitDerivedAttack attack)
    {
        if (attack != null && !derivedAttacks.Contains(attack))
            derivedAttacks.Add(attack);
    }

    public void RemoveDerivedAttack(TraitDerivedAttack attack) => derivedAttacks.Remove(attack);

    public void AddOverflowShare(TraitOverflowShare share)
    {
        if (share != null && !overflowShares.Contains(share))
            overflowShares.Add(share);
    }

    public void RemoveOverflowShare(TraitOverflowShare share) => overflowShares.Remove(share);
}
