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
                         $"클립 '{clip}' 의 HandleSkillEvent({(int)eventType}) 확인: Tools/Player/Gunner/스킬 클립 애니 이벤트 검사", context);
#endif
    }
}

/// <summary>
/// 거너 우클릭 후폭풍(반동) 진행 (PLAN-anim-event-timing P3-2 ①). 시전 때 거리·시간을 실어 두고(<see cref="Arm"/>),
/// 이 피어 애니메이터의 Hit 이벤트로 출발한다(<see cref="Release"/>). Hit 이 안 오면 움직이지 않고 안전망으로 끝난다 — 타이머 없음.
/// </summary>
public struct GunnerRecoilModel
{
    private float remaining;
    private float speed;
    private bool isArmed;
    private bool isReleased;

    public bool IsMoving => isReleased && remaining > 0f;

    public void Arm(float distance, float duration)
    {
        remaining = Mathf.Max(0f, distance);
        speed = duration > 0f ? remaining / duration : 0f;
        isArmed = true;
        isReleased = false;
    }

    /// <summary>Hit 수신. 시전 전·종료 뒤(<see cref="Clear"/>)의 Hit 은 무시한다. 중복 Hit 은 무해.</summary>
    public void Release()
    {
        if (isArmed)
            isReleased = true;
    }

    /// <summary>이번 고정 틱에 밀려날 거리. 출발 전이거나 다 밀렸으면 0.</summary>
    public float Step(float deltaTime)
    {
        if (!IsMoving)
            return 0f;

        float step = Mathf.Min(speed * Mathf.Max(0f, deltaTime), remaining);
        remaining -= step;
        return step;
    }

    public void Clear()
    {
        remaining = 0f;
        isArmed = false;
        isReleased = false;
    }
}
