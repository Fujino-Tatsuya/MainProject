using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 튜토리얼 EditMode 테스트를 메뉴 한 번으로 돌리고 결과를 콘솔에 남긴다.
/// 패턴은 <see cref="TrainingDummyEditModeTestRunner"/> 를 따랐다. 튜토리얼 단계(S2~)의 새 픽스처는 여기에 추가한다.
/// </summary>
internal static class MapSceneEditModeTestRunner
{
    static readonly string[] Fixtures =
    {
        "^MapSceneFlowModelTests$",
        "^MapSceneGateHoldTests$", // 게이트 보류 — 4구역 소개 연출이 끝나야 G7(PLAN-zone4-intro-cinematic)
        "^MapSceneAreaTrackerTests$",
        "^MapSceneBarrierRulesTests$",
        "^MapSceneBackBarrierRulesTests$", // 개인 뒤 배리어(15 후속) — 지남 판정·마스크 유지·로컬 콜라이더 목록
        "^MapSceneMonsterLedgerTests$",
        "^MapSceneBossPathRulesTests$",
        "^MapSceneDeathRulesTests$", // 사망 규칙(S11) + 부활 지점 — 뒤 배리어 예외(C2)·보스 진입 패드 제외·G10 앞 폴백(C3)
        "^MapSceneSafePointRulesTests$", // 할 일 80 스텝별 고정 안전 지점 — 복귀 구간(카운트다운 = 보스 진입·엘리베이터 대기 = 기존·보스전 = 재합류 모서리)·지금 스텝 구간·기본 위치·점유 = 정적 장애물만·막히면 주변 링·링 실패 시 지점 그대로·복귀 높이
        "^MapSceneBossRetryTests$", // 할 일 30 (2) 전멸 리스폰 — 보스 재도전 되감기(G11 닫기·BossEnter)·뒤 배리어 마스크 재계산(복도4 최전방)
        "^TraitSelectionModelTests$", // 특성(T1) — 후보 뽑기·중복 제외·선택 검증·자동 획득·라운드 완료
        "^TraitWindowModelTests$", // 특성 창(T2) — 내 제시 번호(OfferSeq)로 열림·닫힘·닫힌 제시 재표시 금지·다른 사람 재선택에 안 닫힘·획득 토스트·문구
        "^TraitDeadlineModelTests$", // 특성 창 교착 방지 게이지(15 후속) — 비율·10초 강조·로컬 감소·재동기화
        "^TraitResetModelTests$", // 할 일 30 (3) 전멸 리스폰 뒤 특성 초기화 — 팝업 응답 검증·원복 장부·4라운드 재선택·재전멸·이탈
        "^TraitRejoinTests$", // 할 일 48 C14 R4c 판 도중 재합류 — 획득 복원(순서·중복·멱등)·라운드 참가·팝업/재선택 보관·복원
        "^TraitCatalogTests$", // 할 일 125 (1/5) 특성 ID 고정 해시·명세 고정표 36개·검증(중복·충돌·모르는 ID)·고정 후보 순서·목록 버전 비교
        "^TraitFixedSelectionTests$", // 할 일 125 (1/5) 고정 후보 선택 — 차수 = 획득 수+1·자동 선택 첫 번째·후보 0 = Empty·재선택 1차부터·재합류 해시 복원
        "^TutorialGuide(Text|TextSource|SubtitleQueue|Model)Tests$", // 안내 UI(S6) — 문구 변형·치환·대기 포맷·자막 큐·RebuildFromState · 86 T6a SO+Strings 출처
        "^TutorialCharacterLookupTests$", // 캐릭터 판별(15 후속) — PrefabIdHash 로스터 역조회·실패 시 폴백 판정
        // 107: 존1~3·스피너 연습·존4 교육 테스트(Zone1Model·Zone2Model·Zone3Rules·SpinnerPractice/ComboModel·Zone4Education)는 Legacy — 'EditMode 전체 실행'에서만 돈다.
        "^TutorialCooldownResetModelTests$", // 쿨타임 초기화 규칙(v8 C1-0 — 로비 TrainingPlayerReset 공용) — 플레이어별 1회·예약·범위
        "^MapSceneTraitCorridorModelTests$", // 복도(S8) — 게이트 매핑·복도당 라운드 1번·진행 중 복도·목표 장면
        "^MapSceneBossApproachRulesTests$", // 존4·보스 진입(S10) — 설정 자막→G8·기상 지연·다리·만료·패드·도착 팁·보스방 목표/미니맵 강조
        "^LobbyRadioTests$", // 로비 무전(할 일 86 T6b) — 단계 → 대사 표·회차당 1회·단계 바뀜 = 대기 줄 폐기·늦은 클라·자막 큐 ClearPending·문구 행
        "^LobbyBriefingTests$", // 로비 브리핑(할 일 86 T6c) — 대사 순서·서버 시간·완료 1회·중복 시작 무시·중단 해제 신호·잠금 장부·목표 표시·문구 행
        "^MapSceneSkipPlanTests$", // 구간 스킵 Alt+1~0(할 일 34) — 단축키 매핑·뒤로 거부(Alt+0 처음부터 예외)·재시작 재진입 거부·목적 구간 → 게이트·체크·처치·자동 획득 계획
    };

    const string Tag = "[MapSceneTests]";

    static TestRunnerApi _runner;

    [MenuItem("Tools/Tests/맵씬 EditMode 테스트 실행")]
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
