using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>팔라딘 특성의 스킬 쪽 판단(할 일 126) — 스킬 코드가 부르고 EditMode 테스트가 고정한다.</summary>
public static class PaladinTraitRules
{
    /// <summary>
    /// 수호의 잔광(PAL_3_E_SHIELD) — E 가 끝나는 순간(= 방어 성공 뒤 피해 무시 단계가 끝나는 순간) 보호막 사건을 낼까.
    /// 이번 시전에 방어에 성공했고(실패면 예약 없음) 시전자가 살아 있을 때만(사망 = 예약 취소).
    /// 같은 시전의 중복은 효과 거름(oncePerCast, 방어 성공 시전 ID)이 막는다.
    /// </summary>
    public static bool GrantsGuardAfterglow(bool blockedThisCast, SkillEndReason reason, int casterHealth) =>
        blockedThisCast && reason != SkillEndReason.CasterDied && casterHealth > 0;

    /// <summary>
    /// 수호의 파동(PAL_4_E_PUSH) — 반격 판정 순간 플레이어 위치(<paramref name="origin"/>) 중심 반경 안인가(바닥 평면, 대상 콜라이더 표면 기준 —
    /// 반격 부채꼴 거리와 같은 기준). 반경 0 이하 = 특성 없음.
    /// </summary>
    public static bool InsidePushRadius(Vector3 origin, Vector3 closestPoint, float radius)
    {
        if (!(radius > 0f))
            return false;

        Vector3 toTarget = closestPoint - origin;
        toTarget.y = 0f;
        return toTarget.sqrMagnitude <= radius * radius;
    }

    /// <summary>
    /// 진격의 충격(PAL_4_Q_BLAST) — Q 가 끝난 사유로 충격을 낼까. 정상 종료(Completed — 지형 안전 정지 포함)·홀드 해제(Released)·
    /// 최대 유지 시간 도달(MaxDurationReached = 예정 홀드를 끝까지 함)이면 낸다. 강제 중단(Cancelled — 피격·구속·씬 전환)·대시 끊김·사망이면 없다.
    /// 빗나갔는지는 묻지 않는다(명세 — 빗나가도 정상 종료면 발생).
    /// </summary>
    public static bool QBlastFires(SkillEndReason reason, int casterHealth) =>
        casterHealth > 0 &&
        (reason == SkillEndReason.Completed || reason == SkillEndReason.Released || reason == SkillEndReason.MaxDurationReached);

    /// <summary>
    /// 진격의 충격 판정 기준 포즈(할 일 153) = Q 끝 지점(<paramref name="qEndPosition"/>) + 마지막 진행 방향(<paramref name="heading"/>) — 효과 hitArea 의 기준점 Point.
    /// 꼭짓점을 앞으로 옮기는 건 도형 칸 forwardOffset. 서버 판정(<see cref="FirstMeleeMainSkill"/>)과 특성 랩 공격 범위 기즈모(할 일 152)가 같이 쓴다.
    /// </summary>
    public static AttackShapePose QBlastPose(AttackShapeSet area, Vector3 qEndPosition, Vector3 heading) =>
        area != null
            ? area.ResolvePose(qEndPosition, heading, qEndPosition, heading)
            : new AttackShapePose(qEndPosition, heading);

    /// <summary>기본 Q 의 예정 틱 수 = 최대 유지 시간 ÷ 틱 간격(내림). 홀드 틱은 시작 뒤 간격마다 1회라 안전망 padding(&lt; 간격) 안에서는 늘지 않는다.</summary>
    public static int PlannedHoldTicks(float holdDuration, float tickInterval)
    {
        if (!(holdDuration > 0f) || !(tickInterval > 0f))
            return 0;

        return (int)Math.Floor(holdDuration / tickInterval + 1e-4);
    }

    /// <summary>
    /// 🔴 DQpacket 한 곳 — 현재 Q 모드의 예정 단일 대상 원래 피해 합계(명세 §5·진격의 충격). 실제로 맞힌 틱·적 수와 무관하다.
    /// 기본 Q = 틱 1회 Dref(<paramref name="tickDref"/> — 진격 강화 등 Q 피해 특성이 이미 들어간 값이라 여기서 다시 곱하지 않는다) × 예정 틱 수.
    /// 돌파의 방패 모드는 <see cref="QPacketDamage(int,float,float,float)"/> 가 '교체 단발 피해'로 바꾼다 — 판정(스킬)과 카드 <c>{dmg}</c> 가 같이 따라간다.
    /// </summary>
    public static int QPacketDamage(int tickDref, float holdDuration, float tickInterval)
    {
        long total = (long)Math.Max(0, tickDref) * PlannedHoldTicks(holdDuration, tickInterval);
        return total >= int.MaxValue ? int.MaxValue : (int)total;
    }

    /// <summary>
    /// 🔴 DQpacket 모드 분기 한 곳 — <paramref name="dashDamageRatio"/> &gt; 0(돌파의 방패) = 교체 단발 피해 = DQfull × 배율,
    /// 0 이하 = 기본 Q 예정 틱 합계. DQfull = 원래 Q 가 정상 지속시간 동안 한 대상에게 줄 예정 피해 합계(위 함수 — 실제 맞힌 틱·적 수 무관).
    /// 진격 강화는 틱 Dref 에 이미 들어 있으니 다시 곱하지 않는다(§10: 300 × 1.20 × 1.25 = 450).
    /// </summary>
    public static int QPacketDamage(int tickDref, float holdDuration, float tickInterval, float dashDamageRatio)
    {
        int full = QPacketDamage(tickDref, holdDuration, tickInterval);
        return dashDamageRatio > 0f ? TraitDerivedAttackRules.Damage(full, dashDamageRatio) : full;
    }

    /// <summary>
    /// 돌파의 방패 모드인가 — 특성을 골랐고 수치가 온전할 때만. 수치가 깨졌으면 원래 Q 로 돈다(스킬이 오류를 남긴다).
    /// </summary>
    public static bool UsesShieldDash(bool hasTrait, in PaladinShieldDashSettings settings) => hasTrait && settings.IsValid;

    /// <summary>
    /// Q 시작 상태 — 원래 Q = 홀드(Charging, 지속 타격·지속 밀치기·조향·홀드 해제), 돌파의 방패 = 실행(Active).
    /// <see cref="PlayerHoldSkill.OnTick"/>·<see cref="PlayerHoldSkill.OnReleased"/> 는 Charging 일 때만 돌기 때문에 이 값 하나로 옛 루틴이 꺼진다.
    /// </summary>
    public static SkillState QStartState(bool shieldDash) => shieldDash ? SkillState.Active : SkillState.Charging;

    /// <summary>
    /// Q 가 끝난 뒤 좌클릭 홀드면 기본 공격을 잇는가(할 일 146) — 돌파의 방패 모드일 때만. 원래 Q(홀드 스킬)는 잇지 않는다.
    /// </summary>
    public static bool QResumesBasicAttack(bool hasTrait, in PaladinShieldDashSettings settings) => UsesShieldDash(hasTrait, settings);

    /// <summary>기본 Q 의 유효 최대 거리(m) = 전진 속도 × 최대 유지 시간.</summary>
    public static float BaseQMaxDistance(float advanceSpeed, float maxActiveDuration) =>
        Math.Max(0f, advanceSpeed) * Math.Max(0f, maxActiveDuration);

    /// <summary>
    /// 돌파의 방패 콘텐츠 검증(명세 §9·PAL_2_DASH) — 수치 7개가 다 있고 양수(준비·회수는 0 이상)인가, 그리고 기본 Q 유효 최대 거리가
    /// 돌진 거리보다 긴가(기본 Q 가 3m 이하면 '짧아진 거리'가 성립하지 않는다 — 거리를 자동으로 늘리지 않고 오류로 막는다).
    /// </summary>
    public static void ValidateShieldDash(in PaladinShieldDashSettings settings, float baseQMaxDistance, List<string> errors)
    {
        if (!settings.IsValid)
            errors.Add($"돌파의 방패 수치가 비었거나 0 이하 — 거리 {settings.Distance}·속도 {settings.Speed}·준비 {settings.PrepareTime}·회수 {settings.RecoveryTime}·" +
                       $"피해 배율 {settings.DamageRatio}·밀치기 {settings.PushDistance}m/{settings.PushDuration}초");
        if (!(baseQMaxDistance > settings.Distance))
            errors.Add($"기본 Q 유효 최대 거리 {baseQMaxDistance}m 가 돌파의 방패 거리 {settings.Distance}m 이하 — 거리를 자동으로 늘리지 않는다(데이터 확인)");
    }
}
