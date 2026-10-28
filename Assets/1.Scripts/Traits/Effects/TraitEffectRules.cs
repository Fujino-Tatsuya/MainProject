using System;
using System.Collections.Generic;

// 특성 효과 클래스(할 일 125 3/5)가 쓰는 순수 규칙 — Unity·NGO 비의존, EditMode 테스트 대상.
// 원본 명세: Re_C_특성_개발명세_20261016 §3(판정 이벤트·중복 방지)·§5(피해)·§6(쿨)·§7(회복·보호막)·§9(정리).

/// <summary>
/// 특성이 반응하는 서버 사건. 값은 끝에만 추가한다(데이터에 정수로 저장된다).
/// AttackLanded 는 <see cref="Player.ServerAttackLanded"/> 에서 자동으로 오고, 나머지는 각 스킬(캐릭터 레인)이
/// <see cref="PlayerTraitEffects.RaiseServer"/> 로 알린다. Healed 는 특성 회복 경로(<see cref="PlayerTraitEffects.HealServer"/>)가 낸다.
/// </summary>
public enum TraitTrigger : byte
{
    /// <summary>원래 공격 판정 1회가 적을 맞혔다(파생 공격은 발행하지 않는다).</summary>
    AttackLanded = 0,
    /// <summary>방어 성공(팔라딘 E). 시전 ID = 그 방어 시전.</summary>
    GuardSucceeded = 1,
    /// <summary>패시브 발동(팔라딘 불굴의 의지 등).</summary>
    PassiveProc = 2,
    /// <summary>스킬이 예정한 원래 공격을 정상 종료했다(강제 중단·사망은 아님).</summary>
    SkillCompleted = 3,
    /// <summary>특성 회복 경로의 실제 회복 확정(Subject = 회복한 대상, Amount = 실제 회복량 &gt; 0).</summary>
    Healed = 4,
    /// <summary>기본 공격 발사 커밋(빗나가도). 이중 출력 등 발수 카운트용.</summary>
    BasicAttackFired = 5,
}

/// <summary>[서버] 특성 사건 1건. 대상 목록은 싣지 않는다(발행자 버퍼 수명 문제) — 수만 싣는다.</summary>
public readonly struct TraitTriggerEvent
{
    public readonly TraitTrigger Trigger;
    public readonly AttackType AttackType;
    public readonly TraitAttackForm Form;
    /// <summary>같은 시전의 여러 판정을 묶는 값(<see cref="PlayerAttackLanded.CastId"/>). 0 = 모름(매번 다른 시전으로 본다).</summary>
    public readonly int CastId;
    public readonly bool TriggersOnHit;
    public readonly int TargetCount;
    /// <summary>사건의 주체(Healed = 회복한 대상). 없으면 null.</summary>
    public readonly Unit Subject;
    /// <summary>사건의 양(Healed = 실제 회복량). 없으면 0.</summary>
    public readonly int Amount;

    public TraitTriggerEvent(TraitTrigger trigger, AttackType attackType, TraitAttackForm form, int castId,
        bool triggersOnHit = true, int targetCount = 0, Unit subject = null, int amount = 0)
    {
        Trigger = trigger;
        AttackType = attackType;
        Form = form;
        CastId = castId;
        TriggersOnHit = triggersOnHit;
        TargetCount = targetCount;
        Subject = subject;
        Amount = amount;
    }

    public TraitAttackKey Key => new TraitAttackKey(AttackType, Form);

    public override string ToString() => $"{Trigger} {AttackType}/{Form} cast {CastId} targets {TargetCount} amount {Amount}";
}

/// <summary>
/// 특성 효과 1개의 발동 거름(명세 §3) — '시전당 1회'(같은 CastId 두 번째부터 거부)·'간격'(마지막 발동 뒤 N초 안 거부).
/// 둘 다 켜면 '동작당 1회 + 간격'(사냥의 박자). CastId 0 은 매번 새 시전으로 본다.
/// 기억하는 시전 수는 최근 <see cref="RememberedCasts"/>개 — 늦게 도착한 투사체가 오래된 시전을 다시 발동시키지 않을 만큼.
/// </summary>
public sealed class TraitProcGate
{
    public const int RememberedCasts = 32;
    private const double IntervalTolerance = 1e-6;

    private readonly bool oncePerCast;
    private readonly float minInterval;
    private readonly int[] passedCasts = new int[RememberedCasts];
    private int passedCount;
    private int passedNext;
    private double lastProcTime;
    private bool hasProc;

    public TraitProcGate(bool oncePerCast, float minInterval)
    {
        this.oncePerCast = oncePerCast;
        this.minInterval = float.IsNaN(minInterval) ? 0f : Math.Max(0f, minInterval);
    }

    public bool OncePerCast => oncePerCast;
    public float MinInterval => minInterval;

    /// <summary>통과면 기록하고 true. 거부면 아무것도 바꾸지 않고 false.</summary>
    public bool TryPass(int castId, double now)
    {
        if (oncePerCast && castId != 0 && WasPassed(castId))
            return false;
        // 간격은 float 데이터라(0.8f = 0.80000001) 경계에서 1µs 허용 — 정확히 간격만큼 지난 적중은 통과한다.
        if (minInterval > 0f && hasProc && now + IntervalTolerance < lastProcTime + minInterval)
            return false;

        if (castId != 0)
        {
            passedCasts[passedNext] = castId;
            passedNext = (passedNext + 1) % RememberedCasts;
            passedCount = Math.Min(passedCount + 1, RememberedCasts);
        }

        lastProcTime = now;
        hasProc = true;
        return true;
    }

    /// <summary>사망·새 회차 — 기억을 비운다.</summary>
    public void Reset()
    {
        passedCount = 0;
        passedNext = 0;
        hasProc = false;
    }

    private bool WasPassed(int castId)
    {
        for (int i = 0; i < passedCount; i++)
        {
            if (passedCasts[i] == castId)
                return true;
        }

        return false;
    }
}

/// <summary>
/// '시전당 대상별 1회'(명세 §3 대상별 1회 — 여러 콜라이더는 대상 ID 하나로) 거름. 최근 <see cref="TraitProcGate.RememberedCasts"/>개 시전만 기억한다.
/// </summary>
public sealed class TraitCastTargetGate
{
    private readonly Dictionary<int, HashSet<int>> targetsByCast = new Dictionary<int, HashSet<int>>();
    private readonly Queue<int> castOrder = new Queue<int>();

    /// <summary>그 시전에서 그 대상이 처음이면 기록하고 true. CastId 0 은 거르지 않는다(항상 true).</summary>
    public bool TryMark(int castId, int targetId)
    {
        if (castId == 0)
            return true;

        if (!targetsByCast.TryGetValue(castId, out HashSet<int> targets))
        {
            targets = new HashSet<int>();
            targetsByCast.Add(castId, targets);
            castOrder.Enqueue(castId);
            while (castOrder.Count > TraitProcGate.RememberedCasts)
                targetsByCast.Remove(castOrder.Dequeue());
        }

        return targets.Add(targetId);
    }

    public void Reset()
    {
        targetsByCast.Clear();
        castOrder.Clear();
    }
}

/// <summary>③ 이벤트 시 남은 쿨 N초 차감의 대상 고르기.</summary>
public enum TraitRefundTarget : byte
{
    /// <summary>나열한 슬롯 각각(사냥의 박자 Q·E 각각).</summary>
    EachSlot = 0,
    /// <summary>나열한 슬롯 중 남은 시간이 가장 긴 하나(불굴의 순환). 동률이면 먼저 나열한 슬롯.</summary>
    LongestRemaining = 1,
}

/// <summary>③ 쿨 차감 규칙(명세 §6) — 0 아래로 내려가지 않고, 준비된(남은 0) 스킬에는 저장하지 않는다.</summary>
public static class TraitCooldownRefundRules
{
    public static float Reduce(float remaining, float seconds) => Math.Max(0f, remaining - Math.Max(0f, seconds));

    /// <summary>차감할 슬롯을 <paramref name="result"/> 에 채운다(남은 시간 &gt; 0 인 것만).</summary>
    public static void PickSlots(TraitRefundTarget mode, IReadOnlyList<PlayerSkillSlot> slots,
        Func<PlayerSkillSlot, float> remaining, List<PlayerSkillSlot> result)
    {
        result.Clear();
        if (slots == null || remaining == null)
            return;

        if (mode == TraitRefundTarget.EachSlot)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (remaining(slots[i]) > 0f && !result.Contains(slots[i]))
                    result.Add(slots[i]);
            }

            return;
        }

        int best = -1;
        float bestRemaining = 0f;
        for (int i = 0; i < slots.Count; i++)
        {
            float r = remaining(slots[i]);
            if (r > bestRemaining) // 동률은 앞 슬롯 유지(Q 우선)
            {
                best = i;
                bestRemaining = r;
            }
        }

        if (best >= 0)
            result.Add(slots[best]);
    }
}

/// <summary>④ 회복 대상 후보 1명(나누는 불굴). Id = 서버 안정 ID(동률 마지막 기준).</summary>
public readonly struct TraitHealCandidate
{
    public readonly ulong Id;
    public readonly int Hp;
    public readonly int MaxHp;
    public readonly float DistanceSq;
    /// <summary>생존·활성·같은 전투 공간·플레이어(소환물·기믹·쓰러짐 제외)를 호출자가 판정한 결과.</summary>
    public readonly bool Eligible;

    public TraitHealCandidate(ulong id, int hp, int maxHp, float distanceSq, bool eligible = true)
    {
        Id = id;
        Hp = hp;
        MaxHp = maxHp;
        DistanceSq = distanceSq;
        Eligible = eligible;
    }
}

/// <summary>④ 회복 규칙(명세 §7) — 요청 H → 실제 A(<see cref="Unit.HealHp(int)"/> 반환) → 초과 O = H − A → 아군 1명.</summary>
public static class TraitHealRules
{
    /// <summary>회복 요청량 = 원래 회복 × 배율, 마지막에 한 번 반올림(0.5 올림). 0 미만은 0.</summary>
    public static int Request(int baseAmount, float multiplier) =>
        RoundHalfUp(Math.Max(0, baseAmount) * Math.Max(0f, multiplier));

    public static int Overflow(int requested, int actual) => Math.Max(0, requested - Math.Max(0, actual));

    /// <summary>전달량 = 초과 × 전달 비율(반올림).</summary>
    public static int Transfer(int overflow, float ratio) => RoundHalfUp(Math.Max(0, overflow) * Math.Max(0f, ratio));

    /// <summary>
    /// 초과 회복 받을 아군 — 자격·범위 안·체력이 최대 미만 중 HP/MaxHP 최소, 동률은 잃은 체력 큰 순, 그다음 Id 작은 순. 없으면 -1.
    /// </summary>
    public static int PickOverflowRecipient(IReadOnlyList<TraitHealCandidate> candidates, float range)
    {
        if (candidates == null || range <= 0f)
            return -1;

        float rangeSq = range * range;
        int best = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            TraitHealCandidate c = candidates[i];
            if (!c.Eligible || c.MaxHp <= 0 || c.Hp <= 0 || c.Hp >= c.MaxHp || c.DistanceSq > rangeSq)
                continue;

            if (best < 0 || IsBetter(c, candidates[best]))
                best = i;
        }

        return best;
    }

    private static bool IsBetter(TraitHealCandidate a, TraitHealCandidate b)
    {
        // 비율 비교는 곱으로(부동소수 오차 없이): a.Hp/a.Max < b.Hp/b.Max
        long left = (long)a.Hp * b.MaxHp;
        long right = (long)b.Hp * a.MaxHp;
        if (left != right)
            return left < right;

        int lostA = a.MaxHp - a.Hp;
        int lostB = b.MaxHp - b.Hp;
        if (lostA != lostB)
            return lostA > lostB;

        return a.Id < b.Id;
    }

    /// <summary>명세 §5 정수화 — 마지막에 한 번 반올림(0.5 올림).</summary>
    public static int RoundHalfUp(double value)
    {
        if (double.IsNaN(value) || value <= 0d)
            return 0;

        return value >= int.MaxValue ? int.MaxValue : (int)Math.Floor(value + 0.5d);
    }
}

/// <summary>⑤ 보호막 양의 출처.</summary>
public enum TraitShieldAmountSource : byte
{
    /// <summary>받는 대상 최대 체력 × 비율(수호의 잔광).</summary>
    MaxHpRatio = 0,
    /// <summary>사건의 양(실제 회복량) × 비율(불굴의 가호). 양이 0 이면 보호막 없음.</summary>
    EventAmountRatio = 1,
}

/// <summary>⑤ 보호막을 받는 쪽.</summary>
public enum TraitShieldRecipient : byte
{
    Self = 0,
    /// <summary>사건의 주체(Healed = 회복한 대상 — 자신 또는 전달받은 아군).</summary>
    EventSubject = 1,
}

/// <summary>
/// ⑤ 특성 보호막 규칙(명세 §7) — 특성 채널(<see cref="ShieldType.Trait"/>, 출처 = 특성 해시)마다
/// 기존 잔량과 새 양 중 큰 값을 남기고 지속시간을 새로 센다. 여러 시전자의 같은 특성도 합산하지 않는다. 다른 채널은 건드리지 않는다.
/// </summary>
public static class TraitShieldRules
{
    /// <summary>생성량. 상한 비율 &gt; 0 이면 대상 최대 체력 × 상한으로 자른다.</summary>
    public static int Amount(TraitShieldAmountSource source, float ratio, int maxHp, int eventAmount, float capMaxHpRatio)
    {
        double raw = source == TraitShieldAmountSource.MaxHpRatio
            ? (double)Math.Max(0, maxHp) * Math.Max(0f, ratio)
            : (double)Math.Max(0, eventAmount) * Math.Max(0f, ratio);
        int amount = TraitHealRules.RoundHalfUp(raw);
        if (capMaxHpRatio > 0f)
            amount = Math.Min(amount, TraitHealRules.RoundHalfUp((double)Math.Max(0, maxHp) * capMaxHpRatio));
        return amount;
    }

    /// <summary>같은 특성 채널 — 큰 값 유지(합산 아님).</summary>
    public static int Merge(int existing, int incoming) => Math.Max(Math.Max(0, existing), Math.Max(0, incoming));

    /// <summary>특성 채널의 보호막 출처 값 = 특성 해시(시전자 무관 — 여러 시전자 같은 특성 합산 방지).</summary>
    public static ulong ChannelSource(int traitHash) => unchecked((uint)traitHash);
}

/// <summary>
/// ⑨ 다음 공격 1회 강화 상태(명세 §5·돌파의 여세) — 서버 플레이어별 1개. 같은 특성 재부여 = 중첩 없이 지속시간·배율 갱신.
/// 소모 = 맞는 공격 키의 첫 원래 피해 판정에서 1회. 같은 시전(CastId)의 다른 대상 판정은 같은 배율을 받는다(적 수만큼 소모하지 않음).
/// 사망·새 회차에 <see cref="Clear"/>. 특성이 빠지면 <see cref="Remove"/>.
/// </summary>
public sealed class TraitNextAttackBuffs
{
    private struct Buff
    {
        public int TraitHash;
        public float Multiplier;
        public double ExpireAt;
        public int AttackTypeMask;
        public int FormMask;
        public int ConsumedCastId; // 0 = 아직 안 씀
        public bool Consumed;
    }

    private readonly List<Buff> buffs = new List<Buff>();

    public int Count => buffs.Count;

    public void Grant(int traitHash, float multiplier, double expireAt, int attackTypeMask, int formMask)
    {
        Remove(traitHash);
        buffs.Add(new Buff
        {
            TraitHash = traitHash,
            Multiplier = Math.Max(0f, multiplier),
            ExpireAt = expireAt,
            AttackTypeMask = attackTypeMask,
            FormMask = formMask,
        });
    }

    /// <summary>그 특성의 강화가 아직 쓸 수 있게 남아 있는가.</summary>
    public bool Has(int traitHash, double now)
    {
        for (int i = 0; i < buffs.Count; i++)
        {
            if (buffs[i].TraitHash == traitHash && !buffs[i].Consumed && now < buffs[i].ExpireAt)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 그 공격 판정에 붙는 배율(맞는 강화의 곱, 없으면 1)을 내고 소모한다. 같은 CastId 로 다시 물으면 같은 배율.
    /// CastId 0 은 묶지 않는다(그 판정 한 번만).
    /// </summary>
    public float Consume(TraitAttackKey key, int castId, double now)
    {
        float product = 1f;
        for (int i = buffs.Count - 1; i >= 0; i--)
        {
            Buff b = buffs[i];
            if (b.Consumed)
            {
                if (castId != 0 && b.ConsumedCastId == castId)
                {
                    product *= b.Multiplier;
                    continue;
                }

                buffs.RemoveAt(i); // 다른 시전이 왔다 — 지난 시전의 묶음은 끝났다
                continue;
            }

            if (now >= b.ExpireAt)
            {
                buffs.RemoveAt(i);
                continue;
            }

            if (!TraitModifier.Covers(b.AttackTypeMask, (int)key.Type) || !TraitModifier.Covers(b.FormMask, (int)key.Form))
                continue;

            product *= b.Multiplier;
            b.Consumed = true;
            b.ConsumedCastId = castId;
            if (castId == 0)
                buffs.RemoveAt(i);
            else
                buffs[i] = b;
        }

        return product;
    }

    /// <summary>그 특성의 강화를 지운다(특성 되돌리기). 지운 수.</summary>
    public int Remove(int traitHash) => buffs.RemoveAll(b => b.TraitHash == traitHash);

    public void Clear() => buffs.Clear();
}

/// <summary>⑧ 'N번마다 1회' 카운터(이중 출력 3발). N ≤ 1 이면 매번 true. 사망·새 회차에 <see cref="Reset"/>.</summary>
public sealed class TraitShotCounter
{
    public readonly int Required;
    public int Count { get; private set; }

    public TraitShotCounter(int required) => Required = Math.Max(1, required);

    /// <summary>1 세고, N 번째면 0 으로 돌리고 true.</summary>
    public bool Tick()
    {
        if (Required <= 1)
            return true;

        Count++;
        if (Count < Required)
            return false;

        Count = 0;
        return true;
    }

    public void Reset() => Count = 0;
}

/// <summary>
/// 판정 1회의 공통 플래그(명세 §3). 원래 공격 = 전부, 파생 공격 = <see cref="Derived"/>(DamageOnly — 특성 발동·자원·CC·간파 없음).
/// </summary>
[Flags]
public enum TraitHitFlags
{
    None = 0,
    CanProcTraits = 1 << 0,
    CanGainResource = 1 << 1,
    CanApplyCC = 1 << 2,
    CanCounter = 1 << 3,
    Original = CanProcTraits | CanGainResource | CanApplyCC | CanCounter,
    Derived = None,
}

/// <summary>⑧ 파생 공격 피해(명세 §5) — 원본 Dref(또는 예정 원래 묶음 합계) × 파생 배율, 마지막에 한 번 반올림. 대상 조건 배율·방어는 맞는 쪽이 계산한다.</summary>
public static class TraitDerivedAttackRules
{
    public static int Damage(int sourceDref, float ratio) => TraitHealRules.RoundHalfUp((double)Math.Max(0, sourceDref) * Math.Max(0f, ratio));
}
