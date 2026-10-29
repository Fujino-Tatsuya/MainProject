using System;
using System.Collections.Generic;
using UnityEngine;

// 거너 3·4차 특성의 순수 판정 규칙(할 일 127 2/4) — 물리·NGO 비의존, EditMode 테스트 대상(GunnerTraitTier34Tests).
// 스킬(Player/Gunner/*)은 물리 질의 결과를 여기에 넣고 나온 순서·배율·예약만 쓴다.
// 원본 명세: Re_C_특성_개발명세_20261016 §3(대상별 1회)·§5(피해)·§9(정리)·거너 GUN_3_Q_PUSH·GUN_3_PIERCE·GUN_4_R_FOCUS·GUN_4_EXTRA_BASIC·GUN_4_ECHO_Q.

/// <summary>관통 탄도 후보 1개 — 같은 유닛의 여러 콜라이더는 <see cref="TargetKey"/> 하나로 합친다.</summary>
public readonly struct GunnerBeamCandidate
{
    /// <summary>대상 단위 키(유닛이면 유닛, 주인 없는 히트박스면 히트박스).</summary>
    public readonly int TargetKey;
    /// <summary>동률 정렬 키(네트워크 ID, 스폰 안 됐으면 0).</summary>
    public readonly ulong NetworkId;
    /// <summary>발사점에서의 거리(SphereCast distance).</summary>
    public readonly float Distance;
    /// <summary>원본 질의 결과의 위치(호출자가 콜라이더를 되찾는 용도).</summary>
    public readonly int SourceIndex;

    public GunnerBeamCandidate(int targetKey, ulong networkId, float distance, int sourceIndex = -1)
    {
        TargetKey = targetKey;
        NetworkId = networkId;
        Distance = distance;
        SourceIndex = sourceIndex;
    }

    public override string ToString() => $"#{TargetKey} net {NetworkId} d {Distance:0.###}";
}

/// <summary>
/// 관통 탄도(GUN_3_PIERCE) — 기본 공격이 적을 꿰뚫고 벽·지형에서 멈춘다(원래 사거리 유지).
/// 첫 적 100%, 두 번째부터 각각 <c>secondaryDamageRatio</c>(거듭제곱 아님). 서버 거리순, 동률은 네트워크 ID 로 안정 정렬, 한 발당 대상별 1회.
/// </summary>
public static class GunnerPierceRules
{
    private static readonly Comparison<GunnerBeamCandidate> ByDistanceThenId = (a, b) =>
    {
        int c = a.Distance.CompareTo(b.Distance);
        if (c != 0)
            return c;
        c = a.NetworkId.CompareTo(b.NetworkId);
        return c != 0 ? c : a.TargetKey.CompareTo(b.TargetKey);
    };

    /// <summary>
    /// 콜라이더 단위 후보 → 대상 단위 순서. 같은 대상은 가장 가까운 거리 하나로 합치고, 벽 거리보다 먼 대상은 버린다
    /// (벽에 닿는 거리까지는 맞는다 — <paramref name="wallDistance"/> 이하). 결과는 거리 → 네트워크 ID → 대상 키 순.
    /// </summary>
    public static void Order(IReadOnlyList<GunnerBeamCandidate> raw, float wallDistance, List<GunnerBeamCandidate> result)
    {
        result.Clear();
        if (raw == null)
            return;

        for (int i = 0; i < raw.Count; i++)
        {
            GunnerBeamCandidate c = raw[i];
            if (c.Distance > wallDistance)
                continue;

            int existing = IndexOf(result, c.TargetKey);
            if (existing < 0)
                result.Add(c);
            else if (c.Distance < result[existing].Distance)
                result[existing] = c;
        }

        // List.Sort 는 불안정 정렬이지만 비교 키가 대상마다 유일(대상 키 포함)이라 결과가 하나로 정해진다.
        result.Sort(ByDistanceThenId);
    }

    private static int IndexOf(List<GunnerBeamCandidate> list, int targetKey)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].TargetKey == targetKey)
                return i;
        }

        return -1;
    }

    /// <summary>그 발의 몇 번째(0부터) 적중인가 → 피해 비율. 첫 적 1, 이후 각각 secondaryRatio(반복 곱 아님).</summary>
    public static float DamageRatio(int hitIndex, float secondaryRatio) => hitIndex <= 0 ? 1f : Mathf.Max(0f, secondaryRatio);

    /// <summary>원래 발 피해 → 그 적중의 피해. 첫 적은 원래 값 그대로(반올림 없음), 이후는 한 번만 반올림.</summary>
    public static int Damage(int shotDamage, int hitIndex, float secondaryRatio) =>
        hitIndex <= 0 ? Mathf.Max(0, shotDamage) : TraitHealRules.RoundHalfUp((double)Mathf.Max(0, shotDamage) * DamageRatio(hitIndex, secondaryRatio));
}

/// <summary>거너 R(추적 레이저) 실제 피해 범위 1개 — 시전자·중심·반경.</summary>
public readonly struct GunnerRZone
{
    public readonly int OwnerKey;
    public readonly Vector3 Center;
    public readonly float Radius;

    public GunnerRZone(int ownerKey, Vector3 center, float radius)
    {
        OwnerKey = ownerKey;
        Center = center;
        Radius = radius;
    }
}

/// <summary>
/// 집중 포화(GUN_4_R_FOCUS) — 피해 적용 순간 대상 중심이 <b>자신의</b> 활성 R 범위 안이면 기본 공격·Q 원래 피해(와 그 계열 파생) × (1 + 보너스).
/// 여러 R 이 겹쳐도 한 번, 다른 거너의 R 은 자격 없음, R 틱·우클릭 제외. 거리는 수평(XZ) — R 판정 구는 지면 높이라 키 큰 몹의 중심 높이를 빼고 잰다.
/// </summary>
public static class GunnerFocusFireRules
{
    /// <summary>집중 포화가 붙는 공격 종류 — 기본 공격·Q 뿐(R 틱·우클릭·그 밖 제외).</summary>
    public static bool AppliesTo(AttackType type) => type == AttackType.Default || type == AttackType.SkillQ;

    public static bool IsInside(Vector3 center, float radius, Vector3 point)
    {
        float dx = point.x - center.x;
        float dz = point.z - center.z;
        return radius > 0f && dx * dx + dz * dz <= radius * radius;
    }

    /// <summary>그 공격자의 R 중 하나라도 대상 중심을 담는가(겹쳐도 true 한 번).</summary>
    public static bool IsInsideOwnZone(int attackerKey, Vector3 point, IReadOnlyList<GunnerRZone> zones)
    {
        for (int i = 0; zones != null && i < zones.Count; i++)
        {
            if (zones[i].OwnerKey == attackerKey && IsInside(zones[i].Center, zones[i].Radius, point))
                return true;
        }

        return false;
    }

    /// <summary>대상 조건 배율 — 범위 안이고 보너스가 있으면 1 + 보너스, 아니면 1.</summary>
    public static float Multiplier(AttackType type, float bonus, bool insideOwnZone) =>
        AppliesTo(type) && insideOwnZone && bonus > 0f ? 1f + bonus : 1f;

    /// <summary>피해 1건에 대상 조건 배율을 곱한다(배율 1 이면 그대로, 아니면 한 번 반올림).</summary>
    public static int Apply(int damage, float multiplier) =>
        Mathf.Approximately(multiplier, 1f) ? Mathf.Max(0, damage) : TraitHealRules.RoundHalfUp((double)Mathf.Max(0, damage) * multiplier);
}

/// <summary>
/// 충격 레이저(GUN_3_Q_PUSH) — 시전 전체에서 대상별 유효 CC 승인 1회. 면역(슈퍼아머)이라 요청하지 않은 대상은 같은 시전의 이후 원래 발에서
/// 처음 유효 승인될 수 있다. 실제 CC 는 공용 <see cref="TraitKnockbackEffect.ApplyCrowdControl"/> 한 곳(<see cref="PlayerTraitEffects.ModifyOutgoingAttackServer"/> 경유).
/// 🔴 면역 대상에는 공용 경로를 부르지 않는다 — 공용 시전·대상 거름이 부르는 순간 그 대상을 써 버리기 때문이다.
/// </summary>
public sealed class GunnerCastCcGate
{
    private readonly Dictionary<int, HashSet<int>> approvedByCast = new Dictionary<int, HashSet<int>>();
    private readonly Queue<int> castOrder = new Queue<int>();

    /// <summary>이 시전에서 이 대상에 CC 를 요청할까 — 아직 승인 전이고 지금 면역이 아니면 true.</summary>
    public bool ShouldRequest(int castId, int targetId, bool targetImmune) =>
        !targetImmune && !IsApproved(castId, targetId);

    public bool IsApproved(int castId, int targetId) =>
        castId != 0 && approvedByCast.TryGetValue(castId, out HashSet<int> targets) && targets.Contains(targetId);

    /// <summary>요청 결과 CC 가 실제로 실렸다 — 이 시전에서 이 대상은 끝.</summary>
    public void MarkApproved(int castId, int targetId)
    {
        if (castId == 0)
            return;

        if (!approvedByCast.TryGetValue(castId, out HashSet<int> targets))
        {
            targets = new HashSet<int>();
            approvedByCast.Add(castId, targets);
            castOrder.Enqueue(castId);
            while (castOrder.Count > TraitProcGate.RememberedCasts)
                approvedByCast.Remove(castOrder.Dequeue());
        }

        targets.Add(targetId);
    }

    public void Reset()
    {
        approvedByCast.Clear();
        castOrder.Clear();
    }
}

/// <summary>예약된 파생 레이저 1발(이중 출력) — 위치·방향·사거리·폭·피해를 예약 때 고정하고 대상·지형은 발사 때 새로 판정한다.</summary>
public readonly struct GunnerDerivedShot
{
    public readonly Vector3 Origin;
    public readonly Vector3 Direction;
    /// <summary>지형 차단 전 사거리(이중 출력 = 기본 공격 사거리).</summary>
    public readonly float Range;
    /// <summary>판정 폭.</summary>
    public readonly float Width;
    public readonly int Damage;

    public GunnerDerivedShot(Vector3 origin, Vector3 direction, float range, float width, int damage)
    {
        Origin = origin;
        Direction = direction;
        Range = range;
        Width = width;
        Damage = damage;
    }
}

/// <summary>
/// [서버] 지연 발사 예약(이중 출력) — 새 예약이 기존 예약을 덮어쓰지 않는다. 사망·씬 종료면 <see cref="Clear"/>(명세 §9).
/// </summary>
public sealed class GunnerDelayedShots
{
    private readonly List<double> dueTimes = new List<double>();
    private readonly List<GunnerDerivedShot> shots = new List<GunnerDerivedShot>();

    public int Count => shots.Count;

    public void Schedule(double now, float delay, GunnerDerivedShot shot)
    {
        dueTimes.Add(now + Math.Max(0f, delay));
        shots.Add(shot);
    }

    /// <summary>시각이 된 예약을 예약 순서대로 꺼낸다.</summary>
    public void CollectDue(double now, List<GunnerDerivedShot> result)
    {
        result.Clear();
        for (int i = 0; i < shots.Count;)
        {
            if (dueTimes[i] <= now)
            {
                result.Add(shots[i]);
                dueTimes.RemoveAt(i);
                shots.RemoveAt(i);
                continue;
            }

            i++;
        }
    }

    public void Clear()
    {
        dueTimes.Clear();
        shots.Clear();
    }
}

/// <summary>
/// 이중 출력(GUN_4_EXTRA_BASIC) — 원래 기본 공격 <b>발사 커밋</b>만 센다(빗나가도 +1, 취소·Q·추가 발은 부르지 않는다).
/// N 번째에 세 번째 원래 발 피해 × 비율의 추가 레이저를 공격 가속 반영 지연 뒤 예약. 카운터는 추가 발사 예약 때 0, 사망에 초기화(공용 장부).
/// </summary>
public static class GunnerDualOutputRules
{
    /// <returns>이번 발이 N 번째라 추가 레이저를 예약해야 하면 true(피해·지연을 낸다).</returns>
    public static bool CountOriginalShot(TraitDerivedAttack derived, int shotDamage, float attackSpeedMultiplier,
        out int extraDamage, out float delay)
    {
        extraDamage = 0;
        delay = 0f;
        if (derived == null || !derived.Counter.Tick())
            return false;

        extraDamage = derived.DamageFrom(shotDamage);
        delay = PlayerAttackSpeedRules.Duration(derived.Delay, attackSpeedMultiplier);
        return true;
    }
}

/// <summary>
/// 잔류 포격(GUN_4_ECHO_Q, 할 일 149) — Q 시전 1회의 원래 발 묶음. 원래 발에 맞은 대상마다 에너지 폭탄 1개(<see cref="GunnerEchoBombs"/>).
/// 폭탄 1개 피해 = 원래 발마다의 <b>예정 단일 대상 피해 합계</b> × 비율(실제 총 피해·맞힌 수 아님) — 붙은 그 대상 하나에게만.
/// 정상 종료에서만(집속 연사도 마지막 원래 발 뒤 — 발마다 <see cref="AddOriginalShot"/>·<see cref="AddHitTarget"/>). 아무도 안 맞혔으면 없음.
/// </summary>
public sealed class GunnerEchoPlan
{
    private readonly List<ulong> hitTargets = new List<ulong>();

    public bool HasShot { get; private set; }
    public int PlannedDamageSum { get; private set; }

    /// <summary>이번 시전 원래 발에 맞은 대상(네트워크 ID, 중복 없음, 처음 맞은 순서).</summary>
    public IReadOnlyList<ulong> HitTargets => hitTargets;

    public void Reset()
    {
        HasShot = false;
        PlannedDamageSum = 0;
        hitTargets.Clear();
    }

    /// <summary>원래 발 1발 — 예정 단일 대상 피해를 더한다.</summary>
    public void AddOriginalShot(int plannedSingleTargetDamage)
    {
        HasShot = true;
        long sum = (long)PlannedDamageSum + Mathf.Max(0, plannedSingleTargetDamage);
        PlannedDamageSum = sum >= int.MaxValue ? int.MaxValue : (int)sum;
    }

    /// <summary>원래 발에 맞은 대상 1개 — 집속 연사로 같은 대상을 여러 번 맞혀도 시전당 1번만 남는다(새로 넣었으면 true).</summary>
    public bool AddHitTarget(ulong targetId)
    {
        if (hitTargets.Contains(targetId))
            return false;

        hitTargets.Add(targetId);
        return true;
    }

    /// <summary>
    /// 정상 종료인가 — 예정한 원래 발을 쐈고 Q 가 End 이벤트(또는 그 이벤트가 빠졌을 때의 안전망)로 끝났다.
    /// 발사 전 끝남·피격/상태 전환 취소·사망·대시는 아니다.
    /// </summary>
    public static bool IsNormalEnd(bool fired, SkillEndReason reason) =>
        fired && (reason == SkillEndReason.Completed || reason == SkillEndReason.MaxDurationReached);

    /// <summary>정상 종료이고 맞힌 대상이 있으면 폭탄 1개 피해(<see cref="HitTargets"/> 마다 하나씩 심는다). 아니면 false(이 시전은 잔류 없음).</summary>
    public bool TryBuildEcho(bool fired, SkillEndReason reason, TraitDerivedAttack derived, out int bombDamage)
    {
        bombDamage = 0;
        if (!HasShot || hitTargets.Count == 0 || derived == null || !IsNormalEnd(fired, reason) || !derived.Counter.Tick())
            return false;

        bombDamage = derived.DamageFrom(PlannedDamageSum);
        return true;
    }
}

/// <summary>대상 몸에 붙은 잔류 포격 에너지 폭탄 1개 — 서버가 들고, 복제는 <see cref="Id"/>·<see cref="TargetId"/> 만(<c>GunnerBeamView</c>).</summary>
public readonly struct GunnerEchoBomb
{
    /// <summary>폭탄 번호(사수 하나 안에서 유일) — 같은 대상에 이전 시전 폭탄이 같이 붙어 있어도 구분한다.</summary>
    public readonly int Id;
    /// <summary>붙은 대상의 네트워크 ID.</summary>
    public readonly ulong TargetId;
    public readonly double DueTime;
    public readonly int Damage;

    public GunnerEchoBomb(int id, ulong targetId, double dueTime, int damage)
    {
        Id = id;
        TargetId = targetId;
        DueTime = dueTime;
        Damage = damage;
    }
}

/// <summary>
/// [서버] 잔류 포격 에너지 폭탄들(할 일 149) — 시전마다 맞은 대상마다 1개를 심고, 지연이 지나면 그 대상에게만 터진다.
/// 새 시전이 이전 폭탄을 덮어쓰지 않는다(같은 대상에 둘 붙을 수 있다). 대상이 먼저 죽거나 사라지면 시각과 무관하게 바로 '빈 폭발'(피해 대상 없음).
/// 사수 사망·씬 종료면 <see cref="Clear"/>(명세 §9 — 터지지 않고 사라진다).
/// </summary>
public sealed class GunnerEchoBombs
{
    private readonly List<GunnerEchoBomb> bombs = new List<GunnerEchoBomb>();
    private int nextId;

    public int Count => bombs.Count;
    public IReadOnlyList<GunnerEchoBomb> Bombs => bombs;

    /// <summary>대상마다 1개 심는다(목록 안 중복 대상은 한 번). 심은 폭탄을 <paramref name="planted"/> 에 담는다.</summary>
    public void Plant(double now, float delay, int damage, IReadOnlyList<ulong> targets, List<GunnerEchoBomb> planted)
    {
        planted.Clear();
        double due = now + Math.Max(0f, delay);
        for (int i = 0; targets != null && i < targets.Count; i++)
        {
            if (IndexOf(planted, targets[i]) >= 0)
                continue;

            var bomb = new GunnerEchoBomb(++nextId, targets[i], due, Mathf.Max(0, damage));
            bombs.Add(bomb);
            planted.Add(bomb);
        }
    }

    /// <summary>
    /// 꺼낼 폭탄을 심은 순서대로 나눈다 — 대상이 살아 있고 시각이 됐으면 <paramref name="detonated"/>(피해),
    /// 대상이 죽었거나 사라졌으면 시각과 무관하게 <paramref name="fizzled"/>(그 자리 폭발, 피해 없음). 나머지는 남는다.
    /// </summary>
    public void Collect(double now, Predicate<ulong> targetAlive, List<GunnerEchoBomb> detonated, List<GunnerEchoBomb> fizzled)
    {
        detonated.Clear();
        fizzled.Clear();
        for (int i = 0; i < bombs.Count;)
        {
            GunnerEchoBomb bomb = bombs[i];
            bool alive = targetAlive != null && targetAlive(bomb.TargetId);
            if (alive && bomb.DueTime > now)
            {
                i++;
                continue;
            }

            (alive ? detonated : fizzled).Add(bomb);
            bombs.RemoveAt(i);
        }
    }

    public void Clear() => bombs.Clear();

    private static int IndexOf(List<GunnerEchoBomb> list, ulong targetId)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].TargetId == targetId)
                return i;
        }

        return -1;
    }
}
