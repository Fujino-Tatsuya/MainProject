using System.Collections.Generic;
using UnityEngine;

// 거너 3차 집속 연사(GUN_3_VOLLEY, 할 일 127 4/4)의 순수 규칙 — 물리·NGO 비의존, EditMode 테스트 대상(GunnerTraitTier3VolleyTests).
// 스킬(GunnerChargeLaserSkill)은 첫 발에서 일정을 시작하고 틱마다 시각이 된 발을 꺼내 쏜다. 피해·사거리·폭·방향은 첫 발 스냅샷.
// 원본 명세: Re_C_특성_개발명세_20261016 거너 GUN_3_VOLLEY · §5(스냅샷) · 잔류 포격/충격 레이저 연동.

/// <summary>
/// 집속 연사 수치 — 모드는 특성 스위치(<see cref="PlayerTraitModifiers.HasTrait"/> · <see cref="TraitModeIds.GunnerVolley"/>),
/// 수치는 거너 효과(<see cref="GunnerVolleyEffect"/>)가 기여한 ⑩ 스킬 수치. 특성이 없으면 <see cref="None"/>(단발 — 원래 동작).
/// 런타임은 <see cref="From(PlayerTraitModifiers)"/>, 테스트·카드 합성은 <see cref="From(TraitModifierSet)"/>.
/// </summary>
public readonly struct GunnerVolleyTraits
{
    public readonly bool Active;
    public readonly int ShotCount;
    /// <summary>데이터 발사 간격(초) — 공격 가속 전.</summary>
    public readonly float ShotInterval;
    public readonly float PerShotDamageRatio;

    private GunnerVolleyTraits(bool active, int shotCount, float shotInterval, float perShotDamageRatio)
    {
        Active = active;
        ShotCount = Mathf.Max(1, shotCount);
        ShotInterval = Mathf.Max(0f, shotInterval);
        PerShotDamageRatio = Mathf.Max(0f, perShotDamageRatio);
    }

    /// <summary>특성 없음 — 1발·배율 1(원래 단발 Q).</summary>
    public static GunnerVolleyTraits None => new GunnerVolleyTraits(false, 1, 0f, 1f);

    public static GunnerVolleyTraits From(PlayerTraitModifiers modifiers)
    {
        if (modifiers == null || !modifiers.HasTrait(TraitModeIds.GunnerVolley))
            return None;

        return new GunnerVolleyTraits(true,
            Mathf.RoundToInt(modifiers.ResolveSkillValue(GunnerTraitRules.QVolleyShotCount, 1f)),
            modifiers.ResolveSkillValue(GunnerTraitRules.QVolleyShotInterval, 0f),
            modifiers.ResolveSkillValue(GunnerTraitRules.QVolleyPerShotRatio, 1f));
    }

    public static GunnerVolleyTraits From(TraitModifierSet modifiers)
    {
        if (modifiers == null || !Has(modifiers.TraitHashes, TraitModeIds.GunnerVolley))
            return None;

        return new GunnerVolleyTraits(true,
            Mathf.RoundToInt(modifiers.ResolveSkillValue(GunnerTraitRules.QVolleyShotCount, 1f)),
            modifiers.ResolveSkillValue(GunnerTraitRules.QVolleyShotInterval, 0f),
            modifiers.ResolveSkillValue(GunnerTraitRules.QVolleyPerShotRatio, 1f));
    }

    /// <summary>표시용 검증값 — 발수 × 발당 배율(독립 조정값 아님, 카드 별칭 totalDamageRatio).</summary>
    public float TotalDamageRatio => ShotCount * PerShotDamageRatio;

    /// <summary>
    /// 한 발의 충전 배율 = 충전 배율 × 발당 배율. 충전 정도는 발수가 아니라 각 발의 피해를 정한다.
    /// 배율을 곱해서 <see cref="GunnerQCastSnapshot.ShotDamage"/> 에 넘긴다 — 중간 반올림 없이 마지막에 한 번(명세 §5).
    /// </summary>
    public float ShotChargeScale(float chargeScale) => chargeScale * PerShotDamageRatio;

    /// <summary>공격 가속을 반영한 발사 간격 — 충전시간에는 적용하지 않는다(그건 <see cref="GunnerTraitRules.QMaxChargeTimeFor"/>).</summary>
    public float IntervalAt(float attackSpeedMultiplier) => PlayerAttackSpeedRules.Duration(ShotInterval, attackSpeedMultiplier);

    /// <summary>첫 발부터 마지막 발까지 걸리는 추가 시간 = (발수 − 1) × 간격. 서버 안전망에 더한다.</summary>
    public float ExtraDuration(float attackSpeedMultiplier) => (ShotCount - 1) * IntervalAt(attackSpeedMultiplier);

    private static bool Has(IReadOnlyList<int> hashes, string id)
    {
        int hash = TraitIdHash.Of(id);
        for (int i = 0; hashes != null && i < hashes.Count; i++)
        {
            if (hashes[i] == hash)
                return true;
        }

        return false;
    }
}

/// <summary>
/// [서버] Q 시전 1회의 원래 발 일정 — 첫 발을 <see cref="BeginFirstShot"/> 시각 0 으로, 이후 k 번째 발은 k × 간격에 나온다.
/// 단발 Q 도 1발 일정이다(첫 발로 끝). 사망·강제 중단은 <see cref="Cancel"/> — 남은 발은 나오지 않고 <see cref="AllShotsFired"/> 도 거짓(잔류 포격 없음).
/// 틱이 늦어 여러 발 시각이 지났으면 <see cref="TryTakeDue"/> 를 반복해 같은 틱에 순서대로 꺼낸다.
/// </summary>
public sealed class GunnerVolleySchedule
{
    private int shotCount;
    private int fired;
    private double firstShotTime;
    private float interval;
    private bool canceled;

    public int ShotCount => shotCount;
    public int ShotsFired => fired;
    public float Interval => interval;
    public bool Started => fired > 0;
    public bool Canceled => canceled;

    /// <summary>아직 나갈 발이 남았다(시작했고, 취소되지 않았고, 다 쏘지 않았다).</summary>
    public bool HasPending => Started && !canceled && fired < shotCount;

    /// <summary>예정한 원래 발을 모두 쐈다 — 잔류 포격 자격(정상 종료 판정과 함께).</summary>
    public bool AllShotsFired => Started && fired >= shotCount;

    public void Reset()
    {
        shotCount = 0;
        fired = 0;
        firstShotTime = 0d;
        interval = 0f;
        canceled = false;
    }

    /// <summary>첫 발 발사(시각 0) — 일정을 정하고 1발 쏜 것으로 센다.</summary>
    public void BeginFirstShot(double now, int count, float shotInterval)
    {
        shotCount = Mathf.Max(1, count);
        interval = Mathf.Max(0f, shotInterval);
        firstShotTime = now;
        fired = 1;
        canceled = false;
    }

    /// <summary>k 번째(0부터) 발 예정 시각.</summary>
    public double DueTime(int index) => firstShotTime + (double)index * interval;

    /// <summary>다음 발 시각이 되었으면 그 발 번호를 내고 1발 쏜 것으로 센다.</summary>
    public bool TryTakeDue(double now, out int index)
    {
        index = -1;
        if (!HasPending || now + 1e-6 < DueTime(fired))
            return false;

        index = fired++;
        return true;
    }

    /// <summary>남은 발 취소 — 이미 쏜 발(쿨타임·소모 열)은 그대로다.</summary>
    public void Cancel() => canceled = true;
}

/// <summary>
/// [서버] Q 시전 1회의 아군 보호막 장부 — 같은 아군은 시전당 원래 보호막량 1회(발당 비율로 나누거나 발마다 합산하지 않는다).
/// 시전자 제외는 수집 쪽(<c>GunnerBeamAttack.CollectAllies</c>)이 한다.
/// </summary>
public sealed class GunnerCastShieldGate
{
    private readonly HashSet<int> given = new HashSet<int>();

    public void Reset() => given.Clear();

    /// <summary>이 시전에서 이 아군이 처음이면 true(그리고 기록).</summary>
    public bool TryGive(int allyId) => given.Add(allyId);
}
