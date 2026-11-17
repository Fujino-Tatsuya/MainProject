using UnityEngine;

/// <summary>
/// 보스 처치 연출 '손 뻗기·걷기'(11-17) — 상체 = 손 뻗기(23호 magneticgrab, 상체 마스크 레이어), 하체 = 걷기(0층).
/// 23호 컨트롤러에 넣는 이름(저작 메뉴 <c>Tools/Boss Victory/보스 손 뻗기·걷기 레이어 만들기</c>)과 상체 레이어 가중치 순수 함수(테스트 <c>BossVictoryBossReachTests</c>).
/// </summary>
public static class BossVictoryReachRig
{
    public const string UpperLayer = "VictoryUpper";
    public const string UpperState = "VictoryReach";
    public const string UpperSpeedParam = "VictoryUpperSpeed";
    public const string LowerState = "VictoryWalk";
    public const string WalkSpeedParam = "VictoryWalkSpeed";
    /// <summary>Boss_23_walk 디딘 발 속도(m/s, 애니 배율 1) — 11-17 측정(BossVictoryReachAuthoring.MeasureWalkGroundSpeed).</summary>
    public const float MeasuredWalkGroundSpeed = 2.4f;

    /// <summary>
    /// 시각 <paramref name="t"/> 의 상체 레이어 가중치 — 손 뻗기 시작부터 <paramref name="fade"/> 초 동안 0→1,
    /// 피격 애니가 같은 시각이나 뒤에 시작했으면(나중에 시작한 쪽이 이김) 그때부터 <paramref name="hitFade"/> 초 동안 1→0. 손 뻗기를 안 틀었으면 0.
    /// </summary>
    public static float UpperWeight(float t, bool reachPlayed, float reachAt, float fade, bool hitPlayed, float hitAt, float hitFade)
    {
        if (!reachPlayed) return 0f;
        float w = Ramp(t - reachAt, fade);
        if (hitPlayed && hitAt >= reachAt) w *= 1f - Ramp(t - hitAt, hitFade);
        return w;
    }

    static float Ramp(float elapsed, float fade) => fade <= 0f ? (elapsed >= 0f ? 1f : 0f) : Mathf.Clamp01(elapsed / fade);
}
