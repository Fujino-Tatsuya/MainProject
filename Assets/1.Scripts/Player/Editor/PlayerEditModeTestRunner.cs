using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 플레이어·코어(상태이상·보호막·스킬 장부·어쌔신) EditMode 테스트를 메뉴 한 번으로 돌리고 결과를 콘솔에 남긴다.
/// 테스트는 asmdef 없는 <c>Assembly-CSharp-Editor</c> 에 있어 픽스처 이름 정규식으로 고른다.
/// 패턴은 <see cref="DataTableEditModeTestRunner"/> 를 따랐다. 픽스처가 늘면 <see cref="Fixtures"/> 에만 추가한다.
/// </summary>
internal static class PlayerEditModeTestRunner
{
    static readonly string[] Fixtures =
    {
        "^StatusEffectImmunityTests$", // PLAN-assassin A1 — SuperArmor 의미 통일
        "^PlayerSkillCooldownLedgerTests$", // PLAN-assassin A2 — 스킬 단위 쿨 장부·슬롯 교체
        "^PlayerGroundPointProjectionTests$", // PLAN-assassin A4 — 고정 거리 조준·서버 재투영
        "^AssassinComboModelTests$", // PLAN-assassin A6 — 평타 순서·0.8초 유예·스킬 리셋
        "^AssassinLungeModelTests$", // 할 일 19 — 일반 E 강타 전진 거리 분배
        "^AssassinStateModelTests$", // PLAN-assassin A7 — 분노 게이지(충전·상한·최소량·감소·보존)·변신 지속·해제 2초·종료 대기·쓰러짐 정리
        "^AssassinDashStrikeModelTests$", // PLAN-assassin A8 — Q 경로 대상 1회·쿨 차감 1회·보고 거리 상한
        "^AssassinCircleStrikeModelTests$", // PLAN-assassin A9 — 변신 E 원 판정·타당 1회·5타·무적 구간·종료 대기
        "^BackAttackRulesTests$", // PLAN-assassin A11 — 보스 후방 판정·변신 강제·배율
        "^HealthShieldTests$",
        "^GunnerHeatModelTests$",
        "^HealthVignetteModelTests$", // 로컬 체력 비네팅 — 시작 비율·맥동·피격 플래시 색·사망 어둡게(맥동·플래시 끔)
        "^ShieldVignetteModelTests$", // 로컬 실드 비네팅 — 표시 여부·고정 알파 페이드 인/아웃
        "^CombatHudLayerRulesTests$", // CombatHUD 상태 레이어 — 생명주기 상태별 표시 레이어
        "^InterruptHighlightModelTests$", // 인터럽트 강조(할 일 5) — 창 열림 × 사거리 → RMB 슬롯 발광·페이드·맥동
        "^InterruptOmniRangeModelTests$", // 인터럽트 강조 사거리 — 방향 무관 판정 형상 도달 거리 × 대상 콜라이더 표면 거리
        "^OverheadDashIconRulesTests$", // 할 일 29 — 머리 위 대시 아이콘 표시(로컬·생존)·준비 색, 에임 대시 틴트 토글(기본 꺼짐)
        "^InterruptSuccessCooldownModelTests$", // 할 일 26 — 간파 성공 쿨 감소(전체 쿨 × 비율·0 클램프·시전당 1회)
        "^PartyLivesRulesTests$", // 할 일 30 (1) — 파티 공용 목숨: 사망 목적지·공용 차감·재충전·공용 0 Soul 전멸 판정
        "^PartyWipeRespawnRulesTests$", // 할 일 30 (2) — 전멸 → 리스폰 분기(결과 화면 아님)·리스폰 조건·지점 선택(패드 제외·간격·폴백)·목숨 5
        "^InteractPromptSlotTests$", // 상호작용 F 프롬프트 — 마지막 Show 우선·소유자만 Hide·다른 뷰로 Show 시 이전 뷰 숨김·누락·파괴 정리
        "^UnitWallImpactJudgeTests$", // 할 일 32 — 공용 벽 충돌: 스윕 결과 → 벽 충돌·제외 규칙(자기·배리어·유닛·상자·시작 겹침)·막힘 거리·에피소드당 1회
        // 튜토리얼 픽스처(특성·안내 UI·스피너봇 연습)는 TutorialEditModeTestRunner 로 옮겼다(할 일 15 I0).
    };

    const string Tag = "[PlayerTests]";

    static TestRunnerApi _runner;

    [MenuItem("Tools/Tests/플레이어 EditMode 테스트 실행")]
    static void Run()
    {
        if (_runner != null)
        {
            Debug.LogWarning($"{Tag} 이미 실행 중이다 — 끝날 때까지 기다릴 것.");
            return;
        }

        _runner = ScriptableObject.CreateInstance<TestRunnerApi>();
        _runner.RegisterCallbacks(new Callbacks());
        _runner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, groupNames = Fixtures }));
    }

    sealed class Callbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }

        public void RunFinished(ITestResultAdaptor result)
        {
            string summary =
                $"{Tag} {result.TestStatus} — 통과 {result.PassCount} / 실패 {result.FailCount} / " +
                $"건너뜀 {result.SkipCount} ({result.Duration:F3}s)";

            // 통과 0건은 "실패 0건"과 다르다 — 필터가 아무것도 못 잡아도 TestStatus 는 Passed 가 된다.
            if (result.FailCount > 0 || result.PassCount == 0)
                Debug.LogError(summary + (result.PassCount == 0 ? "  ⚠️ 통과 0건 — 필터가 안 걸렸는지 확인" : ""));
            else
                Debug.Log(summary);

            Object.DestroyImmediate(_runner);
            _runner = null;
        }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (result.TestStatus != TestStatus.Failed) return;

            Debug.LogError($"{Tag} FAIL {result.FullName}\n{result.Message}\n{result.StackTrace}");
        }
    }
}
