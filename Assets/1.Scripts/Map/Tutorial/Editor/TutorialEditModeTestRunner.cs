using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 튜토리얼 EditMode 테스트를 메뉴 한 번으로 돌리고 결과를 콘솔에 남긴다.
/// 패턴은 <see cref="TrainingDummyEditModeTestRunner"/> 를 따랐다. 튜토리얼 단계(S2~)의 새 픽스처는 여기에 추가한다.
/// </summary>
internal static class TutorialEditModeTestRunner
{
    static readonly string[] Fixtures =
    {
        "^TutorialFlowModelTests$",
        "^TutorialAreaTrackerTests$",
        "^TutorialBarrierRulesTests$",
        "^TutorialBackBarrierRulesTests$", // 개인 뒤 배리어(15 후속) — 지남 판정·마스크 유지·로컬 콜라이더 목록
        "^TutorialMonsterLedgerTests$",
        "^TutorialBossPathRulesTests$",
        "^TutorialDeathRulesTests$", // 사망 규칙(S11) + 부활 지점 — 뒤 배리어 예외(C2)·보스 진입 패드 제외·G10 앞 폴백(C3)
        "^TutorialBossRetryTests$", // 할 일 30 (2) 전멸 리스폰 — 보스 재도전 되감기(G11 닫기·BossEnter)·뒤 배리어 마스크 재계산(복도4 최전방)
        "^TutorialTraitSelectionModelTests$", // 특성(T1) — 후보 뽑기·중복 제외·선택 검증·자동 획득·라운드 완료
        "^TutorialTraitWindowModelTests$", // 특성 창(T2) — 내 제시 번호(OfferSeq)로 열림·닫힘·닫힌 제시 재표시 금지·다른 사람 재선택에 안 닫힘·획득 토스트·문구
        "^TutorialTraitDeadlineModelTests$", // 특성 창 교착 방지 게이지(15 후속) — 비율·10초 강조·로컬 감소·재동기화
        "^TutorialTraitResetModelTests$", // 할 일 30 (3) 전멸 리스폰 뒤 특성 초기화 — 팝업 응답 검증·원복 장부·4라운드 재선택·재전멸·이탈
        "^TutorialGuide(Text|SubtitleQueue|Model)Tests$", // 안내 UI(S6) — 문구 변형·치환·대기 포맷·자막 큐·RebuildFromState
        "^TutorialCharacterLookupTests$", // 캐릭터 판별(15 후속) — PrefabIdHash 로스터 역조회·실패 시 폴백 판정
        "^SpinnerPracticeModelTests$", // 스피너봇 연습(S9) — 모드·창 간격·연속 놓침·스폰 반경 클램프
        "^SpinnerComboModelTests$", // 스피너봇 연계 연습(v8 §3.5) — 간파→취약 유지→밀치기→벽 충돌→회복(그 자리) ×3·중복 방지
        "^TutorialZone1ModelTests$", // 존1(S7·v8) — 이동 보고 상한·이동 누적·툴팁 1초·착지·목표 순서·대기 줄·공격/스킬 사용 판정
        "^TutorialCooldownResetModelTests$", // 복도1 첫 진입 쿨타임 초기화(v8 C1-0) — 플레이어별 1회·예약·범위
        "^TutorialCorridorModelTests$", // 복도(S8) — 게이트 매핑·복도당 라운드 1번·진행 중 복도·목표 장면
        "^TutorialZone2ModelTests$", // 존2(S8) — 기상 지연·E 안내·Q/E 넛지·목표 줄
        "^TutorialZone3RulesTests$", // 존3 흐름(S9) — 시작 조건·간파 집계·빗나감/이른 간파/힌트·목표·연습 반경
        "^TutorialZone4RulesTests$", // 존4·보스 진입(S10) — 설정 자막→G8·기상 지연·패널/다리/간파 리마인드·만료·패드·도착 팁·목표/강조
        "^TutorialSkipPlanTests$", // 구간 스킵 Alt+1~0(할 일 34) — 단축키 매핑·뒤로 거부·목적 구간 → 게이트·체크·처치·자동 획득 계획
    };

    const string Tag = "[TutorialTests]";

    static TestRunnerApi _runner;

    [MenuItem("Tools/Tests/튜토리얼 EditMode 테스트 실행")]
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

            // 콜백은 전역 등록이라 해제하지 않으면 다른 메뉴의 테스트 실행에도 이 태그로 결과를 찍는다.
            _runner.UnregisterCallbacks(this);
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
