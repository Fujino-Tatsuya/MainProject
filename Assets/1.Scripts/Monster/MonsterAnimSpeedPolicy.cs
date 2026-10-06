using UnityEngine;

/// <summary>
/// 몬스터 <c>animator.speed</c> 를 정하는 규칙(PLAN-monster-anim-speed S2·S4).
///
/// 🔴 기준은 <b>지금 재생 중인 애니 상태</b>지 로직 상태가 아니다. GauntletBot 은 펀치 예고 동안
///    로직 상태가 이미 Attack 인데 화면엔 아직 대기·이동 애니가 나온다 — 로직 상태로 정하면 그 애니가
///    공격 배율로 빨라진다(Codex 설계 회의 10-02).
///
/// | 재생 중                         | 결과                                           |
/// |---|---|
/// | 이동 블렌드 + 움직이는 중         | 실제 속도 ÷ 지금 발이 나가는 속도, 범위 제한      |
/// | 이동 블렌드 + 서 있음 / 맞춤 끔   | 1                                              |
/// | 그 밖 + 로직 Attack              | 몬스터 고유 공격속도(attackSpeed)                |
/// | 그 밖(피격·그로기·사망)           | 1                                              |
///
/// 입력이 전부 복제된 값(상태·이동 속도) + SO 상수라 각 피어가 같은 값을 낸다 — 따로 복제하지 않는다.
/// </summary>
public static class MonsterAnimSpeedPolicy
{
    /// <summary>이 속도(m/s) 이하면 서 있는 것으로 본다 — 멈추는 순간 클립이 거의 정지하는 것을 막는다.</summary>
    public const float MovingThreshold = 0.1f;

    /// <summary>이동 블렌드(대기 ↔ 이동 클립 1D) 측정값. 전부 SO 에서 온다(측정 도구가 기록).</summary>
    public struct Locomotion
    {
        public float clipSpeed;        // W: 이동 클립 100%·재생 1 에서 발이 나가는 속도(m/s). 0 = 맞춤 끔
        public float fullBlendSpeed;   // th: 이동 클립 비중이 100% 가 되는 블렌드 값(m/s)
        public float idleCycle;        // 대기 클립 한 주기(초, 블렌드 timeScale 반영). 0 = 모름(주기 보정 생략)
        public float moveCycle;        // 이동 클립 한 주기(초, 블렌드 timeScale 반영). 0 = 모름
        public Vector2 range;          // 재생 속도 하한·상한
        public bool fullBlendWhileMoving; // 움직이는 동안 블렌드 값을 th 이상으로 보낸다 → 이동 클립 100%
        public float playbackScale;       // 발 맞춤 결과에 곱함(1 = 정확히 맞춤). 0 이하 = 1 로 본다(구조체 기본값 보호)
    }

    /// <summary>
    /// Animator 의 이동 블렌드 파라미터에 넣을 값. 기본은 실제 속도 그대로.
    /// <c>fullBlendWhileMoving</c> 면 움직이는 동안 max(v, th) — 이동 클립만 재생된다(섞임으로 인한 슬로모션 제거).
    /// </summary>
    public static float BlendParam(float moveSpeed, in Locomotion loco) =>
        loco.fullBlendWhileMoving && moveSpeed > MovingThreshold
            ? Mathf.Max(moveSpeed, loco.fullBlendSpeed)
            : moveSpeed;

    public static float Resolve(bool playingLocomotion, float moveSpeed, bool logicAttacking, float attackSpeed, in Locomotion loco)
    {
        if (playingLocomotion)
            return LocomotionSpeed(moveSpeed, loco);

        if (logicAttacking)
            return Mathf.Max(0.05f, attackSpeed);

        return 1f;
    }

    /// <summary>
    /// 재생 속도 1 에서 지금 발이 나가는 속도(m/s).
    ///
    /// 1D 블렌드는 자식 클립의 <b>정규화 시간을 맞춘다</b> — 섞인 상태의 한 주기 = w·Lm + (1−w)·Li.
    /// 그래서 이동 클립은 Lm / (w·Lm + (1−w)·Li) 배로 느려지고, 비중 w 만큼만 섞인다:
    ///   foot(v) = w · W · Lm / (w·Lm + (1−w)·Li),  w = clamp01(v / th)
    /// 대기 클립이 길면(ChompBot 3.8초 vs 이동 0.4초) 이 감속이 커서 "제자리 슬로모션"으로 미끄러진다.
    /// 주기를 모르면(0) 같은 주기로 보고 w·W 로 근사한다.
    /// </summary>
    public static float FootSpeed(float moveSpeed, in Locomotion loco)
    {
        float blend = BlendParam(moveSpeed, loco);   // Animator 가 실제로 받는 블렌드 값 기준
        float w = loco.fullBlendSpeed > 0f ? Mathf.Clamp01(blend / loco.fullBlendSpeed) : 1f;
        float cycleFactor = 1f;
        if (loco.idleCycle > 0f && loco.moveCycle > 0f)
            cycleFactor = loco.moveCycle / (w * loco.moveCycle + (1f - w) * loco.idleCycle);
        return w * loco.clipSpeed * cycleFactor;
    }

    public static float LocomotionSpeed(float moveSpeed, in Locomotion loco)
    {
        if (loco.clipSpeed <= 0f || moveSpeed <= MovingThreshold)
            return 1f;

        float foot = FootSpeed(moveSpeed, loco);
        if (foot <= 0f) return 1f;

        float min = Mathf.Max(0.05f, Mathf.Min(loco.range.x, loco.range.y));
        float max = Mathf.Max(min, Mathf.Max(loco.range.x, loco.range.y));
        // playbackScale: 발 맞춤 값에 곱한 뒤 범위로 자른다. 아래 w 가중을 그대로 타므로 멈추는 순간(w→0) 1 로 이어진다.
        float scale = loco.playbackScale > 0f ? loco.playbackScale : 1f;
        float corrected = Mathf.Clamp(moveSpeed / foot * scale, min, max);

        // 보정은 이동 클립 비중 w 만큼만 건다: 1 + (보정 − 1)·w.
        // 🔴 대기 클립이 섞인 저속 구간(출발 가속·도착 감속·군중 밀림)에서 대기 모션까지 상한(2.5)으로 돌고,
        //    MovingThreshold 경계에서 1 ↔ 2.5 로 튀던 것을 막는다(10-03 교차검증 Claude, 팀장 승인).
        //    평소 이동은 블렌드 임계값보다 빨라 w = 1 — 보정이 그대로 걸린다.
        float w = loco.fullBlendSpeed > 0f ? Mathf.Clamp01(BlendParam(moveSpeed, loco) / loco.fullBlendSpeed) : 1f;
        return 1f + (corrected - 1f) * w;
    }
}
