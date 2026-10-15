using System;
using System.Collections.Generic;

/// <summary>
/// 존2 순수 규칙(S8 → v8 V3) — 기상 지연 · 목표 줄(처치 진행도만).
/// v8: 존2 = 몬스터 전멸만(D13). Q·E 사용 체크·캐릭터별 Q/E 안내·슬롯 강조·넛지는 없다(스킬 교육은 존1 에서 끝남).
/// 원본: Docs/design/tutorial-scenario.md §3.3·§5(존2) · Docs/tech/tutorial-corridors-zone2.md.
/// </summary>
public static class TutorialZone2Rules
{
    /// <summary>파티 첫 진입 → 존2 기상(자막 TUT_Z2_INTRO 가 끝날 즈음, Z2-1). 서버 실제 시간.</summary>
    public const double WakeDelaySeconds = 4d;

    /// <summary>서버 — 첫 진입 시각부터 기상할 때가 됐나.</summary>
    public static bool ShouldWake(double enteredAt, double now) =>
        enteredAt >= 0d && !double.IsNaN(now) && now - enteredAt >= WakeDelaySeconds;
}

/// <summary>존2 목표 줄 TUT_Z2_GOAL — "적 처치 {n}/{total}".</summary>
public static class TutorialZone2Goal
{
    /// <param name="total">장부의 존2 수(<see cref="TutorialDirector.GetCounterTarget"/>).</param>
    public static Dictionary<string, string> Args(int kills, int total)
    {
        int t = Math.Max(0, total);
        int n = Math.Max(0, t > 0 ? Math.Min(kills, t) : kills);
        return new Dictionary<string, string>
        {
            { "n", n.ToString() },
            { "total", t.ToString() },
        };
    }
}
