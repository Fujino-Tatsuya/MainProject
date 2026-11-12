using UnityEngine;

/// <summary>
/// 보스 처치 연출 — 처치자 이동 방식(할 일 272). 프로필 칸 <see cref="BossVictoryProfile.killerMove"/> 가 고른다(캐릭터 분기 대신 데이터).
/// </summary>
public enum BossVictoryKillerMove
{
    BackDash,       // 백대쉬(기존 — 팔라딘) → 처치 연출 스킬
    TeleportDash,   // 순간이동 → Q 한 번으로 도착 지점까지(어쌔신) — 두 점 = 보스 기준 오프셋
    RetreatCharge,  // 제자리. 보스와 가까우면 후퇴 스킬(E) 표시 + 백대쉬 → Q 차징, 멀면 바로 Q 차징(거너)
}

/// <summary>순간이동 Q 이동 구간(클립 이벤트 기준, 할 일 272). A = Custom0 → End(결정), B = Custom0 → 정지 프레임(나중에 바꿀 수 있게).</summary>
public enum BossVictoryMoveInterval
{
    Custom0ToEnd,
    Custom0ToFreeze,
}

/// <summary>
/// 보스 처치 연출 처치자 이동(할 일 272) 순수 계산 — 테스트 <c>BossVictoryKillerMoveTests</c>.
/// 런타임(<see cref="BossVictoryGlitchPresentation"/>)과 타임라인 창 Scene 뷰 핸들이 같은 식을 쓴다.
/// </summary>
public static class BossVictoryKillerMoves
{
    /// <summary>어쌔신 정지 프레임 기본(Combo_Attack_03_04 60fps 71프레임 = 1.1833초) — 타임라인 '처치 연출 스킬' 클립 끝 자르기로 저장한다.</summary>
    public const int DefaultFreezeFrame = 71;

    /// <summary>순간이동 Q 기본 애니 상태 = Combo_Attack_03_04 클립을 든 상태(AssassinAnimatorController).</summary>
    public const string DefaultTeleportMoveState = "Assassin_Q_DashStrike_Transformed";

    /// <summary>
    /// 처치자 단계 시각 — 이동 방식별로 '백대쉬 시작' 단계(= 백대쉬 / 순간이동 / 후퇴)와 '처치 연출 스킬' 단계 시각을 고른다.
    /// 백대쉬·순간이동 = 마커 그대로. 후퇴 차징 — 가까우면 백대쉬 시작 → 'Q 차징 시작' 마커, 멀면 백대쉬 없음(무한대)·백대쉬 시작 시각에 바로 Q 차징.
    /// </summary>
    public static BossVictoryKillerSequence.Times Times(BossVictoryTimeline tl, BossVictoryKillerMove move, bool tooClose)
    {
        BossVictoryKillerSequence.Times t = tl.KillerTimes();
        if (move != BossVictoryKillerMove.RetreatCharge) return t;
        if (tooClose)
        {
            t.FinisherStart = tl.ChargeStartTime;
            return t;
        }
        t.FinisherStart = tl.backDashStartTime;
        t.BackDashStart = float.PositiveInfinity;
        return t;
    }

    /// <summary>후퇴 차징 — 처치 순간 보스와 수평 거리가 <paramref name="minDistance"/> 보다 짧은가(같으면 멂).</summary>
    public static bool IsTooClose(Vector3 killer, Vector3 boss, float minDistance)
    {
        Vector3 d = killer - boss;
        d.y = 0f;
        return d.sqrMagnitude < minDistance * minDistance;
    }

    /// <summary>보스 수평 회전(y 축만) — 기울어진 보스여도 오프셋은 바닥 평면 기준.</summary>
    public static Quaternion Yaw(Quaternion rotation) => Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);

    /// <summary>보스 기준 점 = 보스 위치 + 보스 수평 회전 × 로컬 오프셋.</summary>
    public static Vector3 BossPoint(Vector3 bossPosition, Quaternion bossRotation, Vector3 localOffset) =>
        bossPosition + Yaw(bossRotation) * localOffset;

    /// <summary><see cref="BossPoint"/> 의 역 — 월드 점 → 보스 로컬 오프셋(Scene 뷰 핸들로 끌 때).</summary>
    public static Vector3 BossLocalOffset(Vector3 bossPosition, Quaternion bossRotation, Vector3 worldPoint) =>
        Quaternion.Inverse(Yaw(bossRotation)) * (worldPoint - bossPosition);

    /// <summary>
    /// 순간이동 Q 이동 구간(클립 시간 초). 시작 = Custom0(없으면 0). 끝 = A: End(없으면 정지 프레임) / B: 정지 프레임(0 이하면 End).
    /// 끝이 시작보다 앞이면 시작(그 자리에서 바로 도착).
    /// </summary>
    public static void MoveInterval(float custom0, float end, float freeze, BossVictoryMoveInterval mode, out float start, out float stop)
    {
        start = Mathf.Max(0f, custom0);
        float a = end >= 0f ? end : freeze;
        float b = freeze > 0f ? freeze : end;
        stop = mode == BossVictoryMoveInterval.Custom0ToFreeze ? b : a;
        if (!(stop > start)) stop = start;
    }

    /// <summary>이동 진행(0~1, 곡선 적용) — 클립 시간이 구간 앞이면 0, 뒤면 곡선 끝 값. 곡선이 없으면 선형.</summary>
    public static float MoveProgress(float clipTime, float start, float stop, AnimationCurve ease)
    {
        float u = stop > start ? Mathf.Clamp01((clipTime - start) / (stop - start)) : (clipTime >= start ? 1f : 0f);
        return Ease(ease, u);
    }

    /// <summary>정지 프레임 → 클립 시간(초).</summary>
    public static float FreezeSeconds(int frame, float frameRate) => Mathf.Max(0, frame) / BossVictoryAnimTrim.FrameRate(frameRate);

    /// <summary>거너 처치 연출 차징 구체 크기 = 시작 크기 → 목표 크기(성장 곡선, 진행 <paramref name="t"/> 0~1). t = 1(발사 순간) = 목표 크기.</summary>
    public static float ChargeScale(float from, float to, AnimationCurve growth, float t) =>
        Mathf.LerpUnclamped(from, to, Ease(growth, Mathf.Clamp01(t)));

    static float Ease(AnimationCurve curve, float u) =>
        curve != null && curve.length > 0 ? curve.Evaluate(u) : u;
}
