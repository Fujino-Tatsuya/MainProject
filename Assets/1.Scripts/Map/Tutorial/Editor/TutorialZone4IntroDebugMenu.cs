using UnityEditor;
using UnityEngine;

/// <summary>
/// 4구역 진입 전 소개 연출 디버그 메뉴(PLAN-zone4-intro-cinematic) — <b>Play 중 호스트에서만</b>.
/// 구간 스킵(Alt+6)은 스킵 중이라 연출 없이 G7 을 연다 — 연출을 보려면 Alt+5(복도3)로 간 뒤 이 메뉴로 특성 라운드를 **정식 경로**로 끝낸다
/// (전원 무작위 자동 획득 → 전원 완료 → G7 보류·준비 → 살아 있는 전원이 복도3 이면 연출 시작).
/// </summary>
internal static class TutorialZone4IntroDebugMenu
{
    const string Root = "Tools/Tutorial/Debug/";
    const string FinishTraits = Root + "4구역 소개 연출 — 복도3 특성 전원 완료(정식 경로)";
    const string LogState = Root + "4구역 소개 연출 — 상태 로그";

    static bool Ready()
    {
        TutorialDirector d = TutorialDirector.Instance;
        return Application.isPlaying && d != null && d.IsServer && d.IsTutorialActive;
    }

    const string BootMap = Root + "4구역 소개 연출 — Dev Boot 로 4.MapScene Play(툴바 Dev Boot 와 같음)";

    [MenuItem(BootMap, true)]
    static bool ValidateBoot() => !Application.isPlaying && DevBootLauncher.CanLaunch;

    [MenuItem(BootMap, false, 89)]
    static void Boot() => DevBootLauncher.Launch(DevBootTarget.DefaultScenePath);

    const string VoteAll = Root + "4구역 소개 연출 — 접속자 전원 스킵 동의(서버)";

    [MenuItem(VoteAll, false, 92)]
    static void VoteAllSkip()
    {
        TutorialZone4IntroFlow flow = TutorialDirector.Instance.GetComponent<TutorialZone4IntroFlow>();
        if (flow == null) { Debug.LogWarning("[Zone4IntroDebug] TutorialZone4IntroFlow 가 없다."); return; }
        flow.DebugVoteAllServer();
    }

    [MenuItem(VoteAll, true)]
    [MenuItem(FinishTraits, true)]
    [MenuItem(LogState, true)]
    static bool Validate() => Ready();

    [MenuItem(FinishTraits, false, 90)]
    static void FinishCorridor3Traits()
    {
        TutorialCorridorFlow corridor = TutorialDirector.Instance.GetComponent<TutorialCorridorFlow>();
        if (corridor == null)
        {
            Debug.LogWarning("[Zone4IntroDebug] TutorialCorridorFlow 가 디렉터에 없다.");
            return;
        }
        int auto = corridor.SkipRoundServer(3);
        Debug.Log($"[Zone4IntroDebug] 복도3 특성 라운드 정식 완료 — 자동 획득 {auto}명.");
    }

    [MenuItem(LogState, false, 91)]
    static void Log()
    {
        TutorialDirector d = TutorialDirector.Instance;
        TutorialDirector.Zone4IntroState s = d.Zone4Intro;
        double now = Unity.Netcode.NetworkManager.Singleton.ServerTime.Time;
        Debug.Log($"[Zone4IntroDebug] 단계 {s.Phase} · 회차 {s.Sequence} · 경과 {s.Elapsed(now):0.00}초 · 복귀 {s.ReturnAt:0.00}+{s.ReturnSeconds:0.00} · " +
                  $"표 {s.Votes}/{s.Voters} · G7 열림 {d.IsGateOpen(TutorialGate.G7_Zone4Entry)} · 보류 {d.IsGateHeldServer(TutorialGate.G7_Zone4Entry)} · " +
                  $"준비 {d.IsGateReadyHeldServer(TutorialGate.G7_Zone4Entry)} · 튜토리얼 단계 {d.Step}");
    }
}
