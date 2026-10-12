using UnityEditor;
using UnityEngine;

/// <summary>
/// 튜토리얼 보스 길 디버그 메뉴(S5). <b>Play 중 호스트에서만</b> 동작한다 — 게이트·타이머는 서버 권한.
/// 실제 호출은 S10(Z4-0 HUD 표시 · 설정 자막 끝 G8)·S8/S12(G10 = 복도4 특성 전원 선택)가 붙인다. 그 전까지 Play 확인용.
/// 게이트는 순서대로만 열리므로 "앞 게이트 강제"는 G1 부터 차례로 연다 — 배리어·존 기상 규칙도 실제와 같이 따라온다.
/// </summary>
internal static class TutorialBossPathDebugMenu
{
    const string Root = "Tools/Tutorial/Debug/";

    static bool Ready()
    {
        TutorialDirector d = TutorialDirector.Instance;
        return Application.isPlaying && d != null && d.IsServer && d.IsTutorialActive;
    }

    [MenuItem(Root + "보스 길 상태 로그", true)]
    [MenuItem(Root + "타이머 HUD 표시", true)]
    [MenuItem(Root + "타이머 시작 (G8 — 앞 게이트 강제)", true)]
    [MenuItem(Root + "G9 열기 (앞 게이트 강제)", true)]
    [MenuItem(Root + "G10 열기 (앞 게이트 강제 — 패드·엘리베이터 문)", true)]
    [MenuItem(Root + "G11 열기 (앞 게이트 강제 — 보스방 도착 = 목숨 차감 시작, 이동·Finished 없음)", true)]
    static bool Validate() => Ready();

    [MenuItem(Root + "보스 길 상태 로그", false, 80)]
    static void LogState()
    {
        TutorialDirector d = TutorialDirector.Instance;
        BossTimerManager t = BossTimerManager.Instance;
        string timer = t != null ? $"{t.State} 남은 {t.RemainingSeconds:0.0}/{t.LimitSeconds:0}초" : "(BossTimerManager 없음)";
        Debug.Log($"[TutorialDebug] 보스 길 — 타이머 {timer} · HUD 표시 {d.IsTimerHudShown} · G8 {d.IsBossTimerStarted} · " +
                  $"G9 {d.IsGateOpen(TutorialGate.G9_Corridor4Entry)} · G10 {d.IsGateOpen(TutorialGate.G10_BossEnterAndPad)} · " +
                  $"G11 {d.IsGateOpen(TutorialGate.G11_BossRoom)} · " +
                  $"패드 {(TutorialBossPath.IsBossPadEnabled ? "활성" : "잠김")} · 단계 {d.Step}");
    }

    [MenuItem(Root + "타이머 HUD 표시", false, 81)]
    static void ShowHud() => TutorialDirector.Instance.ShowTimerHudServer();

    [MenuItem(Root + "타이머 시작 (G8 — 앞 게이트 강제)", false, 82)]
    static void StartTimer() => OpenThrough(TutorialGate.G8_TimerStart);

    [MenuItem(Root + "G9 열기 (앞 게이트 강제)", false, 83)]
    static void OpenG9() => OpenThrough(TutorialGate.G9_Corridor4Entry);

    [MenuItem(Root + "G10 열기 (앞 게이트 강제 — 패드·엘리베이터 문)", false, 84)]
    static void OpenG10() => OpenThrough(TutorialGate.G10_BossEnterAndPad);

    [MenuItem(Root + "G11 열기 (앞 게이트 강제 — 보스방 도착 = 목숨 차감 시작, 이동·Finished 없음)", false, 85)]
    static void OpenG11() => OpenThrough(TutorialGate.G11_BossRoom);

    static void OpenThrough(TutorialGate target)
    {
        TutorialDirector d = TutorialDirector.Instance;
        int opened = 0;
        for (int i = 0; i <= (int)target; i++)
        {
            var g = (TutorialGate)i;
            if (d.IsGateOpen(g)) continue;
            if (!d.TryOpenGateServer(g)) break;
            opened++;
        }
        Debug.Log($"[TutorialDebug] {target} 까지 강제 개방 — 새로 연 게이트 {opened}개.");
    }
}
