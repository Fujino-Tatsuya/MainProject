#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 할 일 124 — QA 창 최소화(<see cref="QAWindowMinimizer"/>) 옵션 게이트. 🔴 <b>기본 꺼짐</b> — 배치가 진행 상태 <see cref="StateKey"/> = "1" 로 명시해 켤 때만 최소화한다.
/// 야간 QA(<see cref="QANightlyMenu"/>) 배치가 돌거나 OBS 녹화(<see cref="QANightlyLocalSettings.ObsEnabled"/> — 판 전체 녹화·리플레이 버퍼 모두)가 켜져 있으면
/// 옵션과 무관하게 최소화하지 않고 <c>minimize_skipped reason=nightly|obs</c> 를 남긴다(녹화 화면이 최소화 창이 되지 않게).
/// 배치 쪽 사용: 시작 때 <c>Set(QAWindowMinimizePolicy.StateKey, "1")</c> → 최소화 지점에서 <see cref="MinimizeMainEditor"/>·<see cref="MinimizeVirtualPlayers"/> 에 그 값을 넘긴다.
/// </summary>
public static class QAWindowMinimizePolicy
{
    private const string Tag = "[QA103/Window]";

    /// <summary>배치 진행 상태 키 — "1" 이면 켬, 없거나 그 밖이면 꺼짐.</summary>
    public const string StateKey = "minimize";

    public const string ReasonNightly = "nightly";
    public const string ReasonObs = "obs";
    public const string ReasonOptionOff = "option_off";
    public const string ReasonOptionOn = "option_on";

    public readonly struct Decision
    {
        public readonly bool Minimize;
        public readonly string Reason;

        public Decision(bool minimize, string reason)
        {
            Minimize = minimize;
            Reason = reason;
        }
    }

    /// <summary>최소화 여부와 사유(순수 — EditMode 테스트). 야간 → OBS → 옵션 순으로 막는다.</summary>
    public static Decision Decide(bool optionOn, bool nightlyRunning, bool obsEnabled)
    {
        if (nightlyRunning)
            return new Decision(false, ReasonNightly);
        if (obsEnabled)
            return new Decision(false, ReasonObs);
        return optionOn ? new Decision(true, ReasonOptionOn) : new Decision(false, ReasonOptionOff);
    }

    /// <summary>진행 상태 값 → 옵션(기본 꺼짐).</summary>
    public static bool IsOptionOn(string stateValue) => stateValue == "1";

    public static bool IsOptionOn(IReadOnlyDictionary<string, string> state) =>
        state != null && state.TryGetValue(StateKey, out string v) && IsOptionOn(v);

    public static int MinimizeMainEditor(bool optionOn, string why) =>
        Allowed(optionOn, "main", why) ? QAWindowMinimizer.MinimizeMainEditor(why) : 0;

    public static int MinimizeVirtualPlayers(bool optionOn, string why) =>
        Allowed(optionOn, "virtual_players", why) ? QAWindowMinimizer.MinimizeVirtualPlayers(why) : 0;

    private static bool Allowed(bool optionOn, string target, string why)
    {
        Decision d = Decide(optionOn, IsNightlyRunning(), QANightlyHooks.LoadLocal().ObsEnabled);
        // 옵션 꺼짐(기본)은 조용히 넘긴다 — 야간·OBS 는 옵션과 무관하게 남긴다.
        if (!d.Minimize && d.Reason != ReasonOptionOff)
            Debug.Log($"{Tag} event=minimize_skipped reason={d.Reason} target={target} why={why}");
        return d.Minimize;
    }

    /// <summary>연속 루프가 돌고 그 배치가 야간 배치(batch.txt 에 야간 시드 이름)인가.</summary>
    private static bool IsNightlyRunning() =>
        QABoss103BatchRunner.IsRunning && QANightlyHooks.IsNightly(QANightlyHooks.ReadBatch(QABoss103BatchRunner.CurrentDir));
}
#endif
