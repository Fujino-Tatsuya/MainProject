using System;
using System.Collections.Generic;

/// <summary>
/// 존2 순수 규칙(S8) — 기상 지연 · E 안내 타이밍 · Q/E 넛지 · 목표 줄.
/// 원본: Docs/design/tutorial-scenario.md §3.3·§5(존2) · Docs/tech/tutorial-corridors-zone2.md.
/// </summary>
public static class TutorialZone2Rules
{
    /// <summary>파티 첫 진입 → 존2 기상(자막 TUT_Z2_INTRO 가 끝날 즈음, Z2-1). 서버 실제 시간.</summary>
    public const double WakeDelaySeconds = 4d;
    /// <summary>기상 뒤 이만큼 지나면 Q 를 안 썼어도 E 안내(Z2-2).</summary>
    public const float EPromptAfterWakeSeconds = 10f;
    /// <summary>기상 뒤 이만큼 안에 Q·E 중 안 쓴 것이 있으면 넛지(Z2-4).</summary>
    public const float NudgeAfterWakeSeconds = 15f;

    /// <summary>서버 — 첫 진입 시각부터 기상할 때가 됐나.</summary>
    public static bool ShouldWake(double enteredAt, double now) =>
        enteredAt >= 0d && !double.IsNaN(now) && now - enteredAt >= WakeDelaySeconds;
}

/// <summary>존2 에서 아직 안 쓴 스킬 키.</summary>
[Flags]
public enum TutorialZone2Keys : byte
{
    None = 0,
    Q = 1 << 0,
    E = 1 << 1,
}

/// <summary><see cref="TutorialZone2Guide.Tick"/> 한 번에 띄울 것.</summary>
public readonly struct TutorialZone2Cue
{
    public TutorialZone2Cue(bool showEPrompt, TutorialZone2Keys nudge)
    {
        ShowEPrompt = showEPrompt;
        Nudge = nudge;
    }

    /// <summary>캐릭터별 E 문구(TUT_Z2_E_*) + E 칸 강조를 지금 띄운다.</summary>
    public bool ShowEPrompt { get; }
    /// <summary>넛지(TUT_Z2_NUDGE) 대상 키 — None 이면 안 띄운다.</summary>
    public TutorialZone2Keys Nudge { get; }
}

/// <summary>
/// 로컬 플레이어 존2 안내 시계. 0 = 그 클라가 존2 시작(단계 Zone2)을 본 순간. 기상은 그 뒤 <see cref="TutorialZone2Rules.WakeDelaySeconds"/>.
/// · E 안내 = Q 를 처음 쓴 직후 또는 기상 10초 뒤(먼저 오는 것) — 1번.
/// · 넛지 = 기상 15초 시점에 Q·E 중 안 쓴 것만 — 1번(그때 다 썼으면 안 띄우고 끝).
/// · 강조 = E 안내 전엔 Q 칸, 뒤엔 E 를 쓸 때까지 E 칸.
/// </summary>
public sealed class TutorialZone2Guide
{
    public float Elapsed { get; private set; }
    public bool EPromptShown { get; private set; }
    public bool NudgeDone { get; private set; }

    private static float WakeAt => (float)TutorialZone2Rules.WakeDelaySeconds;

    /// <summary>늦은 클라 — 시간 안내(E 문구·넛지)를 모두 지난 것으로 둔다(지나간 자막 생략).</summary>
    public void SkipTimedCues()
    {
        Elapsed = WakeAt + TutorialZone2Rules.NudgeAfterWakeSeconds;
        EPromptShown = true;
        NudgeDone = true;
    }

    public TutorialZone2Cue Tick(float deltaSeconds, bool usedQ, bool usedE)
    {
        if (deltaSeconds > 0f && !float.IsNaN(deltaSeconds)) Elapsed += deltaSeconds;

        bool showE = false;
        if (!EPromptShown && (usedQ || Elapsed >= WakeAt + TutorialZone2Rules.EPromptAfterWakeSeconds))
        {
            EPromptShown = true;
            showE = true;
        }

        TutorialZone2Keys nudge = TutorialZone2Keys.None;
        if (!NudgeDone && Elapsed >= WakeAt + TutorialZone2Rules.NudgeAfterWakeSeconds)
        {
            NudgeDone = true;
            nudge = Missing(usedQ, usedE);
        }

        return new TutorialZone2Cue(showE, nudge);
    }

    public bool HighlightQ => !EPromptShown;

    public bool HighlightE(bool usedE) => EPromptShown && !usedE;

    public static TutorialZone2Keys Missing(bool usedQ, bool usedE) =>
        (usedQ ? TutorialZone2Keys.None : TutorialZone2Keys.Q) | (usedE ? TutorialZone2Keys.None : TutorialZone2Keys.E);

    /// <summary>넛지 {키} — "Q" · "E" · "Q · E".</summary>
    public static string KeyLabel(TutorialZone2Keys keys) => keys switch
    {
        TutorialZone2Keys.Q => "Q",
        TutorialZone2Keys.E => "E",
        TutorialZone2Keys.Q | TutorialZone2Keys.E => "Q · E",
        _ => string.Empty,
    };

    /// <summary>넛지 {스킬명} — 키 순서대로 이름(없으면 키)을 " · " 로 잇는다.</summary>
    public static string SkillLabel(TutorialZone2Keys keys, string qName, string eName)
    {
        string q = string.IsNullOrEmpty(qName) ? "Q" : qName;
        string e = string.IsNullOrEmpty(eName) ? "E" : eName;
        return keys switch
        {
            TutorialZone2Keys.Q => q,
            TutorialZone2Keys.E => e,
            TutorialZone2Keys.Q | TutorialZone2Keys.E => q + " · " + e,
            _ => string.Empty,
        };
    }
}

/// <summary>존2 목표 줄 TUT_Z2_GOAL — "Q 사용 {q} · E 사용 {e} · 적 처치 {n}/{total}".</summary>
public static class TutorialZone2Goal
{
    // 안내 UI 폰트 정적 아틀라스에 ☐/☑ 가 없어 체크리스트와 같은 □/■ 를 쓴다(tutorial-guide-ui.md).
    public const string CheckedMark = "■";
    public const string UncheckedMark = "□";

    /// <param name="total">장부의 존2 수(<see cref="TutorialDirector.GetCounterTarget"/>).</param>
    public static Dictionary<string, string> Args(bool usedQ, bool usedE, int kills, int total)
    {
        int t = Math.Max(0, total);
        int n = Math.Max(0, t > 0 ? Math.Min(kills, t) : kills);
        return new Dictionary<string, string>
        {
            { "q", usedQ ? CheckedMark : UncheckedMark },
            { "e", usedE ? CheckedMark : UncheckedMark },
            { "n", n.ToString() },
            { "total", t.ToString() },
        };
    }
}
