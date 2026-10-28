/// <summary>
/// 보스 타이머·진입 패드·타이머 HUD(경석 영역)가 튜토리얼 규칙을 묻는 <b>한 곳</b>(S5). 모든 피어에서 읽을 수 있다.
/// 규칙은 순수 <see cref="MapSceneBossPathRules"/>, 여기는 런타임 값(마커·디렉터 복제 상태)만 모은다.
///
/// 판별 = <see cref="MapSceneStageMarker.IsActive"/>(씬 로드 때 OnEnable — 맵 생성·NetworkObject 스폰 순서와 무관하게 이미 확정)
///        + 씬에 <see cref="MapSceneDirector"/> 가 있음(Awake — 스폰 전에도 있다). 디렉터가 없으면 튜토리얼 진행이 아예 없으므로
///        잠금도 걸지 않는다(타이머를 영원히 못 켜는 교착 방지).
/// 게이트 값(G10 · 타이머 HUD 표시)은 디렉터 복제 값 — 스폰 전에는 닫힘/숨김으로 읽힌다(보수적).
/// </summary>
public static class MapSceneBossPath
{
    /// <summary>튜토리얼 스테이지인가(모든 피어, 스폰 순서 무관).</summary>
    public static bool IsTutorialStage => MapSceneStageMarker.IsActive && MapSceneDirector.Instance != null;

    /// <summary>[서버] 맵 생성 직후 보스 타이머를 자동 시작하는가 — 비튜토리얼만.</summary>
    public static bool ShouldAutoStartTimer => MapSceneBossPathRules.ShouldAutoStartTimer(IsTutorialStage);

    /// <summary>타이머 HUD 를 숨기는가(모든 피어) — 튜토리얼 · Idle · 디렉터 '타이머 HUD 표시' 꺼짐.</summary>
    public static bool ShouldHideTimerHud(BossTimerManager.TimerState state)
    {
        MapSceneDirector d = MapSceneDirector.Instance;
        return MapSceneBossPathRules.ShouldHideTimerHud(IsTutorialStage, state, d != null && d.IsTimerHudShown);
    }

    /// <summary>
    /// 보스 진입 패드가 켜져 있는가(모든 피어) — 판정은 서버 트리거, 표시는 각 피어의 범위 테두리.
    /// 전멸 리스폰 뒤 특성 초기화 팝업·재선택 중인 사람이 있으면 잠긴다(할 일 30 D3, 복제값).
    /// </summary>
    public static bool IsBossPadEnabled
    {
        get
        {
            MapSceneDirector d = MapSceneDirector.Instance;
            TraitDirector traits = TraitDirector.Instance;
            return MapSceneBossPathRules.IsPadEnabled(IsTutorialStage, d != null && d.IsGateOpen(MapSceneGate.G10_BossEnterAndPad),
                traits != null && traits.IsTraitResetInProgress);
        }
    }
}
