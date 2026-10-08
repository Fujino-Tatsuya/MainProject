using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 어쌔신 변신 E 원형 5타의 순수 규칙(character_assassin.md §9). Unity 물리 없이 EditMode 로 검증한다.
/// - 시작 조건: 변신 중이고 종료 대기(만료·수동 해제 요청)가 아닐 때만(§10.3 — 종료 요청 후 새 공격 없음).
/// - 대상 선택: 콜라이더 AABB 의 수평 투영이 원(중심·반경)과 겹치면 범위 안. 높이는 호출자의 Overlap 상자가 자른다.
/// </summary>
public static class AssassinCircleStrikeRules
{
    public static bool CanStart(bool isTransformed, bool isTransformEndPending) =>
        isTransformed && !isTransformEndPending;

    /// <summary>
    /// <paramref name="bounds"/>(월드 AABB)의 XZ 사각형이 중심 <paramref name="center"/>·반경 <paramref name="radius"/> 원과 겹치는가.
    /// 큰 보스 콜라이더도 가장자리만 원에 걸리면 맞는다. y 는 보지 않는다.
    /// </summary>
    public static bool OverlapsCircle(Vector3 center, float radius, Bounds bounds)
    {
        if (radius <= 0f)
            return false;

        float closestX = Mathf.Clamp(center.x, bounds.min.x, bounds.max.x);
        float closestZ = Mathf.Clamp(center.z, bounds.min.z, bounds.max.z);
        float dx = closestX - center.x;
        float dz = closestZ - center.z;
        return dx * dx + dz * dz <= radius * radius;
    }
}

/// <summary>
/// 변신 E 사용 1회의 타격 진행(§9.2·§3.3).
/// - 타격은 최대 <c>hitCount</c> 회. 클립 Hit 이벤트가 더 와도 무시하고, End 가 일찍 오면 호출자가 <see cref="RemainingStrikes"/> 만큼 마저 친다.
/// - 같은 대상은 한 타에서 1회, 다음 타에서는 다시 맞는다(타마다 현재 범위 재판정 — 첫 타 대상을 이어 붙이지 않음).
/// - 무적 구간 = <see cref="Begin"/> ~ <see cref="Stop"/>. 쓰러짐·사망·강제 중단은 남은 타를 치지 않고 Stop 한다.
/// 키는 Unit 또는 (Unit 이 아닌 상자의) Hurtbox — 호출자가 정한다.
/// </summary>
public sealed class AssassinCircleStrikeSequence
{
    private readonly HashSet<object> strikeTargets = new HashSet<object>();
    private int hitCount;

    public bool IsActive { get; private set; }
    public int StrikesDone { get; private set; }
    public int RemainingStrikes => IsActive ? Mathf.Max(0, hitCount - StrikesDone) : 0;

    /// <summary>공격 시작부터 끝까지 무적을 유지해야 하는가(§9.2).</summary>
    public bool HoldsInvulnerability => IsActive;

    public void Begin(int strikes)
    {
        hitCount = Mathf.Max(1, strikes);
        StrikesDone = 0;
        strikeTargets.Clear();
        IsActive = true;
    }

    /// <summary>다음 타를 시작한다. 진행 중이 아니거나 이미 모든 타를 쳤으면 false.</summary>
    public bool TryBeginStrike()
    {
        if (RemainingStrikes <= 0)
            return false;

        StrikesDone++;
        strikeTargets.Clear();
        return true;
    }

    /// <summary>이번 타에서 처음 보는 대상이면 true 를 돌려주고 기록한다.</summary>
    public bool TryRegisterTarget(object target)
    {
        return IsActive && target != null && strikeTargets.Add(target);
    }

    /// <summary>
    /// 종료 — 무적 해제. 정상 완료면 호출자가 먼저 남은 타를 마저 치고, 쓰러짐·사망·강제 중단이면 남은 타는 그대로 버려진다.
    /// 이미 준 피해는 유지한다(§12.2-3·4).
    /// </summary>
    public void Stop()
    {
        IsActive = false;
        strikeTargets.Clear();
    }
}
