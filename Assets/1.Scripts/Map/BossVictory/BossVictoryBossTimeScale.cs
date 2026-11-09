/// <summary>
/// 보스 처치 연출 — 보스 시간 배율 순수 함수(255 후속 5, 테스트 <c>BossVictoryBossTimeScaleTests</c>). 애니·이펙트·서버 사망 표현 대기가 같은 값을 읽는다.
/// 처치 순간 0(마지막 피격 자세) → 노드들이 시각 순으로 상태를 바꾸고 <b>시각이 가장 늦은(지난) 노드가 이긴다</b>(같은 시각이면 피격 애니 &lt; 정지 &lt; 슬로우):
/// '보스 피격 애니 시작'(상태가 있으면) = 1 · '보스 정지'(켜짐) = 0 · '보스 슬로우'(켜짐, 꺼짐이면 처치자 느려짐 순간) = End 후 배율.
/// </summary>
public static class BossVictoryBossTimeScale
{
    /// <summary>보스 노드 시각(초, 처치 판정 뒤 글리치 시계). 정지·슬로우 0 = 끔.</summary>
    public struct Nodes
    {
        public bool HasHitAnim;   // 피격 애니 상태 이름이 있다(비면 마지막 자세 그대로 — 피격 애니 노드 없음)
        public float HitAt;
        public float FreezeAt;
        public float SlowAt;
    }

    /// <summary>
    /// 시각 <paramref name="t"/> 의 보스 배율. <paramref name="killerSlowAt"/> = 처치자 느려짐이 온 시각(아직이면 음수) — 보스 슬로우 마커가 꺼졌을 때 그 순간을 슬로우로 쓴다.
    /// </summary>
    public static float Scale(in Nodes nodes, float killerSlowAt, float slowScale, float t)
    {
        float scale = 0f;
        float at = float.NegativeInfinity;
        if (nodes.HasHitAnim) Apply(nodes.HitAt, 1f, t, ref at, ref scale);
        if (nodes.FreezeAt > 0f) Apply(nodes.FreezeAt, 0f, t, ref at, ref scale);
        float slow = nodes.SlowAt > 0f ? nodes.SlowAt : killerSlowAt;
        if (slow >= 0f) Apply(slow, slowScale, t, ref at, ref scale);
        return scale;
    }

    /// <summary>피격 애니를 지금 틀 때인가(아직 안 틀었고 상태가 있고 시각이 지났다).</summary>
    public static bool ShouldPlayHitAnim(in Nodes nodes, float t) => nodes.HasHitAnim && t >= nodes.HitAt;

    // 지난 노드 중 시각이 늦은 쪽(같으면 뒤에 부른 쪽)이 이긴다.
    static void Apply(float nodeAt, float nodeScale, float t, ref float at, ref float scale)
    {
        if (t < nodeAt || nodeAt < at) return;
        at = nodeAt;
        scale = nodeScale;
    }
}
