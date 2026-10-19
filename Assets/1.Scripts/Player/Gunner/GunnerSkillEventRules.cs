using UnityEngine;

/// <summary>
/// 거너 스킬의 애니 이벤트 규칙 (PLAN-anim-event-timing P3).
/// 판정·종료·생성 시점은 클립 이벤트(GunnerAnimEventAuthoring 이 저작)가 정하고,
/// 이벤트가 끝내 안 오면 PlayerSkillController 의 maxActiveDuration 안전망이 끝낸다.
/// </summary>
public static class GunnerSkillEventRules
{
    /// <summary>
    /// R 레이저 생성 위치·대상. 지점 지정이면 그 지점, 대상이 살아 있으면 지금 대상 위치,
    /// 시전 뒤 대상이 죽었거나 사라졌으면 시전 순간 대상 위치에 대상 없이 → 레이저가 재탐색으로 적을 찾는다.
    /// </summary>
    public static Vector3 ResolveLaserSpawn(bool hasAimPoint, Vector3 aimPoint,
        bool targetAlive, Vector3 targetPosition, Vector3 castTargetPosition, out bool keepTarget)
    {
        if (hasAimPoint)
        {
            keepTarget = false;
            return aimPoint;
        }

        keepTarget = targetAlive;
        return targetAlive ? targetPosition : castTargetPosition;
    }

    /// <summary>[에디터] 이벤트 없이 안전망으로 끝났다 — 클립 이벤트가 빠졌거나 상태가 재생되지 않았다.</summary>
    public static void WarnIfFallbackEnded(SkillEndReason reason, string skill, string clip,
        SkillAnimationEventType eventType, Object context)
    {
#if UNITY_EDITOR
        if (reason != SkillEndReason.MaxDurationReached)
            return;

        Debug.LogWarning($"[Gunner/{skill}] {eventType} 애니 이벤트 없이 안전망(maxActiveDuration)으로 끝났다 — " +
                         $"클립 '{clip}' 의 HandleSkillEvent({eventType}={(int)eventType}) 확인: Tools/Player/Gunner/스킬 클립 애니 이벤트 검사", context);
#endif
    }
}
