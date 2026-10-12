/// <summary>
/// 튜토리얼 보스 길(S5)의 순수 규칙 — 타이머 자동 시작·제한시간·HUD 숨김·보스 진입 패드 잠금. Unity·네트워크 비의존(EditMode 테스트 대상).
/// 런타임 판별(마커·디렉터 상태)은 <see cref="TutorialBossPath"/> 가 모아 여기로 넘긴다.
/// 원본: Docs/design/tutorial-scenario.md §3.7~§3.9·§4 · PLAN-tutorial.md S5 · Docs/tech/tutorial-director.md §9.
///
/// 🔴 만료 강제 이동(<see cref="BossTimerManager"/> → <see cref="BossTeleportManager.ForceStartEncounter"/>)은 이 규칙을 하나도 보지 않는다 —
///    패드 잠금은 패드 점유(<see cref="PadOccupied"/>)만 끈다. 잠긴 패드는 카운트다운을 만들지 않으므로 강제 이동을 '진행 중'으로 막지도 못한다.
/// </summary>
public static class TutorialBossPathRules
{
    /// <summary>튜토리얼 제한시간(D6) — 존4 처치량·이동·복도4 특성 선택 추정 250~330초 + 여유.</summary>
    public const float DefaultTutorialTimerLimitSeconds = 420f;

    /// <summary>맵 생성 직후 타이머를 자동으로 시작하는가. 튜토리얼이면 끈다 — G8(존4 설정 자막 뒤)에서 서버가 시작한다(D1).</summary>
    public static bool ShouldAutoStartTimer(bool isTutorial) => !isTutorial;

    /// <summary>
    /// 이번 판의 제한시간. 시작 API 가 덮어쓴 값(&gt; 0)이 있으면 그것, 아니면 씬(인스펙터) 값.
    /// 비튜토리얼은 덮어쓰지 않으므로 씬 값(600 · 코드 기본 300) 그대로다.
    /// </summary>
    public static float EffectiveLimitSeconds(float overrideSeconds, float sceneSeconds) =>
        overrideSeconds > 0f ? overrideSeconds : sceneSeconds;

    /// <summary>
    /// 타이머 HUD 를 숨기는가. 튜토리얼에서 <b>아직 Idle 이고 '표시'를 켜지 않았을 때만</b>(§4 — Z4-0 에서 처음 나타남).
    /// 비튜토리얼은 기존대로 Idle 에도 빈 틀이 보인다. 시작 뒤(Running·Expired·Stopped)는 항상 보인다.
    /// </summary>
    public static bool ShouldHideTimerHud(bool isTutorial, BossTimerManager.TimerState state, bool revealed) =>
        isTutorial && state == BossTimerManager.TimerState.Idle && !revealed;

    /// <summary>보스 진입 패드가 켜져 있는가. 튜토리얼이면 G10(복도4 특성 전원 선택)이 열린 뒤만(D3·D17).</summary>
    public static bool IsPadEnabled(bool isTutorial, bool g10Open) => !isTutorial || g10Open;

    /// <summary>패드 점유로 보고할 값 — 잠긴 패드는 사람이 서 있어도 비어 있는 것으로 본다(카운트다운·색 전환 없음).</summary>
    public static bool PadOccupied(bool anyoneInside, bool padEnabled) => anyoneInside && padEnabled;

    /// <summary>엘리베이터 문을 여는가 — 튜토리얼이면 G10 과 함께. 비튜토리얼은 손대지 않는다(기존 기본 상태 Closed 그대로).</summary>
    public static bool ShouldOpenElevatorDoor(bool isTutorial, bool g10Open) => isTutorial && g10Open;
}
